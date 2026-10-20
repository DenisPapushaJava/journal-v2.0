using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Core.Results;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultRowOrderTests
{
    [Fact]
    public void SelectionOrderChangesOnlyRowsAndAllFormatsKeepOneExtraTrailingLine()
    {
        var orders = new[] { ResultOutputTests.Order() with { Id = 5, ThicknessMillimeters = 8 },
            ResultOutputTests.Order() with { Id = 2, ThicknessMillimeters = 1 },
            ResultOutputTests.Order() with { Id = 8, ThicknessMillimeters = 4 } };
        var sorted = OrderResultFormatter.Format(orders);
        var chosen = OrderResultFormatter.Format(orders, rowOrder: ResultRowOrder.SelectionOrder);
        Assert.StartsWith("Лист 1x", sorted); Assert.StartsWith("Лист 8x", chosen);
        Assert.Equal(Lines(sorted).Order(), Lines(chosen).Order());
        Assert.EndsWith("\n\n", chosen); Assert.False(chosen.EndsWith("\n\n\n", StringComparison.Ordinal));
        var content = ResultClipboardContent.FromText(chosen);
        Assert.Equal(chosen, content.Text);
        Assert.Equal(4, Regex.Matches(content.Rtf, @"\\par ").Count);
        var root = Assert.Single(new HtmlParser().ParseDocument(content.Html).Body!.Children);
        Assert.Equal(5, root.Children.Length);
        Assert.Equal("\u00a0", root.LastElementChild!.TextContent);
        Assert.Single(root.Children[^2].QuerySelectorAll("br"));
        Assert.Equal(chosen, ResultClipboardContent.FromText(content.Text).Text);
        Assert.Equal(sorted, OrderResultFormatter.Format(orders, rowOrder: (ResultRowOrder)99));
        Assert.Empty(OrderResultFormatter.Format([], rowOrder: ResultRowOrder.SelectionOrder));
    }

    [Theory]
    [InlineData(null, ResultRowOrder.ThicknessAndMaterial)]
    [InlineData("null", ResultRowOrder.ThicknessAndMaterial)]
    [InlineData("[]", ResultRowOrder.ThicknessAndMaterial)]
    [InlineData("1", ResultRowOrder.ThicknessAndMaterial)]
    [InlineData("\"unknown\"", ResultRowOrder.ThicknessAndMaterial)]
    [InlineData("\"SelectionOrder\"", ResultRowOrder.SelectionOrder)]
    public void OldOrInvalidOrderPreferencesKeepTheRestOfTheProfile(string? raw, ResultRowOrder expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-result-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui.json");
            File.WriteAllText(path, "{\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24},\"LockOrderDate\":false" +
                (raw is null ? "" : ",\"ResultRowOrder\":" + raw) + "}");
            var store = new JsonUiPreferencesStore(path); var loaded = store.Load();
            Assert.Null(loaded.Error); var profile = Assert.IsType<UiPreferences>(loaded.Preferences);
            Assert.Equal(expected, profile.ResultRowOrder); Assert.True(profile.DarkTheme);
            Assert.Equal(24, profile.Layout.RowHeight); Assert.False(profile.LockOrderDate);
            store.Save(profile); Assert.Equal(expected, store.Load().Preferences!.ResultRowOrder);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingPreservesTheDraftAndSurvivesLayoutResetAndRestart(bool dark)
    {
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark });
        var model = new MainWindowViewModel(new DemoOrderStore());
        var owner = new MainWindow(preferences) { DataContext = model };
        try
        {
            owner.Show(); Pump();
            Assert.Equal(ResultRowOrder.ThicknessAndMaterial, model.ResultRowOrder);
            model.ResultText = "Ручной текст письма";
            using var settingsModel = new SettingsViewModel(model);
            var settings = new SettingsWindow { DataContext = settingsModel, Width = 800, Height = 580 };
            settings.Show(owner); Pump();
            var selector = settings.FindControl<ComboBox>("ResultRowOrderSelector")!;
            selector.BringIntoView(); Pump();
            Assert.True(selector.IsEffectivelyEnabled); Assert.Equal(0, selector.SelectedIndex);
            Assert.Equal(new[] { "По толщине и материалу", "По порядку выбора" }, selector.Items.Cast<string>());
            selector.SelectedIndex = 1; Pump();
            Assert.Equal(ResultRowOrder.SelectionOrder, model.ResultRowOrder);
            Assert.Equal("Ручной текст письма", model.ResultText);
            settings.Close();
            owner.FindControl<JournalButton>("PersonalizeButton")!.RaiseEvent(new(Button.ClickEvent)); Pump();
            owner.FindControl<JournalButton>("ResetLayoutButton")!.RaiseEvent(new(Button.ClickEvent)); Pump();
            owner.FindControl<JournalButton>("CancelLayoutButton")!.RaiseEvent(new(Button.ClickEvent)); Pump();
            Assert.Equal(ResultRowOrder.SelectionOrder, model.ResultRowOrder);
            Assert.Equal("Ручной текст письма", model.ResultText);
        }
        finally { foreach (var window in owner.OwnedWindows.ToArray()) window.Close(); owner.Close(); }
        Assert.Equal(ResultRowOrder.SelectionOrder, preferences.Value!.ResultRowOrder);
        var nextModel = new MainWindowViewModel(new DemoOrderStore());
        var next = new MainWindow(preferences) { DataContext = nextModel };
        try { next.Show(); Pump(); Assert.Equal(ResultRowOrder.SelectionOrder, nextModel.ResultRowOrder); }
        finally { next.Close(); }
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
