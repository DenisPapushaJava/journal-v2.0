using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class PlainRowSelectionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false, 1, 1366, 768)]
    [InlineData(true, 1.5, 1366, 768)]
    [InlineData(false, 1, 1920, 1080)]
    [InlineData(true, 1, 1920, 1080)]
    public async Task SolidFillsAndNativeOutlinesKeepTextAndLayoutStable(bool dark, double scale, double width, double height)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var rows = Enumerable.Range(1, 150).Select(index => seed with { Id = index,
            SpecificationNumber = $"1019-{index:0000}", Color = index % 2 == 0 ? -25089 : unchecked((int)0xFFB9D5AC) }).ToArray();
        var model = new MainWindowViewModel(new Reader(rows));
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = 24 } });
        var window = new MainWindow(preferences) { DataContext = model, Width = width, Height = height };
        try
        {
            window.SetRenderScaling(scale); window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var first = grid.GetVisualDescendants().OfType<DataGridRow>().First(row => row.IsEffectivelyVisible);
            var firstBorder = first.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RowBorder");
            var firstOutline = first.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "BackgroundRectangle");
            Assert.False(firstOutline.IsVisible);
            Assert.IsAssignableFrom<ISolidColorBrush>(firstBorder.Background);
            var cell = first.GetVisualDescendants().OfType<DataGridCell>().First(cell => cell.Content is TextBlock);
            var point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); Pump(); Assert.IsAssignableFrom<ISolidColorBrush>(firstBorder.Background);
            Assert.False(firstOutline.IsVisible);
            window.MouseMove(new(1, 1)); Pump(); Assert.IsAssignableFrom<ISolidColorBrush>(firstBorder.Background);
            var firstBounds = first.Bounds; var firstCellBounds = cell.Bounds;
            foreach (var item in grid.CollectionView) grid.SelectedItems.Add(item);
            Pump();
            Assert.Equal(firstBounds, first.Bounds); Assert.Equal(firstCellBounds, cell.Bounds);
            Assert.Equal(150, grid.SelectedItems.Count);
            var visible = grid.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.IsEffectivelyVisible).ToArray();
            Assert.InRange(visible.Length, 2, 149);
            Assert.DoesNotContain(grid.GetVisualDescendants().OfType<Control>(), control => control.Name == "SelectionChrome");
            foreach (var row in visible)
            {
                Assert.True(row.IsSelected); Assert.Null(row.Effect);
                Assert.IsAssignableFrom<ISolidColorBrush>(row.Background);
                var border = row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RowBorder");
                Assert.Equal(row.Background, border.Background);
                Assert.IsAssignableFrom<ISolidColorBrush>(border.Background);
                Assert.Equal(default(BoxShadows), border.BoxShadow); Assert.Null(border.Effect);
                var outline = row.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "BackgroundRectangle");
                Assert.True(outline.IsVisible); Assert.False(outline.IsHitTestVisible);
                Assert.Null(outline.Fill);
                Assert.InRange(Assert.IsAssignableFrom<ISolidColorBrush>(outline.Stroke).Color.A, (byte)0x73, (byte)0x80);
                Assert.Equal(1, outline.Opacity); Assert.Equal(1, outline.StrokeThickness); Assert.Null(outline.Effect);
                Assert.True(row.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked);
            }
            var horizontal = grid.GetVisualDescendants().OfType<JournalScrollBar>().Single(bar => bar.Name == "PART_HorizontalScrollbar");
            var presenter = grid.GetVisualDescendants().OfType<DataGridRowsPresenter>().Single();
            Assert.InRange(Math.Abs(firstOutline.Bounds.Width + 24 - presenter.Bounds.Width), 0, .75);
            var frameOrigin = firstOutline.TranslatePoint(default, grid)!.Value;
            output.WriteLine($"Рамка {width}: {frameOrigin}, {firstOutline.Bounds.Size}; окно таблицы {grid.Bounds.Size}.");
            if (horizontal.IsVisible && horizontal.Maximum > 0)
            {
                horizontal.ScrollTo(horizontal.Maximum / 2); Pump();
                Assert.Equal(frameOrigin, firstOutline.TranslatePoint(default, grid)!.Value);
                horizontal.ScrollTo(0); Pump();
            }
            var green = visible.First(row => ((OrderRowViewModel)row.DataContext!).Color == unchecked((int)0xFFB9D5AC));
            var greenText = green.GetVisualDescendants().OfType<DataGridCell>().Select(cell => cell.Content).OfType<TextBlock>().First();
            var selectedText = Assert.IsAssignableFrom<ISolidColorBrush>(greenText.Foreground).Color;
            var selectedBackground = Assert.IsAssignableFrom<ISolidColorBrush>(green.Background).Color;
            var greenPoint = greenText.TranslatePoint(new Point(greenText.Bounds.Width / 2, greenText.Bounds.Height / 2), window)!.Value;
            window.MouseMove(greenPoint); Pump();
            Assert.True(green.IsPointerOver);
            Assert.Equal(selectedText, Assert.IsAssignableFrom<ISolidColorBrush>(greenText.Foreground).Color);
            var hoveredBackground = Assert.IsAssignableFrom<ISolidColorBrush>(green.Background).Color;
            Assert.NotEqual(selectedBackground, hoveredBackground);
            Assert.True(Jornal.Desktop.Tables.RowAppearance.Contrast(selectedText, hoveredBackground) >= 4.5);
            Snapshot(window, $"solid-selected-hover-{dark}-{scale}-{width}");
            window.MouseMove(new(1, 1)); Pump();
            Assert.Equal(selectedText, Assert.IsAssignableFrom<ISolidColorBrush>(greenText.Foreground).Color);
            Assert.Equal(selectedBackground, Assert.IsAssignableFrom<ISolidColorBrush>(green.Background).Color);
            Snapshot(window, $"solid-selected-{dark}-{scale}-{width}");
            if (Environment.GetEnvironmentVariable("JOURNAL_MEASURE_PLAIN_ROWS") == "1")
            {
                var borders = visible.Select(row => (Outline: row.GetVisualDescendants()
                    .OfType<Rectangle>().Single(rectangle => rectangle.Name == "BackgroundRectangle"),
                    Separator: row.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "PART_BottomGridLine"))).ToArray();
                var outlineTimes = new List<double>();
                var solidTimes = new List<double>();
                void SetOutline(bool outlined)
                {
                    foreach (var pair in borders)
                    {
                        if (outlined)
                        {
                            pair.Outline.ClearValue(Visual.IsVisibleProperty);
                            pair.Separator.ClearValue(Visual.OpacityProperty);
                        }
                        else { pair.Outline.IsVisible = false; pair.Separator.Opacity = .4; }
                    }
                    Pump();
                }
                // Warm both paths before collecting timings (JIT/fonts/render caches).
                foreach (var outlined in new[] { true, false })
                {
                    SetOutline(outlined);
                    Measure(window, $"{dark}, прогрев {(outlined ? "с рамкой" : "без рамки")}");
                }
                // Keep the selection, viewport and controls identical; change only the native row fill.
                // Alternate the measurement order to reduce warm-up and order bias.
                for (var round = 0; round < 3; round++)
                {
                    foreach (var outlined in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
                    {
                        SetOutline(outlined);
                        Assert.Equal(150, grid.SelectedItems.Count);
                        var median = Measure(window, $"{dark}, раунд {round + 1}: {(outlined ? "с рамкой" : "без рамки")}, выбрано 150, видимых строк {visible.Length}");
                        (outlined ? outlineTimes : solidTimes).Add(median);
                    }
                }
                foreach (var pair in borders)
                {
                    pair.Outline.ClearValue(Visual.IsVisibleProperty);
                    pair.Separator.ClearValue(Visual.OpacityProperty);
                }
                Pump();
                output.WriteLine($"Итого {dark}, {width}×{height}: средняя медиана с рамкой {outlineTimes.Average():F2} мс, без рамки {solidTimes.Average():F2} мс; разница {outlineTimes.Average() - solidTimes.Average():F2} мс ({100 * (outlineTimes.Average() / solidTimes.Average() - 1):F1}%).");
            }
            grid.SelectedItems.Clear(); Pump();
            foreach (var row in visible)
            {
                var item = Assert.IsType<OrderRowViewModel>(row.DataContext);
                Assert.Equal(Color.FromUInt32(unchecked((uint)item.Color!.Value)), Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);
                Assert.False(row.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked);
                Assert.False(row.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "BackgroundRectangle").IsVisible);
            }
            if (Environment.GetEnvironmentVariable("JOURNAL_MEASURE_PLAIN_ROWS") == "1") Measure(window, $"{dark}: без выбора");
            var items = grid.CollectionView.Cast<object>().ToArray();
            foreach (var index in new[] { 0, 1, 3 }) grid.SelectedItems.Add(items[index]);
            Pump();
            foreach (var row in visible)
                Assert.Equal(row.IsSelected, row.GetVisualDescendants().OfType<Rectangle>().Single(rectangle => rectangle.Name == "BackgroundRectangle").IsVisible);
            Snapshot(window, $"outline-mixed-{dark}-{scale}-{width}");
        }
        finally { window.Close(); }
    }

    private double Measure(Window window, string label)
    {
        var size = PixelSize.FromSize(window.ClientSize, 1);
        using var bitmap = new RenderTargetBitmap(size);
        using var buffer = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var frame = buffer.Lock();
        var samples = new List<double>();
        for (var iteration = 0; iteration < 22; iteration++)
        {
            var start = Stopwatch.GetTimestamp(); bitmap.Render(window); bitmap.CopyPixels(frame);
            if (iteration > 1) samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        samples.Sort();
        output.WriteLine($"Полная отрисовка окна {size}, {label}: медиана {samples[samples.Count / 2]:F2} мс, среднее {samples.Average():F2} мс (20 кадров, headless).");
        return samples[samples.Count / 2];
    }
    private static void Snapshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private sealed class Reader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows);
    }
}
