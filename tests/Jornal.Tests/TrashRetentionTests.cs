using System.Text.Json;
using Jornal.Desktop.Customization;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class TrashRetentionTests
{
    private static readonly TrashSource Source = new("mdb", Path.Combine(Path.GetTempPath(), "retention-test.mdb"));
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    public async Task EachDeletionKeepsItsPromisedRetentionAcrossSettingChanges(int days)
    {
        var clock = new Clock(); var backend = new OrderMaintenanceTests.Backend(); var store = new MemoryTrashStore();
        var service = new OrderTrashService(backend, Source, store, clock, days);
        var entry = await service.DeleteAsync(backend.Current!, TestContext.Current.CancellationToken);
        Assert.Equal(days, entry.RetentionPeriodDays);
        clock.Now = clock.Now.AddDays(days).AddTicks(-1);
        var otherSetting = new OrderTrashService(backend, Source, store, clock, days == 30 ? 90 : 30);
        Assert.Single(await otherSetting.RefreshAsync(false, TestContext.Current.CancellationToken));
        clock.Now = clock.Now.AddTicks(1);
        Assert.Empty(await otherSetting.RefreshAsync(false, TestContext.Current.CancellationToken));
        Assert.Equal(1, backend.Deletes); Assert.Equal(0, backend.Restores);
    }

    [Fact]
    public async Task OldJsonArchivesRetainThirtyDaysAndUncertainOutcomesNeverExpire()
    {
        var clock = new Clock(); var backend = new OrderMaintenanceTests.Backend(); var store = new MemoryTrashStore();
        var original = new TrashEntry(1, Guid.NewGuid(), Source, backend.Current!, clock.Now, TrashState.Deleted);
        var oldJson = JsonSerializer.Serialize(original);
        Assert.DoesNotContain("Retention", oldJson);
        var old = JsonSerializer.Deserialize<TrashEntry>(oldJson)!;
        using (var lease = await store.OpenAsync(Source, TestContext.Current.CancellationToken))
        {
            await lease.SaveAsync(old);
            await lease.SaveAsync(old with { OperationId = Guid.NewGuid(), State = TrashState.DeletePending, RetentionDays = 90 });
        }
        clock.Now = clock.Now.AddDays(500);
        var entries = await new OrderTrashService(backend, Source, store, clock, 90).RefreshAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(TrashState.DeletePending, Assert.Single(entries).State);
        Assert.Equal(0, backend.Deletes);
    }

    [Theory]
    [InlineData("null", "{}")]
    [InlineData("false", "15")]
    [InlineData("[]", "\"90\"")]
    public void BadNewPreferencesCannotEraseThemeLayoutOrLists(string automatic, string retention)
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-behavior-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui.json");
            File.WriteAllText(path, "{\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24},\"EntrySuggestions\":{\"Managers\":[\"Менеджер\"]},\"AutomaticJournalRefresh\":" + automatic + ",\"TrashRetentionDays\":" + retention + "}");
            var loaded = new JsonUiPreferencesStore(path).Load();
            Assert.Null(loaded.Error); var preferences = Assert.IsType<UiPreferences>(loaded.Preferences);
            Assert.True(preferences.DarkTheme); Assert.Equal(24, preferences.Layout.RowHeight);
            Assert.Equal(["Менеджер"], preferences.EntrySuggestions.Managers);
            Assert.Equal(30, preferences.TrashRetentionDays);
            Assert.Equal(automatic == "false" ? false : true, preferences.AutomaticJournalRefresh);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void OldProfilesDefaultToAutomaticRefreshAndNewChoicesSurviveRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-behavior-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui.json"); File.WriteAllText(path, "{}");
            var store = new JsonUiPreferencesStore(path); var loaded = store.Load().Preferences!;
            Assert.True(loaded.AutomaticJournalRefresh); Assert.Equal(30, loaded.TrashRetentionDays);
            store.Save(loaded with { AutomaticJournalRefresh = false, TrashRetentionDays = 90 });
            var saved = store.Load().Preferences!;
            Assert.False(saved.AutomaticJournalRefresh); Assert.Equal(90, saved.TrashRetentionDays);
        }
        finally { Directory.Delete(directory, true); }
    }
}
