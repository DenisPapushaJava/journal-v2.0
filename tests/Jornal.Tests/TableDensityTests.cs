using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TableDensityTests
{
    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 1.25)]
    [InlineData(true, 1.25)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void DensityKeepsTextCheckboxesAndBadgesInsideRowsAtDifferentScales(bool dark, double scale)
    {
        var store = new MemoryUiPreferencesStore();
        store.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = 24 } });
        var model = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow(store) { DataContext = model, Width = 1366, Height = 768 };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            Assert.Empty(grid.SelectedItems);
            foreach (var (height, font) in new[] { (24, 12), (28, 13), (32, 13), (40, 14), (48, 14) })
            {
                window.FindControl<TextBox>("RowHeightInput")!.Text = height.ToString(); Pump();
                Assert.Equal(height, grid.RowHeight);
                Assert.Equal(font, grid.FontSize);
                var rows = grid.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(rows);
                foreach (var row in rows)
                {
                    // DataGrid добавляет линию-разделитель, округлённую до физического пикселя.
                    Assert.InRange(row.Bounds.Height, height - 0.01, height + Math.Ceiling(scale) / scale + 0.01);
                    var texts = row.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text)).ToArray();
                    Assert.NotEmpty(texts);
                    foreach (var text in texts)
                    {
                        Assert.Equal(font, text.FontSize);
                        var point = text.TranslatePoint(default, row)!.Value;
                        Assert.True(point.Y >= -0.1 && point.Y + text.Bounds.Height <= row.Bounds.Height + 0.1, $"Обрезан текст при {height}/{scale}: {text.Bounds}");
                    }
                    var box = row.GetVisualDescendants().OfType<CheckBox>().Single(check => check.Classes.Contains("row-selection"));
                    Assert.InRange(box.Bounds.Height, 24, height);
                    var outline = box.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "NormalRectangle");
                    var outlinePoint = outline.TranslatePoint(default, row)!.Value;
                    Assert.True(outlinePoint.Y >= 1 && outlinePoint.Y + outline.Bounds.Height <= row.Bounds.Height - 1,
                        $"Чекбокс обрезан при {height}/{scale}: контур {outlinePoint}/{outline.Bounds}, строка {row.Bounds}.");
                }
                grid.ScrollIntoView(model.Orders[0], grid.Columns.Last()); Pump();
                var badges = grid.GetVisualDescendants().OfType<StatusBadge>().Where(badge => badge.IsEffectivelyVisible).ToArray();
                Assert.NotEmpty(badges);
                Assert.All(badges, badge =>
                {
                    Assert.True(badge.Bounds.Height <= height - 4);
                    Assert.Equal(font, ((TextBlock)badge.Child!).FontSize);
                    Assert.True(badge.Child!.Bounds.Height <= badge.Bounds.Height);
                });
                grid.ScrollIntoView(model.Orders[0], grid.Columns[1]); Pump();
                if (height == 24) Screenshot(window, $"density-24-{scale}-{(dark ? "dark" : "light")}");
            }
            Assert.Empty(grid.SelectedItems);
            window.FindControl<TextBox>("RowHeightInput")!.Text = "24"; Pump();
            var checkbox = grid.GetVisualDescendants().OfType<CheckBox>().First(box => box.IsEffectivelyVisible && box.Classes.Contains("row-selection"));
            Click(window, checkbox);
            Assert.Single(grid.SelectedItems);
            model.SearchText = "нет совпадений"; Pump();
            Assert.True(model.ShowEmptyState);
            Assert.Equal(24, grid.RowHeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MenuInputLimitsPersistenceAndLayoutCancelWorkTogether()
    {
        var store = new MemoryUiPreferencesStore();
        var model = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow(store) { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.Columns[3].IsVisible = false;
            var menu = window.FindControl<JournalButton>("ColumnsButton")!;
            Click(window, menu);
            var input = window.FindControl<TextBox>("RowHeightInput")!;
            var popup = TopLevel.GetTopLevel(input)!;
            Click(popup, input);
            Keys(popup, Key.A, RawInputModifiers.Control);
            popup.KeyTextInput("2"); Pump(); Assert.Equal(32, grid.RowHeight);
            popup.KeyTextInput("4"); Pump(); Assert.Equal(24, grid.RowHeight);
            Assert.False(window.FindControl<JournalButton>("DecreaseRowHeightButton")!.IsEnabled);
            Screenshot(popup, "density-menu");
            input.Text = "0"; Keys(popup, Key.Enter); Assert.Equal(24, grid.RowHeight);
            input.Text = "999"; Keys(popup, Key.Enter); Assert.Equal(48, grid.RowHeight);
            Assert.False(window.FindControl<JournalButton>("IncreaseRowHeightButton")!.IsEnabled);
            input.Text = "bad"; Keys(popup, Key.Enter); Assert.Equal("48", input.Text);
            Click(popup, window.FindControl<JournalButton>("ResetRowHeightButton")!); Assert.Equal(32, grid.RowHeight);
            Click(popup, window.FindControl<JournalButton>("DecreaseRowHeightButton")!); Assert.Equal(31, grid.RowHeight);
            Click(popup, window.FindControl<JournalButton>("IncreaseRowHeightButton")!); Assert.Equal(32, grid.RowHeight);
            input.Text = "24"; Pump();
            menu.Flyout!.Hide(); Pump();
            grid.SelectedItem = model.Orders[0];
            model.ResultText = "Текст письма";
            Click(window, window.FindControl<JournalButton>("PersonalizeButton")!);
            Click(window, window.FindControl<JournalButton>("ResetLayoutButton")!);
            Assert.Equal(32, grid.RowHeight);
            Click(window, window.FindControl<JournalButton>("CancelLayoutButton")!);
            Assert.Equal(24, grid.RowHeight);
            Assert.False(grid.Columns[3].IsVisible);
            Assert.Single(grid.SelectedItems);
            Assert.Equal("Текст письма", model.ResultText);
        }
        finally { window.Close(); }
        Assert.Equal(24, store.Value!.Layout.RowHeight);
        var restored = new MainWindow(store) { DataContext = new MainWindowViewModel(new DemoOrderStore()) };
        try
        {
            restored.Show(); Pump();
            Assert.Equal(24, restored.FindControl<DataGrid>("OrdersGrid")!.RowHeight);
            Assert.Equal("24", restored.FindControl<TextBox>("RowHeightInput")!.Text);
            Assert.False(restored.FindControl<DataGrid>("OrdersGrid")!.Columns[3].IsVisible);
        }
        finally { restored.Close(); }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Keys(TopLevel root, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var physical = key == Key.A ? PhysicalKey.A : key == Key.Enter ? PhysicalKey.Enter : (PhysicalKey)0;
        root.KeyPress(key, modifiers, physical, null); root.KeyRelease(key, modifiers, physical, null); Pump();
    }
    private static void Click(TopLevel root, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Screenshot(TopLevel root, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var scale = root.RenderScaling;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(root.Bounds.Width * scale), (int)(root.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(root); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
