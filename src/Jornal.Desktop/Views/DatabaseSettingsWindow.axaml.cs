using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Local;

namespace Jornal.Desktop.Views;

public partial class DatabaseSettingsWindow : Window
{
    private DatabaseSettingsViewModel? _model;
    private readonly CancellationTokenSource _importCancellation = new();
    public DatabaseSettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.Connected -= OnConnected;
            _model = DataContext as DatabaseSettingsViewModel;
            if (_model is not null) _model.Connected += OnConnected;
        };
    }
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_model?.UseLocalCopy == true) LocalCopyPathInput.Focus();
        else if (_model?.UseDatabase == true) DatabasePathInput.Focus();
        else DatabaseModeButton.Focus();
    }
    protected override void OnClosed(EventArgs e)
    {
        if (_model is not null)
        {
            _model.ConnectCommand.Cancel();
            _model.Connected -= OnConnected;
        }
        _importCancellation.Cancel();
        if (_model?.IsImporting != true) _importCancellation.Dispose();
        base.OnClosed(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
    }
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConnected(object? sender, EventArgs e) => Close(true);

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not { } model || !StorageProvider.CanOpen) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите базу OrderDB.mdb", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("База Access (*.mdb)") { Patterns = ["*.mdb", "*.MDB"] }]
            });
            if (files.Count > 0 && IsVisible)
            {
                var path = files[0].TryGetLocalPath();
                if (path is null) model.Error = "Выберите файл в локальной или подключённой сетевой папке.";
                else { model.UseDatabase = true; model.DatabasePath = path; }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.Error = "Не удалось открыть выбор файла. Можно ввести полный путь вручную.";
        }
    }

    private static FilePickerFileType LocalCopyType => new("Локальная тестовая копия") { Patterns = ["*" + LocalJournalStore.Extension] };

    private async void OnBrowseLocalCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not { CanUseLocalCopy: true } model || !StorageProvider.CanOpen) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title = "Выберите локальную копию", AllowMultiple = false, FileTypeFilter = [LocalCopyType] });
            if (files.Count > 0 && IsVisible && files[0].TryGetLocalPath() is { } path)
            { model.UseLocalCopy = true; model.LocalCopyPath = path; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.Error = "Не удалось открыть выбор файла. Можно ввести полный путь вручную."; }
    }

    private async void OnImportLocalCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not { CanUseLocalCopy: true, CanChangeSource: true } model || !StorageProvider.CanOpen || !StorageProvider.CanSave) return;
        try
        {
            var sources = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите закрытую копию исходной MDB", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("База Access (*.mdb)") { Patterns = ["*.mdb", "*.MDB"] }]
            });
            if (!IsVisible || sources.Count == 0 || sources[0].TryGetLocalPath() is not { } source) return;
            var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохраните новую локальную тестовую копию",
                SuggestedFileName = "OrderDB-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + LocalJournalStore.Extension,
                FileTypeChoices = [LocalCopyType], ShowOverwritePrompt = true
            });
            if (IsVisible && destination?.TryGetLocalPath() is { } path)
                await model.ImportLocalCopyAsync(source, path, _importCancellation.Token);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { if (IsVisible) model.Error = "Не удалось открыть выбор файла. " + exception.Message; }
        finally { if (!IsVisible) _importCancellation.Dispose(); }
    }
}
