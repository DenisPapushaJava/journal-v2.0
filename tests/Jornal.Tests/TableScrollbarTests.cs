using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TableScrollbarTests
{
    [AvaloniaTheory]
    [InlineData(false, 32, 1)]
    [InlineData(true, 32, 1)]
    [InlineData(false, 24, 1.5)]
    [InlineData(true, 24, 1.5)]
    public async Task ScrollbarsReserveSpaceAndLastRowAndColumnRemainAccessible(bool dark, int height, double scale)
    {
        var (window, model, grid) = await Open(dark, height, scale);
        try
        {
            var rows = Part<DataGridRowsPresenter>(grid, "PART_RowsPresenter");
            var headers = Part<DataGridColumnHeadersPresenter>(grid, "PART_ColumnHeadersPresenter");
            var vertical = Part<ScrollBar>(grid, "PART_VerticalScrollbar");
            var horizontal = Part<ScrollBar>(grid, "PART_HorizontalScrollbar");
            Assert.True(vertical.IsVisible && vertical.Maximum > 0);
            Assert.True(horizontal.IsVisible && horizontal.Maximum > 0);
            Save(window, $"scrollbars-start-{height}-{scale}-{dark}");
            AssertSeparated(grid, rows, headers, vertical, horizontal);

            DragThumb(window, vertical, new Vector(0, vertical.Bounds.Height / 2));
            Assert.True(vertical.Value > 0);
            DragThumb(window, horizontal, new Vector(horizontal.Bounds.Width / 2, 0));
            Assert.True(horizontal.Value > 0);
            Assert.Empty(grid.SelectedItems);

            // Прокрутка и выбор сохраняют доступ к крайним данным, включая закреплённый чекбокс.
            grid.ScrollIntoView(model.Orders.Last(), grid.Columns.Last()); Pump();
            AssertSeparated(grid, rows, headers, vertical, horizontal);
            var last = grid.GetVisualDescendants().OfType<DataGridRow>()
                .Single(row => row.IsEffectivelyVisible && row.DataContext == model.Orders.Last());
            var rowBounds = Bounds(last, grid);
            Assert.True(rowBounds.Bottom <= Bounds(horizontal, grid).Top + 0.1);
            var cell = last.GetVisualDescendants().OfType<DataGridCell>()
                .Single(item => ReferenceEquals(DataGridColumn.GetColumnContainingElement(item), grid.Columns.Last()));
            // На дробном DPI граница ячейки округляется отдельно от области отсечения.
            Assert.True(Bounds(cell, grid).Right <= Bounds(vertical, grid).Left + 1 / scale,
                $"Последняя ячейка {Bounds(cell, grid)}, полоса {Bounds(vertical, grid)}, область {Bounds(rows, grid)}.");
            var checkbox = last.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Classes.Contains("row-selection"));
            Click(window, checkbox);
            Assert.Same(model.Orders.Last(), Assert.Single(grid.SelectedItems));
            Assert.True(checkbox.IsChecked);
            Assert.True(window.FindControl<Control>("ColumnsButton")!.IsEffectivelyVisible);
            Save(window, $"scrollbars-end-{height}-{scale}-{dark}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpaceIsReleasedWhenFilteringAndHidingColumnsRemovesOverflow(bool dark)
    {
        var (window, model, grid) = await Open(dark);
        try
        {
            var rows = Part<DataGridRowsPresenter>(grid, "PART_RowsPresenter");
            var vertical = Part<ScrollBar>(grid, "PART_VerticalScrollbar");
            var horizontal = Part<ScrollBar>(grid, "PART_HorizontalScrollbar");
            var initial = rows.Bounds.Size;
            model.SearchText = "Единственная заявка"; Pump();
            Assert.Single(model.Orders);
            Assert.False(vertical.IsVisible);
            Assert.True(rows.Bounds.Width > initial.Width);
            foreach (var column in grid.Columns.Skip(2)) column.IsVisible = false;
            Pump();
            Assert.False(horizontal.IsVisible);
            Assert.True(rows.Bounds.Height > initial.Height);
            Assert.InRange(Bounds(rows, grid).Right, grid.Bounds.Width - 0.1, grid.Bounds.Width + 0.1);
            Assert.InRange(Bounds(rows, grid).Bottom, grid.Bounds.Height - 0.1, grid.Bounds.Height + 0.1);

            // Переполнение возвращается после фильтра/изменения ширины без пересоздания таблицы.
            model.SearchText = "";
            foreach (var column in grid.Columns) column.IsVisible = true;
            window.Width = 1280; Pump();
            Assert.True(vertical.IsVisible && horizontal.IsVisible);
            AssertSeparated(grid, rows, Part<DataGridColumnHeadersPresenter>(grid, "PART_ColumnHeadersPresenter"), vertical, horizontal);
            model.SearchText = "Нет совпадений"; Pump();
            Assert.Empty(model.Orders);
            Assert.False(vertical.IsVisible);
        }
        finally { window.Close(); }
    }

    private static void AssertSeparated(DataGrid grid, Control rows, Control headers, ScrollBar vertical, ScrollBar horizontal)
    {
        var content = Bounds(rows, grid);
        var v = Bounds(vertical, grid);
        var h = Bounds(horizontal, grid);
        Assert.Equal(12, vertical.Bounds.Width, 4);
        Assert.Equal(12, horizontal.Bounds.Height, 4);
        // Частично видимые строки/столбцы не должны рисоваться за пределами своей области.
        Assert.True(rows.ClipToBounds); Assert.True(headers.ClipToBounds);
        Assert.True(content.Right <= v.Left + 0.1, $"Вертикальная полоса {v} перекрывает строки {content}.");
        Assert.True(Bounds(headers, grid).Right <= v.Left + 0.1, "Заголовки должны заканчиваться вместе с ячейками.");
        Assert.True(content.Bottom <= h.Top + 0.1, $"Горизонтальная полоса {h} перекрывает строки {content}.");
        Assert.True(h.Right <= v.Left + 0.1 && v.Bottom <= h.Top + 0.1, "Полосы не должны перекрывать друг друга.");
        Assert.False(vertical.AllowAutoHide); Assert.False(horizontal.AllowAutoHide);
    }

    private static async Task<(MainWindow, MainWindowViewModel, DataGrid)> Open(bool dark, int height = 32, double scale = 1)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var orders = Enumerable.Range(1, 80).Select(id => seed with
        {
            Id = id, SpecificationNumber = $"1017-{id:0000}",
            CustomerName = id == 1 ? "Единственная заявка" : $"Заказчик {id:00}"
        }).ToArray();
        var model = new MainWindowViewModel(new Reader(orders));
        var settings = new MemoryUiPreferencesStore(); settings.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = height } });
        var window = new MainWindow(settings) { DataContext = model, Width = 1366, Height = 768 };
        window.SetRenderScaling(scale); window.Show(); Pump();
        return (window, model, window.FindControl<DataGrid>("OrdersGrid")!);
    }
    private sealed class Reader(IReadOnlyList<OrderSummary> orders) : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(orders);
    }
    private static T Part<T>(DataGrid grid, string name) where T : Control => grid.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);
    private static Rect Bounds(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(MainWindow window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void DragThumb(MainWindow window, ScrollBar bar, Vector delta)
    {
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single(control => control.IsEffectivelyVisible);
        var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + delta);
        window.MouseUp(start + delta, MouseButton.Left); Pump();
    }
    private static void Save(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
