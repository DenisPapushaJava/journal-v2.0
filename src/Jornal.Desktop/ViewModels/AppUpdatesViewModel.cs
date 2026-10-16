using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Desktop.Updates;

namespace Jornal.Desktop.ViewModels;

public partial class AppUpdatesViewModel(IAppUpdater updater, TimeProvider? timeProvider = null) : ObservableObject
{
    public static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(6);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private AppUpdate? _update;
    private readonly object _progressLock = new();
    private int _downloadOperation;
    private bool _automaticChecksStarted;
    private long? _lastCheckCompleted;
    private Func<bool> _canRestart = () => false;
    private Action _prepareRestart = () => { };
    private Action _closeApplication = () => { };
    public bool CheckAutomatically { get; init; }
    public string CurrentVersion => updater.CurrentVersion;
    public bool IsInstalled => updater.IsInstalled;
    public string InstallationHint => OperatingSystem.IsWindows()
        ? "Для установки со всеми необходимыми компонентами используйте OfflineSetup.exe из раздела выпусков."
        : "Для обновления из программы запустите Journal из AppImage из раздела выпусков.";
    [ObservableProperty] private string _status = "Проверьте, появилась ли новая версия Journal.";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private bool _restartScheduled;
    [ObservableProperty] private bool _includePreview;
    [ObservableProperty] private bool _hasUpdate;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] private string _availableVersion = "";
    [ObservableProperty] private string _releaseNotes = "";
    public string NotificationText => HasUpdate ? $"Доступна новая версия Journal {AvailableVersion}" : "Обновления Journal";
    public bool HasError => Error is not null;
    public bool CanCheck => IsInstalled && !IsBusy;
    public bool CanDownload => HasUpdate && !IsDownloaded && !IsBusy;
    public bool CanInstall => HasUpdate && IsDownloaded && !IsBusy;
    [ObservableProperty] private bool _isDownloading;
    public void ConfigureRestart(Func<bool> canRestart, Action prepare, Action close)
    { _canRestart = canRestart; _prepareRestart = prepare; _closeApplication = close; }
    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnHasUpdateChanged(bool value) { OnPropertyChanged(nameof(NotificationText)); NotifyCommands(); }
    partial void OnAvailableVersionChanged(string value) => OnPropertyChanged(nameof(NotificationText));
    partial void OnIsDownloadedChanged(bool value) => NotifyCommands();
    partial void OnIncludePreviewChanged(bool value)
    {
        _update = null; HasUpdate = false; IsDownloaded = false; ReleaseNotes = ""; AvailableVersion = ""; Error = null;
        Status = "Канал изменён. Нажмите «Проверить обновления».";
    }
    private void NotifyCommands()
    {
        foreach (var name in new[] { nameof(CanCheck), nameof(CanDownload), nameof(CanInstall) }) OnPropertyChanged(name);
        CheckCommand.NotifyCanExecuteChanged(); DownloadCommand.NotifyCanExecuteChanged(); InstallCommand.NotifyCanExecuteChanged();
    }

    public async Task RunAutomaticChecksAsync(CancellationToken cancellationToken)
    {
        if (_automaticChecksStarted || !CheckAutomatically || !IsInstalled || cancellationToken.IsCancellationRequested) return;
        _automaticChecksStarted = true;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var remaining = _lastCheckCompleted is { } completed
                    ? AutomaticCheckInterval - _timeProvider.GetElapsedTime(completed)
                    : TimeSpan.Zero;
                if (remaining <= TimeSpan.Zero)
                {
                    // Все проверки разделяют IsBusy. Найденный пакет сохраняем до решения пользователя.
                    if (CanCheck && !HasUpdate) await CheckForUpdatesAsync(quiet: true, cancellationToken);
                    remaining = AutomaticCheckInterval;
                }
                // Один таймер без накопления пропущенных попыток после сна или отсутствия сети.
                await Task.Delay(remaining, _timeProvider, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    [RelayCommand(CanExecute = nameof(CanCheck), IncludeCancelCommand = true)]
    private Task CheckAsync(CancellationToken cancellationToken) => CheckForUpdatesAsync(quiet: false, cancellationToken);

    private async Task CheckForUpdatesAsync(bool quiet, CancellationToken cancellationToken)
    {
        if (!CanCheck || cancellationToken.IsCancellationRequested) return;
        IsBusy = true;
        if (!quiet) { Error = null; Status = "Проверяем выпуски на GitHub…"; }
        try
        {
            var update = await updater.CheckAsync(IncludePreview, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _update = update;
            Error = null;
            IsDownloaded = false;
            HasUpdate = _update is not null;
            AvailableVersion = _update?.Version ?? "";
            ReleaseNotes = string.IsNullOrWhiteSpace(_update?.Notes) ? "Описание изменений не добавлено." : _update.Notes;
            Status = HasUpdate ? $"Доступна версия {AvailableVersion}. Загрузка начнётся по вашей команде." : "Новых версий в выбранном канале не найдено.";
        }
        catch (OperationCanceledException) { if (!quiet) Status = "Проверка отменена."; }
        catch (Exception exception)
        {
            if (quiet) Trace.TraceWarning("Background update check failed: {0}", exception);
            else Fail(exception, "Не удалось проверить обновления. Проверьте интернет и повторите попытку. Можно продолжать работу с журналом.");
        }
        finally { _lastCheckCompleted = _timeProvider.GetTimestamp(); IsBusy = false; OnPropertyChanged(nameof(CurrentVersion)); }
    }

    [RelayCommand(CanExecute = nameof(CanDownload), IncludeCancelCommand = true)]
    private async Task DownloadAsync(CancellationToken cancellationToken)
    {
        if (!CanDownload || _update is null) return;
        IsBusy = true; Error = null; Status = "Загружаем обновление…";
        int operation;
        lock (_progressLock) { operation = ++_downloadOperation; IsDownloading = true; DownloadProgress = 0; }
        try
        {
            await updater.DownloadAsync(_update, new Progress<int>(value =>
            {
                lock (_progressLock)
                    if (operation == _downloadOperation && IsDownloading && !cancellationToken.IsCancellationRequested)
                        DownloadProgress = Math.Max(DownloadProgress, Math.Clamp(value, 0, 100));
            }), cancellationToken);
            lock (_progressLock) { IsDownloaded = true; DownloadProgress = 100; }
            Status = "Обновление загружено. Установите его, когда будете готовы перезапустить Journal.";
        }
        catch (OperationCanceledException) { Status = "Загрузка отменена. Её можно повторить."; }
        catch (Exception exception) { Fail(exception, "Не удалось загрузить или проверить пакет. Текущая версия продолжает работать. Повторите загрузку."); }
        finally { lock (_progressLock) { ++_downloadOperation; IsDownloading = false; } IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private void Install()
    {
        if (!CanInstall || _update is null) return;
        Error = null;
        if (!_canRestart()) { Error = "Завершите операции с журналом и закройте другие формы перед установкой."; return; }
        IsBusy = true; IsApplying = true;
        try
        {
            _prepareRestart();
            updater.ScheduleRestart(_update);
            RestartScheduled = true;
            _closeApplication();
        }
        catch (Exception exception) { Fail(exception, "Не удалось подготовить обновление. Journal остаётся открыт. " + (exception is InvalidOperationException ? exception.Message : "Проверьте доступ к папке приложения и профилю, затем повторите попытку.")); }
        finally { IsApplying = false; IsBusy = false; }
    }
    private void Fail(Exception exception, string message) { Trace.TraceWarning("App update failed: {0}", exception); Error = message; Status = "Обновление не выполнено."; }
}
