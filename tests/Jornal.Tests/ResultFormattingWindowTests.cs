using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultFormattingWindowTests
{
    [AvaloniaTheory]
    [InlineData(1280, 680, false)]
    [InlineData(1280, 680, true)]
    [InlineData(1366, 768, false)]
    [InlineData(1366, 768, true)]
    public async Task FormattingPaletteCopyAndPersistenceUseTheSameStyle(int width, int height, bool dark)
    {
        var store = new MemoryUiPreferencesStore();
        var model = new MainWindowViewModel(new DemoOrderStore()) { IsDarkTheme = dark,
            ResultText = "Лист 6x1500x3000,\tМатериал: 5083H111,\t3 шт.\tВес 48,2 кг.\tРезка 500 ₽.\tрезка Лазер\nКомментарий для письма" };
        var window = new MainWindow(store) { DataContext = model, Width = width, Height = height };
        try
        {
            window.Show(); Pump();
            var bar = window.FindControl<ResultFormattingBar>("ResultFormatting")!;
            var size = bar.FindControl<ComboBox>("ResultFontSize")!;
            var color = bar.FindControl<Button>("ResultColorButton")!;
            var text = window.FindControl<TextBox>("ResultTextBox")!;
            Assert.Equal(10m, size.SelectedItem);
            Assert.Equal(new decimal[] { 8, 10, 12, 14 }, size.ItemsSource!.Cast<decimal>());
            Assert.Equal(10d * 96 / 72, text.FontSize, 6);
            Assert.Equal(Color.Parse(ResultTextStyle.DefaultColor), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            Assert.Equal(Color.Parse("#F1F2F4"), Assert.IsAssignableFrom<ISolidColorBrush>(window.FindControl<Border>("ResultPanel")!.Background).Color);
            foreach (var control in new Control[] { bar, window.FindControl<JournalButton>("AddResultButton")!,
                         window.FindControl<JournalButton>("ClearResultButton")!, window.FindControl<JournalButton>("CopyResultButton")! })
            {
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= height && bounds.Right <= width);
            }
            var panel = window.FindControl<Border>("ResultPanel")!;
            Assert.Equal(106, window.FindControl<Grid>("ResultSlot")!.Bounds.Height);
            var inputBounds = new Rect(text.Bounds.Size).TransformToAABB(text.TransformToVisual(panel)!.Value);
            foreach (var control in new Control[] { bar, window.FindControl<StackPanel>("ResultActions")! })
            {
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(panel)!.Value);
                Assert.True(bounds.Bottom <= panel.Bounds.Height && bounds.Right <= panel.Bounds.Width);
                Assert.True(inputBounds.Right <= bounds.Left);
            }
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == "Результат");
            Save(window, $"format-default-{width}-{dark}");
            Click(size); Pump(); Assert.True(size.IsDropDownOpen);
            Save(window, $"format-size-list-{width}-{dark}");
            size.IsDropDownOpen = false;
            size.SelectedItem = 12m; Pump();
            Assert.Equal(12m, model.ResultFontSizePoints);
            Assert.Equal(12d * 96 / 72, text.FontSize, 6);
            Click(color); Pump();
            var flyout = Assert.IsType<Flyout>(color.Flyout);
            Assert.True(flyout.IsOpen);
            var palette = Assert.IsAssignableFrom<Control>(flyout.Content);
            var swatches = palette.GetVisualDescendants().OfType<Button>().Where(button => button.DataContext is ResultColorOption).ToArray();
            Assert.Equal(24, swatches.Length);
            Assert.Equal(ResultTextStyle.DefaultColor, Assert.Single(model.ResultColors, option => option.IsSelected).Hex);
            Save(TopLevel.GetTopLevel(palette)!, $"format-palette-{width}-{dark}");
            var blue = swatches.Single(button => button.DataContext is ResultColorOption { Hex: "#2F5496" });
            Click(blue); Pump();
            Assert.False(flyout.IsOpen); Assert.Equal("#2F5496", model.ResultTextColor);
            Assert.Equal(Color.Parse("#2F5496"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            var originalText = model.ResultText;
            await model.CopyResultAsync(new ResultClipboard(() => window.Clipboard));
            using (var copied = await window.Clipboard!.TryGetDataAsync())
            {
                Assert.NotNull(copied);
                Assert.Equal(originalText, await copied.TryGetTextAsync());
                Assert.Contains("font-size:12pt;color:#2F5496", Encoding.UTF8.GetString((await copied.TryGetValueAsync(ResultClipboardPayload.HtmlFormat))!));
                Assert.Contains(@"\fs24\cf1", Encoding.ASCII.GetString((await copied.TryGetValueAsync(ResultClipboardPayload.RtfFormat))!));
            }
            Save(window, $"format-changed-{width}-{dark}");
            Click(color); Pump();
            var popup = TopLevel.GetTopLevel(palette)!;
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            popup.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(flyout.IsOpen);
            model.IsCustomizing = true; Pump();
            Assert.False(size.IsEffectivelyEnabled); Assert.False(color.IsEffectivelyEnabled);
            model.IsCustomizing = false; Pump();
            Assert.Equal(originalText, model.ResultText);
        }
        finally { window.Close(); }
        Assert.Equal(12m, store.Value!.ResultFormatting.FontSizePoints);
        Assert.Equal("#2F5496", store.Value.ResultFormatting.ColorHex);
        var reopenedModel = new MainWindowViewModel(new DemoOrderStore());
        var reopened = new MainWindow(store) { DataContext = reopenedModel };
        try
        {
            reopened.Show(); Pump();
            Assert.Equal(12m, reopenedModel.ResultFontSizePoints);
            Assert.Equal("#2F5496", reopenedModel.ResultTextColor);
            Assert.Equal(dark, reopenedModel.IsDarkTheme);
        }
        finally { reopened.Close(); }
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Save(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1));
        bitmap.Render(root); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
