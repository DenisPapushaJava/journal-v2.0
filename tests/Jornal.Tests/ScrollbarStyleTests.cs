using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ScrollbarStyleTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DropdownAlsoReservesANarrowLane(bool dark)
    {
        var combo = new ComboBox { ItemsSource = Enumerable.Range(1, 80).Select(id => $"Тариф {id}").ToArray(),
            SelectedIndex = 0, Width = 220, MaxDropDownHeight = 180 };
        var window = new Window { Content = combo, Width = 480, Height = 300,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); combo.IsDropDownOpen = true; Pump();
            var root = Part<Popup>(combo, "PART_Popup").Child!;
            var viewer = root.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var vertical = Part<ScrollBar>(viewer, "PART_VerticalScrollBar");
            Assert.False(viewer.AllowAutoHide); Assert.True(vertical.IsVisible);
            Assert.Equal(12, vertical.Bounds.Width, 4);
            Assert.True(Bounds(Part<ScrollContentPresenter>(viewer, "PART_ContentPresenter"), viewer).Right <= Bounds(vertical, viewer).Left + 0.1);
            Save(TopLevel.GetTopLevel(root)!, $"narrow-scrollbars-popup-{dark}");
        }
        finally { combo.IsDropDownOpen = false; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "viewer")]
    [InlineData(true, "viewer")]
    [InlineData(false, "text")]
    [InlineData(true, "text")]
    [InlineData(false, "list")]
    [InlineData(true, "list")]
    public void OrdinaryAndEmbeddedViewersReserveSpaceAndSupportDragging(bool dark, string kind)
    {
        Control control = kind switch
        {
            "text" => new TextBox { AcceptsReturn = true, Text = string.Join('\n', Enumerable.Repeat(new string('Ж', 160), 80)) },
            "list" => new ListBox { ItemsSource = Enumerable.Range(1, 80).Select(id => $"{id}: {new string('Ж', 160)}").ToArray() },
            _ => new ScrollViewer { Content = new Border { Width = 1600, Height = 2400, Child = new TextBlock { Text = "Последний столбец и строка доступны при прокрутке" } } }
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(control, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(control, ScrollBarVisibility.Auto);
        var window = new Window { Content = control, Width = 420, Height = 260,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            var viewer = control as ScrollViewer ?? control.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var vertical = Part<ScrollBar>(viewer, "PART_VerticalScrollBar");
            var horizontal = Part<ScrollBar>(viewer, "PART_HorizontalScrollBar");
            Assert.True(vertical.IsVisible && vertical.Maximum > 0);
            Assert.True(horizontal.IsVisible && horizontal.Maximum > 0);
            AssertViewerSeparated(viewer, vertical, horizontal);
            Save(window, $"narrow-scrollbars-{kind}-{dark}");
            AssertNarrow(window, vertical); AssertNarrow(window, horizontal);
            Drag(window, vertical, new Vector(0, 60));
            Drag(window, horizontal, new Vector(60, 0));
            Assert.True(viewer.Offset.X > 0 && viewer.Offset.Y > 0,
                $"После перетаскивания: Offset={viewer.Offset}, V={vertical.Value}, H={horizontal.Value}.");
            AssertViewerSeparated(viewer, vertical, horizontal);

            // После исчезновения переполнения дорожки возвращают место содержимому.
            var before = viewer.Viewport;
            if (control is TextBox text) text.Text = "Короткий текст";
            else if (control is ListBox list) list.ItemsSource = new[] { "Короткий список" };
            else viewer.Content = new TextBlock { Text = "Короткий текст" };
            Pump();
            Assert.False(vertical.IsVisible); Assert.False(horizontal.IsVisible);
            Assert.True(viewer.Viewport.Width > before.Width && viewer.Viewport.Height > before.Height);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    public void PriceGridKeepsLastInputAndRemoveButtonOutsideScrollbars(bool dark, double scale)
    {
        var store = new DemoOrderStore();
        var model = new PriceEditorViewModel(store.Prices, store.Prices, "Деможурнал", "Демонстрационные тарифы", PriceStorageKind.Session);
        var window = new PriceEditorWindow { DataContext = model, Width = 840, Height = 560,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); Pump(); model.BeginEditCommand.Execute(null);
            model.BeginAddRateCommand.Execute(null); model.NewThickness = "500"; model.NewRate = "20";
            model.StageRateCommand.Execute(null); Pump();
            Assert.Single(model.Rows, row => row.IsNew);
            var grid = window.FindControl<DataGrid>("PriceGrid")!;
            Save(window, $"prices-narrow-scrollbars-default-{dark}");
            // Принудительное горизонтальное переполнение проверяет обе дорожки и их угол.
            grid.Columns[2].Width = new DataGridLength(340);
            Pump();
            grid.ScrollIntoView(model.Rows.Last(), grid.Columns.Last()); Pump();
            var vertical = Part<ScrollBar>(grid, "PART_VerticalScrollbar");
            var horizontal = Part<ScrollBar>(grid, "PART_HorizontalScrollbar");
            Assert.True(vertical.IsVisible && horizontal.IsVisible);
            var rows = Bounds(Part<DataGridRowsPresenter>(grid, "PART_RowsPresenter"), grid);
            var headers = Bounds(Part<DataGridColumnHeadersPresenter>(grid, "PART_ColumnHeadersPresenter"), grid);
            var v = Bounds(vertical, grid); var h = Bounds(horizontal, grid);
            Assert.True(rows.Right <= v.Left + 0.1 && rows.Bottom <= h.Top + 0.1);
            Assert.True(headers.Right <= v.Left + 0.1);
            AssertNarrow(window, vertical); AssertNarrow(window, horizontal);
            grid.ScrollIntoView(model.Rows.Last(), grid.Columns.Last()); Pump();
            Save(window, $"prices-narrow-scrollbars-{scale}-{dark}");
            var realized = grid.GetVisualDescendants().OfType<DataGridRow>().ToArray();
            Assert.Contains(realized, row => row.DataContext == model.Rows.Last());
            var last = realized.Single(row => row.DataContext == model.Rows.Last());
            var remove = last.GetVisualDescendants().OfType<JournalButton>().Single(button => button.IsEffectivelyVisible);
            var button = Bounds(remove, grid);
            Assert.True(button.Right <= v.Left + 1 / scale && button.Bottom <= h.Top + 1 / scale);
            Save(window, $"prices-narrow-scrollbars-{scale}-{dark}");
            var center = remove.TranslatePoint(new Point(remove.Bounds.Width / 2, remove.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left); window.MouseUp(center, MouseButton.Left); Pump();
            Assert.DoesNotContain(model.Rows, row => row.IsNew);
        }
        finally { model.CancelEditCommand.Execute(null); window.Close(); }
    }

    private static void AssertViewerSeparated(ScrollViewer viewer, ScrollBar vertical, ScrollBar horizontal)
    {
        var content = Bounds(Part<ScrollContentPresenter>(viewer, "PART_ContentPresenter"), viewer);
        var v = Bounds(vertical, viewer); var h = Bounds(horizontal, viewer);
        Assert.False(viewer.AllowAutoHide);
        Assert.True(content.Right <= v.Left + 0.1 && content.Bottom <= h.Top + 0.1,
            $"Область {content} перекрыта полосами {v}, {h}.");
        Assert.True(h.Right <= v.Left + 0.1 && v.Bottom <= h.Top + 0.1);
    }

    private static void AssertNarrow(Window window, ScrollBar bar)
    {
        Assert.False(bar.AllowAutoHide);
        var vertical = bar.Orientation == Avalonia.Layout.Orientation.Vertical;
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        var ink = thumb.GetVisualDescendants().OfType<Border>().Single();
        Assert.Equal(12, vertical ? bar.Bounds.Width : bar.Bounds.Height, 4);
        Assert.Equal(12, vertical ? thumb.Bounds.Width : thumb.Bounds.Height, 4);
        Assert.InRange(vertical ? ink.Bounds.Width : ink.Bounds.Height, 6 - 1 / window.RenderScaling, 6 + 1 / window.RenderScaling);
        var before = bar.Bounds;
        window.MouseMove(bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value); Pump();
        Assert.Equal(before, bar.Bounds);
        Assert.InRange(vertical ? ink.Bounds.Width : ink.Bounds.Height, 6 - 1 / window.RenderScaling, 6 + 1 / window.RenderScaling);
    }
    private static void Drag(Window window, ScrollBar bar, Vector delta)
    {
        var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
        // Захват у края прозрачной части должен работать так же, как у видимого бегунка.
        var p = bar.Orientation == Avalonia.Layout.Orientation.Vertical ? new Point(1, thumb.Bounds.Height / 2) : new Point(thumb.Bounds.Width / 2, 1);
        var start = thumb.TranslatePoint(p, window)!.Value;
        window.MouseDown(start, MouseButton.Left); window.MouseMove(start + delta); window.MouseUp(start + delta, MouseButton.Left); Pump();
    }
    private static T Part<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(part => part.Name == name);
    private static Rect Bounds(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Save(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
