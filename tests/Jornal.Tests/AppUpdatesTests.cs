using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class AppUpdatesTests
{
    [Fact]
    public async Task StartupOnlyChecksOnceAndNeverDownloadsOrInstalls()
    {
        var updater = new FakeUpdater();
        var model = new AppUpdatesViewModel(updater) { CheckOnStartup = true };
        await model.CheckAtStartupAsync(); await model.CheckAtStartupAsync();
        Assert.Equal(1, updater.Checks); Assert.False(updater.Preview);
        Assert.True(model.HasUpdate); Assert.Contains("2026.10.0", model.NotificationText);
        Assert.True(model.CanDownload); Assert.False(model.CanInstall);
        Assert.Equal(0, updater.Downloads); Assert.Equal(0, updater.Restarts);
    }

    [Fact]
    public async Task UninstalledZipDoesNotMakeRequestsAndExplainsHowToInstall()
    {
        var updater = new FakeUpdater { IsInstalled = false };
        var model = new AppUpdatesViewModel(updater) { CheckOnStartup = true };
        await model.CheckAtStartupAsync(); await model.CheckCommand.ExecuteAsync(null);
        Assert.Equal(0, updater.Checks); Assert.False(model.CanCheck); Assert.False(model.HasUpdate);
        Assert.NotEmpty(model.InstallationHint);
    }

    [Fact]
    public async Task DownloadAndRestartAreSeparateAndPreparationHappensFirst()
    {
        var updater = new FakeUpdater(); var model = new AppUpdatesViewModel(updater);
        var steps = new List<string>(); updater.OnRestart = () => steps.Add("updater");
        model.ConfigureRestart(() => true, () => steps.Add("save"), () => steps.Add("close"));
        await model.CheckCommand.ExecuteAsync(null);
        model.InstallCommand.Execute(null); Assert.Empty(steps);
        await model.DownloadCommand.ExecuteAsync(null);
        Assert.True(model.IsDownloaded); Assert.True(model.HasUpdate); Assert.Equal(100, model.DownloadProgress);
        Assert.Equal(0, updater.Restarts);
        model.InstallCommand.Execute(null);
        Assert.Equal(new[] { "save", "updater", "close" }, steps);
        Assert.True(model.RestartScheduled);
    }

    [Fact]
    public async Task ActiveOperationAndFailedStateSavePreventRestartAndRemainRetryable()
    {
        var updater = new FakeUpdater(); var model = new AppUpdatesViewModel(updater);
        await model.CheckCommand.ExecuteAsync(null); await model.DownloadCommand.ExecuteAsync(null);
        model.ConfigureRestart(() => false, () => throw new Exception("Must not save"), () => throw new Exception("Must not close"));
        model.InstallCommand.Execute(null);
        Assert.True(model.HasError); Assert.Equal(0, updater.Restarts); Assert.True(model.CanInstall);
        model.ConfigureRestart(() => true, () => throw new IOException("Disk full"), () => throw new Exception("Must not close"));
        model.InstallCommand.Execute(null);
        Assert.Equal(0, updater.Restarts); Assert.False(model.RestartScheduled); Assert.True(model.CanInstall);
    }

    [Fact]
    public async Task OfflineAndDownloadFailuresKeepTheCurrentAppAndCanBeRetried()
    {
        var updater = new FakeUpdater { CheckError = new HttpRequestException() };
        var model = new AppUpdatesViewModel(updater) { CheckOnStartup = true };
        await model.CheckAtStartupAsync();
        Assert.True(model.HasError); Assert.False(model.HasUpdate); Assert.True(model.CanCheck);
        updater.CheckError = null;
        await model.CheckCommand.ExecuteAsync(null);
        updater.DownloadError = new InvalidDataException("Wrong checksum");
        await model.DownloadCommand.ExecuteAsync(null);
        Assert.True(model.CanDownload); Assert.False(model.CanInstall); Assert.True(model.HasError);
        updater.DownloadError = null;
        await model.DownloadCommand.ExecuteAsync(null);
        Assert.False(model.HasError); Assert.True(model.CanInstall); Assert.Equal(0, updater.Restarts);
        updater.CheckError = new HttpRequestException();
        await model.CheckCommand.ExecuteAsync(null);
        Assert.True(model.HasUpdate); Assert.True(model.IsDownloaded); Assert.True(model.CanInstall);
    }

    [Fact]
    public async Task PreviewOptInInvalidatesThePreviousPackageAndStableRemainsTheDefault()
    {
        var updater = new FakeUpdater(); var model = new AppUpdatesViewModel(updater);
        await model.CheckCommand.ExecuteAsync(null); await model.DownloadCommand.ExecuteAsync(null);
        model.IncludePreview = true;
        Assert.False(model.HasUpdate); Assert.False(model.CanInstall); Assert.False(model.IsDownloaded);
        await model.CheckCommand.ExecuteAsync(null); Assert.True(updater.Preview);
        updater.Update = null; model.IncludePreview = false;
        await model.CheckCommand.ExecuteAsync(null);
        Assert.False(model.HasUpdate); Assert.False(model.CanDownload); Assert.False(updater.Preview);
    }

    [Fact]
    public async Task CancelledDownloadDoesNotBecomeInstallable()
    {
        var updater = new FakeUpdater { BlockDownload = true }; var model = new AppUpdatesViewModel(updater);
        await model.CheckCommand.ExecuteAsync(null);
        var downloading = model.DownloadCommand.ExecuteAsync(null);
        Assert.True(model.IsDownloading); Assert.False(model.CanCheck); Assert.False(model.CanInstall);
        model.DownloadCommand.Cancel(); await downloading;
        Assert.False(model.IsDownloaded); Assert.True(model.CanDownload); Assert.Equal(0, updater.Restarts);
    }

    internal sealed class FakeUpdater : IAppUpdater
    {
        public string CurrentVersion => "2026.9.1";
        public bool IsInstalled { get; init; } = true;
        public AppUpdate? Update { get; set; } = new("2026.10.0", "Исправлен импорт длинных названий.\nСохранены привычные действия журнала.", 1234);
        public Exception? CheckError { get; set; }
        public Exception? DownloadError { get; set; }
        public bool BlockDownload { get; init; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public int Restarts { get; private set; }
        public bool Preview { get; private set; }
        public Action? OnRestart { get; set; }
        public Task<AppUpdate?> CheckAsync(bool preview, CancellationToken cancellationToken)
        { Checks++; Preview = preview; return CheckError is null ? Task.FromResult(Update) : Task.FromException<AppUpdate?>(CheckError); }
        public async Task DownloadAsync(AppUpdate update, IProgress<int> progress, CancellationToken cancellationToken)
        {
            Downloads++; progress.Report(50);
            if (DownloadError is not null) throw DownloadError;
            if (BlockDownload) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public void ScheduleRestart(AppUpdate update) { Restarts++; OnRestart?.Invoke(); }
    }
}
