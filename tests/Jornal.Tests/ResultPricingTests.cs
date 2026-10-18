using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultPricingTests
{
    [Theory]
    [InlineData(true, "900", "100", "900")]
    [InlineData(true, "900", "1250.5", "1250")]
    [InlineData(true, "0", "100.5", "100")]
    [InlineData(false, "900", "100", "100")]
    [InlineData(false, "900", "0", "0")]
    [InlineData(false, "900", "-10", "-10")]
    [InlineData(false, "900", "101.5", "102")]
    [InlineData(true, "79228162514264337593543950335", "100", "79228162514264337593543950335")]
    public void OnlyTheOutputPriceChangesAndAllClipboardFormatsAgree(bool enabled, string minimum, string stored, string expected)
    {
        var value = decimal.Parse(stored, CultureInfo.InvariantCulture);
        var order = ResultOutputTests.Order() with { StoredCuttingPrice = value };
        var settings = new ResultPriceSettings { UseMinimumPrice = enabled, MinimumPrice = decimal.Parse(minimum, CultureInfo.InvariantCulture) };
        var text = OrderResultFormatter.Format([order], settings);
        Assert.Equal(expected, Price(text));
        Assert.Equal(value, order.StoredCuttingPrice);
        Assert.Equal(WithoutPrice(OrderResultFormatter.Format([order])), WithoutPrice(text));
        var clipboard = ResultClipboardContent.FromText(text);
        Assert.Equal(text, clipboard.Text);
        Assert.Contains(expected + " ₽", new HtmlParser().ParseDocument(clipboard.Html).Body!.TextContent);
        Assert.Contains(expected, clipboard.Rtf);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimumIsPerRowAndNeverInventsAnUnknownPrice(bool enabled)
    {
        var first = ResultOutputTests.Order() with { StoredCuttingPrice = 100 };
        var second = first with { Id = 2, StoredCuttingPrice = 300 };
        var settings = new ResultPriceSettings { UseMinimumPrice = enabled, MinimumPrice = 700 };
        var lines = OrderResultFormatter.Format([first, second], settings).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { enabled ? "700" : "100", enabled ? "700" : "300" }, lines.Select(Price));
        Assert.Throws<ArgumentException>(() => OrderResultFormatter.Format([first with { StoredCuttingPrice = null }], settings));
    }

    [Theory]
    [InlineData("")]
    [InlineData(",\"ResultPricing\":null")]
    [InlineData(",\"ResultPricing\":\"bad\"")]
    [InlineData(",\"ResultPricing\":{\"MinimumPrice\":-10}")]
    [InlineData(",\"ResultPricing\":{\"MinimumPrice\":500.5}")]
    [InlineData(",\"ResultPricing\":{\"MinimumPrice\":1e999}")]
    [InlineData(",\"ResultPricing\":{\"MinimumPrice\":\"bad\"}")]
    [InlineData(",\"ResultPricing\":{\"MinimumPrice\":{},\"UseMinimumPrice\":\"bad\"}")]
    public void OldOrInvalidPricingKeepsTheRestOfTheProfileAndLegacyDefault(string fragment)
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-price-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24}" + fragment + "}");
            var loaded = new JsonUiPreferencesStore(path).Load();
            Assert.Null(loaded.Error); Assert.NotNull(loaded.Preferences);
            Assert.True(loaded.Preferences.DarkTheme); Assert.Equal(24, loaded.Preferences.Layout.RowHeight);
            Assert.Equal(new ResultPriceSettings(), loaded.Preferences.ResultPricing);
            var store = new JsonUiPreferencesStore(path);
            store.Save(loaded.Preferences with { ResultPricing = new() { UseMinimumPrice = false, MinimumPrice = 900 } });
            Assert.Equal(new ResultPriceSettings { UseMinimumPrice = false, MinimumPrice = 900 }, store.Load().Preferences!.ResultPricing);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ChangingTheRuleKeepsTheDraftUntilTheNextBuildAndDoesNotChangeTheOrder()
    {
        var model = new MainWindowViewModel(new DemoOrderStore());
        await model.ReloadCommand.ExecuteAsync(null);
        model.AcceptCreatedOrder(ResultOutputTests.Order() with { Id = 4, StoredCuttingPrice = 100 });
        var row = model.Orders.Single(item => item.Id == 4);
        model.ResultMinimumPriceText = "900";
        model.AddOrdersToResult([row]); Assert.Equal("900", Price(model.ResultText));
        model.ResultText = "Ручной текст письма";
        model.UseResultMinimumPrice = false;
        model.ResultMinimumPriceText = "1200";
        Assert.Equal("Ручной текст письма", model.ResultText);
        var clipboard = new RecordingClipboard();
        await model.CopyResultAsync(clipboard); Assert.Equal(model.ResultText, clipboard.Content!.Text);
        model.AddOrdersToResult([row]); Assert.Equal("100", Price(model.ResultText));
        model.UseResultMinimumPrice = true;
        model.AddOrdersToResult([row]); Assert.Equal("1200", Price(model.ResultText));
        Assert.Equal(100, row.StoredCuttingPrice);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneralSettingsRetainFiveSectionsAndPersistPricingSeparatelyFromLayout(bool dark)
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = 24 } });
        var (model, _, _) = OrderSourceTests.Create();
        var owner = new MainWindow(preferences) { DataContext = model };
        try
        {
            owner.Show(); Pump();
            Click(owner, "SettingsButton");
            var window = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            window.Width = 800; window.Height = 580; Pump();
            Assert.Equal(5, window.FindDescendant<ListBox>("SettingsNavigation")!.ItemCount);
            Assert.True(window.FindDescendant<CheckBox>("UseResultMinimumPriceCheckBox")!.IsChecked);
            var input = window.FindDescendant<TextBox>("ResultMinimumPriceInput")!;
            Assert.Equal("500", input.Text);
            input.Text = "900"; Pump(); Assert.Equal(900, model.ResultMinimumPrice);
            input.Text = null; Pump(); Assert.Equal(900, model.ResultMinimumPrice); Assert.False(model.HasResultMinimumPriceError);
            input.Text = "2+1"; Pump(); Assert.True(model.HasResultMinimumPriceError); Assert.Equal(900, model.ResultMinimumPrice);
            input.Focus(); Click(window, "UseResultMinimumPriceCheckBox");
            Assert.False(model.UseResultMinimumPrice); Assert.False(input.IsEffectivelyEnabled);
            Assert.Equal("900", input.Text); Assert.False(model.HasResultMinimumPriceError);
            Save(window, $"general-result-price-{dark}");
            Click(window, "CloseSettingsButton");
            Click(owner, "PersonalizeButton");
            model.ResultMinimumPriceText = "1200";
            owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(model.IsCustomizing); Assert.Equal(24, owner.FindDescendant<DataGrid>("OrdersGrid")!.RowHeight);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        Assert.Equal(new ResultPriceSettings { UseMinimumPrice = false, MinimumPrice = 1200 }, preferences.Value!.ResultPricing);
        var restarted = new MainWindowViewModel(new DemoOrderStore());
        var next = new MainWindow(preferences) { DataContext = restarted };
        try
        {
            next.Show(); Pump(); Assert.False(restarted.UseResultMinimumPrice); Assert.Equal("1200", restarted.ResultMinimumPriceText);
            Assert.Equal(24, next.FindDescendant<DataGrid>("OrdersGrid")!.RowHeight);
        }
        finally { next.Close(); }
    }

    private static string Price(string text) => Regex.Match(text, @"Резка\s+(-?\d+) ₽").Groups[1].Value;
    private static string WithoutPrice(string text) => Regex.Replace(text, @"Резка\s+-?\d+ ₽", "Резка <цена> ₽");
    private sealed class RecordingClipboard : IResultClipboard
    {
        public ResultClipboardContent? Content { get; private set; }
        public Task WriteAsync(ResultClipboardContent content) { Content = content; return Task.CompletedTask; }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, string name)
    {
        var control = window.FindDescendant<Control>(name)!;
        control.BringIntoView(); Pump();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
