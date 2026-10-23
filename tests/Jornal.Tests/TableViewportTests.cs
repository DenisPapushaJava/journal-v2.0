using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Core.Orders;
using Xunit;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Jornal.Tests;

public sealed class TableViewportTests
{
    [AvaloniaTheory]
    [InlineData(false, 1920)] [InlineData(true, 1920)]
    [InlineData(false, 1900)] [InlineData(true, 1900)]
    public void FullHdShowsAllColumnsAndFilterHeadersWithoutHorizontalScrolling(bool dark, int width)
    {
        var store = new MemoryUiPreferencesStore(); store.Save(new() { DarkTheme = dark });
        var first = LinkedColumnFilterTests.Rows()[0] with
        {
            SpecificationNumber = "1018-1234", ManagerName = "Вахреева", CustomerName = "ООО «МАСТЭНЕРГО»", MaterialName = "AISI-430",
            CuttingType = "HansLaser", ThicknessMillimeters = 32, SheetSize = "1500x3000", SheetCount = "253", CutLengthMeters = 1234,
            WeightKilograms = "4477,4", StoredCuttingPrice = 76012, DeliveryNumber = "9285"
        };
        first = first with { Annotations = OrderAnnotations.For(first, true, "Гибка после резки"), CuttingTime = OrderCuttingTime.For(first, 2207) };
        var model = new MainWindowViewModel(new LinkedColumnFilterTests.Reader { Items = Enumerable.Range(1, 40).Select(id => first with { Id = id }).ToArray() });
        var window = new MainWindow(store) { DataContext = model, Width = width, Height = 1040 };
        try
        {
            window.Show(); Pump(); var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            Assert.Equal(19, grid.Columns.Count); Assert.All(grid.Columns, column => Assert.True(column.IsVisible)); AssertFilled(grid);
            Assert.Null(window.FindControl<JournalButton>("FitTableToViewportButton"));
            foreach (var column in grid.Columns.Skip(1))
            {
                var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(item => Equals(item.Content, column.Header));
                ClickAt(window, header.TranslatePoint(new Point(12, header.Bounds.Height / 2), window)!.Value);
                AssertFilled(grid);
                var caption = header.GetVisualDescendants().OfType<TextBlock>().Single(item => item.IsEffectivelyVisible && Equals(item.Text, column.Header));
                if (header.GetVisualDescendants().OfType<JournalButton>().FirstOrDefault() is { } trigger)
                {
                    var natural = new TextBlock { Text = caption.Text, FontFamily = caption.FontFamily, FontSize = caption.FontSize, FontWeight = caption.FontWeight };
                    natural.Measure(Size.Infinity);
                    Assert.True(caption.Bounds.Width >= natural.DesiredSize.Width - 0.5, $"{column.Header}: {caption.Bounds.Width} < {natural.DesiredSize.Width}");
                    var buttonBounds = new Rect(trigger.Bounds.Size).TransformToAABB(trigger.TransformToVisual(header)!.Value);
                    var sort = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(item => item.Name == "SortIcon");
                    Assert.True(sort.IsVisible);
                    var sortBounds = new Rect(sort.Bounds.Size).TransformToAABB(sort.TransformToVisual(header)!.Value);
                    Assert.True(buttonBounds.Right <= sortBounds.Left + 0.5, $"Перекрытие фильтра/сортировки: {column.Header}");
                    Assert.True(buttonBounds.Right <= header.Bounds.Width);
                }
            }
            string[] numeric = ["Дата", "Листы", "Рез, м", "Вес, кг", "Стоимость", "Время резки"];
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(item => item.IsEffectivelyVisible);
            foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible &&
                         DataGridColumn.GetColumnContainingElement(text)?.Header is string name && numeric.Contains(name)))
                Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
            if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory); using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
                bitmap.Render(window); bitmap.Save(System.IO.Path.Combine(directory, $"full-hd-table-{dark}-{width}.png"), new PngBitmapEncoderOptions());
            }
            model.SearchText = "нет подходящих заявок"; Pump(); Assert.Empty(model.Orders); AssertFilled(grid);
            model.SearchText = ""; Pump(); Assert.Equal(40, model.Orders.Count); AssertFilled(grid);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1.5)]
    public void DefaultColumnsFillViewportAndGrowShrinkAndHideWithinTheirMinimums(bool dark, double scale)
    {
        var (window, model, grid) = Open(dark, scale);
        try
        {
            AssertOpaqueFrame(window);
            Assert.False(grid.Columns[0].Width.IsStar); Assert.Equal(24, grid.Columns[0].ActualWidth);
            Assert.True(Bar(grid).IsVisible); Assert.All(grid.Columns.Where(column => column.IsVisible), column => Assert.True(column.ActualWidth >= column.MinWidth - 1));
            var visible = new[] { "Менеджер", "Заказчик", "Материал" };
            foreach (var column in grid.Columns.Skip(1)) column.IsVisible = visible.Contains(column.Header);
            Pump(); AssertFilled(grid); var before = grid.Columns.Where(column => column.IsVisible).Select(column => column.ActualWidth).ToArray();
            grid.SelectedItems.Add(model.Orders[0]); model.ResultText = "Письмо";
            window.Width = 1800; Pump(); AssertFilled(grid); AssertOpaqueFrame(window);
            Assert.True(grid.Columns.Single(column => column.Header is "Заказчик").ActualWidth > before[2]);
            Assert.Single(grid.SelectedItems); Assert.Equal("Письмо", model.ResultText);
            grid.Columns.Single(column => column.Header is "Материал").IsVisible = false; Pump(); AssertFilled(grid);
            window.Width = 1280; Pump(); AssertFilled(grid); Assert.Equal(24, grid.Columns[0].ActualWidth); AssertOpaqueFrame(window);
            foreach (var column in grid.Columns) column.IsVisible = true; Pump();
            Assert.True(Bar(grid).IsVisible && Bar(grid).Maximum > 0);
            Assert.All(grid.Columns, column => Assert.True(column.ActualWidth >= column.MinWidth - 1));
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ReapplyingTemplateUpdatesTheScrollbarUsedForColumnWidths(bool dark)
    {
        var store = new MemoryUiPreferencesStore(); store.Save(new() { DarkTheme = dark });
        var reader = new LinkedColumnFilterTests.Reader { Items = Enumerable.Range(1, 40)
            .Select(id => LinkedColumnFilterTests.Rows()[0] with { Id = id }).ToArray() };
        var model = new MainWindowViewModel(reader);
        var window = new MainWindow(store) { DataContext = model, Width = 1920, Height = 768 };
        try
        {
            window.Show(); Pump(); var grid = window.FindControl<DataGrid>("OrdersGrid")!; AssertFilled(grid);
            var previous = grid.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Name == "PART_VerticalScrollbar");
            var template = grid.Template;
            grid.Template = null; Pump(); grid.Template = template; Pump();
            var current = grid.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Name == "PART_VerticalScrollbar");
            Assert.NotSame(previous, current); Assert.DoesNotContain(grid, previous.GetVisualAncestors());
            Assert.True(current.IsVisible); AssertFilled(grid);
            model.SearchText = "нет подходящих заявок"; Pump();
            Assert.Empty(model.Orders); Assert.False(current.IsVisible); AssertFilled(grid);
            model.SearchText = ""; model.IsDarkTheme = !dark; Pump();
            Assert.NotEmpty(model.Orders); AssertFilled(grid);
            window.Width = 1280; Pump(); Assert.True(Bar(grid).IsVisible);
            window.Width = 1920; Pump(); AssertFilled(grid);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void LegacyPixelWidthsAutomaticallyBecomeRelativeAndSurviveRestart(bool dark)
    {
        var store = new MemoryUiPreferencesStore(); store.Save(new() { DarkTheme = dark, Layout = new()
        {
            Columns = [new("manager", 3, true, false, 200), new("customer", 4, true, false, 260), new("material", 6, true, false, 160)]
        } });
        var (window, _, grid) = Open(dark, store: store);
        try
        {
            Assert.Null(window.FindControl<JournalButton>("FitTableToViewportButton"));
            foreach (var column in grid.Columns.Skip(1)) column.IsVisible = column.Header is "Менеджер" or "Заказчик" or "Материал";
            Pump(); var columns = grid.Columns.Where(column => column.IsVisible).Skip(1).ToArray();
            AssertFilled(grid);
            var ratio = columns[1].ActualWidth / columns[0].ActualWidth; Assert.InRange(ratio, 1.29, 1.31);
            window.Width = 1600; Pump(); AssertFilled(grid); Assert.InRange(columns[1].ActualWidth / columns[0].ActualWidth, 1.29, 1.31);
            window.Close(); Pump();
            Assert.Equal(ScreenLayout.CurrentColumnSizingVersion, store.Value!.Layout.ColumnSizingVersion);
            Assert.All(store.Value.Layout.Columns.Where(column => column.Visible), column => Assert.True(column.Star));
            var (reopened, _, restored) = Open(dark, store: store);
            try
            {
                AssertFilled(restored);
                Assert.Equal(19, restored.Columns.Count);
            }
            finally { reopened.Close(); Pump(); }
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void ManualResizeDuringCustomizationCanBeCanceledAndEmptyVisibilityIsSafe()
    {
        var (window, _, grid) = Open(false);
        try
        {
            foreach (var item in grid.Columns.Skip(1)) item.IsVisible = item.Header is "Менеджер" or "Заказчик" or "Материал";
            Pump();
            var column = grid.Columns.Single(item => item.Header is "Менеджер"); column.Width = new DataGridLength(200); Pump();
            Click(window.FindControl<JournalButton>("PersonalizeButton")!); column.Width = new DataGridLength(260); Pump();
            Assert.Equal(260, column.ActualWidth);
            // Кнопка отмены режима возвращает прежние пиксельные размеры.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(column.Width.IsStar); Assert.Equal(200, column.Width.Value);
            foreach (var item in grid.Columns.Skip(1)) item.IsVisible = false; Pump();
            Assert.Equal(24, grid.Columns[0].ActualWidth); Assert.False(Bar(grid).IsVisible);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void OverflowSavesLogicalRelativeWeightsAndRestoresThemInAWiderWindow()
    {
        var store = new MemoryUiPreferencesStore(); var (window, _, grid) = Open(false, store: store);
        Assert.True(Bar(grid).IsVisible); Assert.All(grid.Columns.Skip(1), column => Assert.False(column.Width.IsStar));
        window.FindControl<TextBox>("RowHeightInput")!.Text = "24"; Pump(); window.Close(); Pump();
        Assert.All(store.Value!.Layout.Columns, column => Assert.True(column.Star));
        var (reopened, _, restored) = Open(false, store: store);
        try
        {
            reopened.Width = 2800; Pump(); AssertFilled(restored);
            Assert.Equal(24, restored.RowHeight);
        }
        finally { reopened.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void AutomaticViewportAdaptationNeverSavesPreferencesByItself()
    {
        var store = new CountingStore(); var model = new MainWindowViewModel(new LinkedColumnFilterTests.Reader());
        var window = new MainWindow(store) { DataContext = model, Width = 1280, Height = 768 };
        try
        {
            window.Show(); Pump(); Assert.Equal(0, store.Writes);
            window.Width = 2800; Pump(); Assert.Equal(0, store.Writes);
            window.Width = 1280; Pump(); Assert.Equal(0, store.Writes);
        }
        finally { window.Close(); Pump(); }
        Assert.Equal(0, store.Writes);
    }

    [AvaloniaFact]
    public void CurrentManualWidthPersistsWithoutBeingMigratedAgainOnRestart()
    {
        var store = new MemoryUiPreferencesStore(); var (window, _, grid) = Open(false, store: store);
        var customer = grid.Columns.Single(column => column.Header is "Заказчик");
        customer.Width = new DataGridLength(650); Pump(); Assert.Equal(650, customer.ActualWidth);
        window.Close(); Pump(); Assert.Equal(ScreenLayout.CurrentColumnSizingVersion, store.Value!.Layout.ColumnSizingVersion);
        Assert.False(store.Value.Layout.Columns.Single(column => column.Id == "customer").Star);
        var (reopened, _, restored) = Open(false, store: store);
        try { Assert.Equal(650, restored.Columns.Single(column => column.Header is "Заказчик").ActualWidth); }
        finally { reopened.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void LegacyMixedPixelsAndSingleStarKeepComparableColumnProportions()
    {
        var store = new MemoryUiPreferencesStore(); store.Save(new() { Layout = new()
        {
            Columns = [new("manager", 3, true, false, 200), new("customer", 4, true, true, 1)]
        } });
        var (window, _, grid) = Open(false, store: store);
        try
        {
            foreach (var column in grid.Columns.Skip(1)) column.IsVisible = column.Header is "Менеджер" or "Заказчик";
            Pump(); AssertFilled(grid);
            var manager = grid.Columns.Single(column => column.Header is "Менеджер");
            var customer = grid.Columns.Single(column => column.Header is "Заказчик");
            Assert.InRange(customer.ActualWidth / manager.ActualWidth, .89, .91);
        }
        finally { window.Close(); Pump(); }
    }

    private sealed class CountingStore : IUiPreferencesStore
    {
        public int Writes { get; private set; }
        public PreferencesLoadResult Load() => new(new UiPreferences());
        public void Save(UiPreferences preferences) => Writes++;
    }

    private static void AssertOpaqueFrame(MainWindow window)
    {
        Assert.Equal(WindowTransparencyLevel.None, window.ActualTransparencyLevel);
        using var frame = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        frame.Render(window);
        Assert.True(frame.Format == PixelFormat.Bgra8888 || frame.Format == PixelFormat.Rgba8888);
        var pixels = new byte[frame.PixelSize.Width * frame.PixelSize.Height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(frame.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, frame.PixelSize.Width * 4); }
        finally { pin.Free(); }
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != 255) Assert.Fail($"Прозрачный пиксель {index / 4} после изменения размера {window.ClientSize}.");
    }

    private static (MainWindow, MainWindowViewModel, DataGrid) Open(bool dark, double scale = 1, MemoryUiPreferencesStore? store = null)
    {
        store ??= new MemoryUiPreferencesStore(); if (store.Value is null) store.Save(new() { DarkTheme = dark });
        var model = new MainWindowViewModel(new LinkedColumnFilterTests.Reader());
        var window = new MainWindow(store) { DataContext = model, Width = 1280, Height = 768 };
        window.SetRenderScaling(scale); window.Show(); Pump(); return (window, model, window.FindControl<DataGrid>("OrdersGrid")!);
    }
    private static ScrollBar Bar(DataGrid grid) => grid.GetVisualDescendants().OfType<ScrollBar>()
        .Single(bar => bar.Name == "PART_HorizontalScrollbar" && bar.Orientation == Orientation.Horizontal);
    private static void AssertFilled(DataGrid grid)
    {
        Assert.False(Bar(grid).IsVisible, $"Ширина {grid.Bounds.Width}; прокрутка {Bar(grid).Maximum}; " +
            string.Join("; ", grid.Columns.Where(column => column.IsVisible).Select(column => $"{column.Header}: {column.ActualWidth:0.##} ({column.Width})")));
        var vertical = grid.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Name == "PART_VerticalScrollbar");
        var available = grid.Bounds.Width - (vertical.IsVisible ? vertical.Bounds.Width : 0);
        var used = grid.Columns.Where(column => column.IsVisible).Sum(column => column.ActualWidth);
        Assert.InRange(Math.Abs(available - used), 0, 2);
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        ClickAt(root, point);
    }
    private static void ClickAt(TopLevel root, Point point)
    { root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump(); }
}
