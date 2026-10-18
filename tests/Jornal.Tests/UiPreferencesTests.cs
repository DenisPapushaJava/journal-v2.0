using System.Text.Json;
using Jornal.Desktop.Customization;
using Jornal.Core.Results;
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
            store.Save(new UiPreferences { ResultFormatting = new ResultTextStyle { FontSizePoints = 12m, ColorHex = "#2F5496" },
                Layout = new ScreenLayout { ResultHeight = 216, RowHeight = 24, ColumnSizingVersion = ScreenLayout.CurrentColumnSizingVersion,
                PanelOrder = ["status", "orders", "clipboard"],
                Columns = [new("customer", 1, false, false, 250)] } });
            store.Save(store.Load().Preferences! with { DarkTheme = true });
            var loaded = store.Load();
            Assert.Null(loaded.Error);
            Assert.True(loaded.Preferences!.DarkTheme);
            Assert.Equal(216, loaded.Preferences.Layout.ResultHeight);
            Assert.Equal(24, loaded.Preferences.Layout.RowHeight);
            Assert.Equal(ScreenLayout.CurrentColumnSizingVersion, loaded.Preferences.Layout.ColumnSizingVersion);
            Assert.Equal("status", loaded.Preferences.Layout.PanelOrder[0]);
            Assert.Equal(new ResultTextStyle { FontSizePoints = 12m, ColorHex = "#2F5496" }, loaded.Preferences.ResultFormatting);
            Assert.Equal(new ColumnPreference("customer", 1, false, false, 250), Assert.Single(loaded.Preferences.Layout.Columns));
            Assert.Single(Directory.GetFiles(directory, "*", SearchOption.AllDirectories));
        });
    }

    [Theory]
    [InlineData("", "10")]
    [InlineData(",\"ResultFormatting\":null", "10")]
    [InlineData(",\"ResultFormatting\":{\"FontSizePoints\":999,\"ColorHex\":\"bad\"}", "10")]
    public void ExistingOrInvalidFormattingKeepsTheRestOfTheProfile(string formatting, string size)
    {
        InTemporaryFolder(directory =>
        {
            var path = Path.Combine(directory, "ui-settings.json");
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true,\"Layout\":{\"ResultHeight\":216}" + formatting + "}");
            var loaded = new JsonUiPreferencesStore(path).Load();
            Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme);
            Assert.Equal(216, loaded.Preferences.Layout.ResultHeight);
            Assert.Equal(32, loaded.Preferences.Layout.RowHeight);
            Assert.Equal(decimal.Parse(size, System.Globalization.CultureInfo.InvariantCulture), loaded.Preferences.ResultFormatting.FontSizePoints);
            Assert.Equal(ResultTextStyle.DefaultColor, loaded.Preferences.ResultFormatting.ColorHex);
        });
    }

    [Theory]
    [InlineData("24", 24)]
    [InlineData("48", 48)]
    [InlineData("25.5", 26)]
    [InlineData("0", 32)]
    [InlineData("-100", 32)]
    [InlineData("999", 32)]
    [InlineData("1e999", 32)]
    [InlineData("null", 32)]
    [InlineData("\"bad\"", 32)]
    [InlineData("{\"anything\":[1,2]}", 32)]
    public void InvalidRowHeightDoesNotDiscardOtherPreferences(string height, double expected)
    {
        InTemporaryFolder(directory =>
        {
            var path = Path.Combine(directory, "ui-settings.json");
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true,\"Layout\":{\"ResultHeight\":216,\"RowHeight\":" + height + "}}");
            var loaded = new JsonUiPreferencesStore(path).Load();
            Assert.Null(loaded.Error);
            Assert.True(loaded.Preferences!.DarkTheme);
            Assert.Equal(216, loaded.Preferences.Layout.ResultHeight);
            Assert.Equal(expected, loaded.Preferences.Layout.RowHeight);
        });
        Assert.Equal(32, TableDensity.NormalizeHeight(double.NaN));
        Assert.Equal(32, TableDensity.NormalizeHeight(double.PositiveInfinity));
        Assert.Equal(32, TableDensity.NormalizeHeight(double.NegativeInfinity));
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
        Assert.Equal(106, layout.ResultHeight);
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
