using System.Text.Json;
using Jornal.Desktop.Customization;
using Xunit;

namespace Jornal.Tests;

public sealed class UiPreferencesTests
{
    [Fact]
    public void ProfileRoundTripsAndReplacementLeavesNoTemporaryFile()
    {
        InTemporaryFolder(directory =>
        {
            var store = new JsonUiPreferencesStore(Path.Combine(directory, "profile", "ui-settings.json"));
            Assert.Null(store.Load().Preferences);
            store.Save(new UiPreferences { Layout = new ScreenLayout { ResultHeight = 216,
                PanelOrder = ["status", "orders", "clipboard"],
                Columns = [new("customer", 1, false, false, 250)] } });
            store.Save(store.Load().Preferences! with { DarkTheme = true });
            var loaded = store.Load();
            Assert.Null(loaded.Error);
            Assert.True(loaded.Preferences!.DarkTheme);
            Assert.Equal(216, loaded.Preferences.Layout.ResultHeight);
            Assert.Equal("status", loaded.Preferences.Layout.PanelOrder[0]);
            Assert.Equal(new ColumnPreference("customer", 1, false, false, 250), Assert.Single(loaded.Preferences.Layout.Columns));
            Assert.Single(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        });
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Version\":42}")]
    [InlineData("{\"Version\":1,\"Layout\":null}")]
    public void BrokenOrFutureProfileReportsErrorAndRemainsUntouched(string content)
    {
        InTemporaryFolder(directory =>
        {
            var path = Path.Combine(directory, "ui-settings.json");
            File.WriteAllText(path, content);
            var result = new JsonUiPreferencesStore(path).Load();
            Assert.Null(result.Preferences);
            Assert.NotEmpty(result.Error!);
            Assert.Equal(content, File.ReadAllText(path));
        });
    }

    [Fact]
    public void PartialProfileRestoresKnownPanelsAndRejectsInvalidNumbers()
    {
        var layout = new ScreenLayout
        {
            ResultHeight = double.NaN,
            PanelOrder = ["unknown", "status", "status"],
            PanelWidths = new() { ["orders"] = -100, ["clipboard"] = 600 },
            Columns = [new("date", 1, true, false, 120), new("date", 2, false, false, 200),
                new("bad", 0, true, false, 100), new("nan", 2, true, false, double.NaN)]
        }.Normalize();
        Assert.Equal(152, layout.ResultHeight);
        Assert.Equal(new[] { "status", "orders", "clipboard" }, layout.PanelOrder);
        Assert.Equal(380, layout.PanelWidths["orders"]);
        Assert.Equal(600, layout.PanelWidths["clipboard"]);
        Assert.Equal("date", Assert.Single(layout.Columns).Id);
        var profile = JsonSerializer.Deserialize<UiPreferences>("{\"Layout\":{\"PanelOrder\":null,\"PanelWidths\":null,\"Columns\":null}}")!;
        Assert.Equal(3, profile.Layout.Normalize().PanelOrder.Length);
    }

    [Fact]
    public void FailedReplacementRetainsPreviousProfileAndCleansTemporaryFile()
    {
        InTemporaryFolder(directory =>
        {
            var path = Path.Combine(directory, "ui-settings.json");
            var store = new JsonUiPreferencesStore(path);
            store.Save(new UiPreferences());
            File.Move(path, path + ".backup");
            Directory.CreateDirectory(path);
            var exception = Record.Exception(() => store.Save(new UiPreferences { DarkTheme = true }));
            Assert.True(exception is IOException or UnauthorizedAccessException);
            Assert.False(new JsonUiPreferencesStore(path + ".backup").Load().Preferences!.DarkTheme);
            Assert.Empty(Directory.GetFiles(directory, ".ui-settings-*.tmp"));
        });
    }

    private static void InTemporaryFolder(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-profile-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
