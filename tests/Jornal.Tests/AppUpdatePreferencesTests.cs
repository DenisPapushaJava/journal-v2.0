using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class AppUpdatePreferencesTests
{
    [Theory]
    [InlineData("2026.1.0-alpha.2", true)]
    [InlineData("2026.1.0-beta.1+commit", true)]
    [InlineData("2026.1.0-rc.1", true)]
    [InlineData("2026.1.0", false)]
    [InlineData("2026.1.0+commit", false)]
    [InlineData("неизвестна", false)]
    public void FirstLaunchRemembersTheInstalledChannel(string version, bool preview)
    {
        using var fixture = new Fixture();
        var model = fixture.Model(version);
        Assert.Equal(preview, model.IncludePreview);
        Assert.Equal(preview, fixture.Store().Load().Preferences!.IncludePreview);
        Assert.False(model.HasPreferencesWarning);
    }

    [Theory]
    [InlineData(true, "2026.1.0")]
    [InlineData(false, "2026.1.0-alpha.2")]
    public async Task ExplicitChoiceSurvivesRestartAndVersionChangesAndControlsTheStartupCheck(bool preview, string nextVersion)
    {
        using var fixture = new Fixture();
        var model = fixture.Model();
        model.IncludePreview = !preview;
        model.IncludePreview = preview;
        var updater = new AppUpdatesTests.FakeUpdater { CurrentVersion = nextVersion };
        var restarted = new AppUpdatesViewModel(updater, preferencesStore: fixture.Store()) { CheckAutomatically = true };
        Assert.Equal(preview, restarted.IncludePreview);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var checks = restarted.RunAutomaticChecksAsync(lifetime.Token);
        try
        {
            Assert.Equal(1, updater.Checks);
            Assert.Equal(preview, updater.Preview);
            Assert.Equal(0, updater.Downloads);
            Assert.Equal(0, updater.Restarts);
        }
        finally { lifetime.Cancel(); await checks; }
        Assert.Equal(new[] { "update-settings.json" }, Directory.GetFiles(fixture.Directory).Select(Path.GetFileName));
    }

    [Fact]
    public void DevelopmentLaunchDoesNotOptIntoPreviewByVersionAlone()
    {
        using var fixture = new Fixture();
        var model = new AppUpdatesViewModel(new AppUpdatesTests.FakeUpdater { CurrentVersion = "2026.1.0-dev", IsInstalled = false },
            preferencesStore: fixture.Store());
        Assert.False(model.IncludePreview);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Version\":\"1\",\"IncludePreview\":true}")]
    [InlineData("{\"Version\":999999999999,\"IncludePreview\":true}")]
    [InlineData("{\"Version\":2,\"IncludePreview\":true}")]
    [InlineData("{\"Version\":1,\"IncludePreview\":\"true\"}")]
    [InlineData("{\"Version\":1,\"IncludePreview\":null}")]
    public async Task DamagedOrUnknownSettingsFallBackToStableWithoutOverwritingAndCanBeRepaired(string content)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, content);
        var model = fixture.Model("2026.1.0-alpha.2");
        Assert.False(model.IncludePreview);
        Assert.True(model.HasPreferencesWarning);
        Assert.Equal(content, File.ReadAllText(fixture.Path));
        await model.CheckCommand.ExecuteAsync(null);
        Assert.True(model.HasPreferencesWarning);
        Assert.True(model.HasUpdate);
        model.IncludePreview = true;
        Assert.False(model.HasPreferencesWarning);
        Assert.True(fixture.Store().Load().Preferences!.IncludePreview);
    }

    [Fact]
    public void OversizedSettingsAndReadErrorsDoNotPreventStartup()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Path, new string(' ', 16 * 1024 + 1));
        Assert.True(fixture.Model().HasPreferencesWarning);
        File.Delete(fixture.Path);
        Directory.CreateDirectory(fixture.Path);
        var model = fixture.Model("2026.1.0-alpha.2");
        Assert.True(model.HasPreferencesWarning);
        Assert.False(model.IncludePreview);
    }

    [Fact]
    public async Task FailedSaveKeepsSessionChoiceAndExplainsItWithoutBreakingChecks()
    {
        var store = new FailingStore();
        var updater = new AppUpdatesTests.FakeUpdater();
        var model = new AppUpdatesViewModel(updater, preferencesStore: store);
        model.IncludePreview = true;
        Assert.True(model.IncludePreview);
        Assert.True(model.HasPreferencesWarning);
        await model.CheckCommand.ExecuteAsync(null);
        Assert.True(updater.Preview);
        Assert.True(model.HasPreferencesWarning);
        Assert.False(store.Preferences.IncludePreview);
        store.Fail = false;
        model.IncludePreview = false;
        model.IncludePreview = true;
        Assert.True(store.Preferences.IncludePreview);
        Assert.False(model.HasPreferencesWarning);
    }

    [Fact]
    public void FailedAtomicReplacementCleansTemporaryFileAndPreservesTheDestination()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(fixture.Path);
        var sentinel = System.IO.Path.Combine(fixture.Path, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        var error = Record.Exception(() => fixture.Store().Save(new(IncludePreview: true)));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("keep", File.ReadAllText(sentinel));
        Assert.Empty(Directory.GetFiles(fixture.Directory));
    }

    private sealed class FailingStore : IAppUpdatePreferencesStore
    {
        public AppUpdatePreferences Preferences { get; private set; } = new();
        public bool Fail { get; set; } = true;
        public AppUpdatePreferencesLoad Load() => new(Preferences);
        public void Save(AppUpdatePreferences preferences)
        {
            if (Fail) throw new UnauthorizedAccessException();
            Preferences = preferences;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "journal-update-preferences-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Directory, "update-settings.json");
        public Fixture() => System.IO.Directory.CreateDirectory(Directory);
        public JsonAppUpdatePreferencesStore Store() => new(Path);
        public AppUpdatesViewModel Model(string version = "2026.1.0") => new(new AppUpdatesTests.FakeUpdater { CurrentVersion = version }, preferencesStore: Store());
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
