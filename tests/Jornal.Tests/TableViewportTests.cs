using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class TableViewportTests
{
    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1.5)]
    public void DefaultColumnsFillViewportAndGrowShrinkAndHideWithinTheirMinimums(bool dark, double scale)
    {
        var (window, model, grid) = Open(dark, scale);
        try
        {
            Assert.False(grid.Columns[0].Width.IsStar); Assert.Equal(44, grid.Columns[0].ActualWidth);
            Assert.True(Bar(grid).IsVisible); Assert.All(grid.Columns.Where(column => column.IsVisible), column => Assert.True(column.ActualWidth >= column.MinWidth - 1));
            var visible = new[] { "Менеджер", "Заказчик", "Материал" };
            foreach (var column in grid.Columns.Skip(1)) column.IsVisible = visible.Contains(column.Header);
            Pump(); Assert.All(grid.Columns.Where(column => column.IsVisible).Skip(1), column => Assert.True(column.Width.IsStar)); AssertFilled(grid); var before = grid.Columns.Where(column => column.IsVisible).Select(column => column.ActualWidth).ToArray();
            grid.SelectedItems.Add(model.Orders[0]); model.ResultText = "Письмо";
            window.Width = 1800; Pump(); AssertFilled(grid);
            Assert.True(grid.Columns.Single(column => column.Header is "Заказчик").ActualWidth > before[2]);
            Assert.Single(grid.SelectedItems); Assert.Equal("Письмо", model.ResultText);
            grid.Columns.Single(column => column.Header is "Материал").IsVisible = false; Pump(); AssertFilled(grid);
            window.Width = 1280; Pump(); AssertFilled(grid); Assert.Equal(44, grid.Columns[0].ActualWidth);
            foreach (var column in grid.Columns) column.IsVisible = true; Pump();
            Assert.True(Bar(grid).IsVisible && Bar(grid).Maximum > 0);
            Assert.All(grid.Columns, column => Assert.True(column.ActualWidth >= column.MinWidth - 1));
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ExistingPixelWidthsStayIntactUntilFitActionAndRelativeSizesSurviveRestart(bool dark)
    {
        var store = new MemoryUiPreferencesStore(); store.Save(new() { DarkTheme = dark, Layout = new()
        {
            Columns = [new("manager", 3, true, false, 200), new("customer", 4, true, false, 260), new("material", 6, true, false, 160)]
        } });
        var (window, _, grid) = Open(dark, store: store);
        try
        {
            foreach (var name in new[] { "Менеджер", "Заказчик", "Материал" }) Assert.False(grid.Columns.Single(column => Equals(column.Header, name)).Width.IsStar);
            foreach (var column in grid.Columns.Skip(1)) column.IsVisible = column.Header is "Менеджер" or "Заказчик" or "Материал";
            Pump(); var columns = grid.Columns.Where(column => column.IsVisible).Skip(1).ToArray();
            Assert.Equal([200d, 260d, 160d], columns.Select(column => column.ActualWidth));
            Fit(window); Assert.All(columns, column => Assert.True(column.Width.IsStar)); AssertFilled(grid);
            var ratio = columns[1].ActualWidth / columns[0].ActualWidth; Assert.InRange(ratio, 1.29, 1.31);
            window.Width = 1600; Pump(); AssertFilled(grid); Assert.InRange(columns[1].ActualWidth / columns[0].ActualWidth, 1.29, 1.31);
            window.Close(); Pump();
            Assert.All(store.Value!.Layout.Columns.Where(column => column.Visible), column => Assert.True(column.Star));
            var (reopened, _, restored) = Open(dark, store: store);
            try
            {
                AssertFilled(restored); Assert.True(restored.Columns.Single(column => column.Header is "Менеджер").Width.IsStar);
                Assert.Equal(18, restored.Columns.Count);
            }
            finally { reopened.Close(); Pump(); }
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void FitDuringCustomizationCanBeCanceledAndEmptyVisibilityIsSafe()
    {
        var (window, _, grid) = Open(false);
        try
        {
            foreach (var item in grid.Columns.Skip(1)) item.IsVisible = item.Header is "Менеджер" or "Заказчик" or "Материал";
            Pump();
            var column = grid.Columns.Single(item => item.Header is "Менеджер"); column.Width = new DataGridLength(200); Pump();
            Click(window.FindControl<JournalButton>("PersonalizeButton")!); Fit(window); Assert.True(column.Width.IsStar);
            // Кнопка отмены режима возвращает прежние пиксельные размеры.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(column.Width.IsStar); Assert.Equal(200, column.Width.Value);
            foreach (var item in grid.Columns.Skip(1)) item.IsVisible = false; Pump(); Fit(window);
            Assert.Equal(44, grid.Columns[0].ActualWidth); Assert.False(Bar(grid).IsVisible);
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
            Assert.All(restored.Columns.Skip(1), column => Assert.True(column.Width.IsStar));
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

    private sealed class CountingStore : IUiPreferencesStore
    {
        public int Writes { get; private set; }
        public PreferencesLoadResult Load() => new(new UiPreferences());
        public void Save(UiPreferences preferences) => Writes++;
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
        Assert.False(Bar(grid).IsVisible);
        var vertical = grid.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.Name == "PART_VerticalScrollbar");
        var available = grid.Bounds.Width - (vertical.IsVisible ? vertical.Bounds.Width : 0);
        var used = grid.Columns.Where(column => column.IsVisible).Sum(column => column.ActualWidth);
        Assert.InRange(Math.Abs(available - used), 0, 2);
    }
    private static void Fit(MainWindow window)
    {
        var trigger = window.FindControl<JournalButton>("ColumnsButton")!;
        var flyout = Assert.IsType<Flyout>(trigger.Flyout); if (!flyout.IsOpen) Click(trigger);
        Assert.True(flyout.IsOpen); Click(window.FindControl<JournalButton>("FitTableToViewportButton")!); flyout.Hide(); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
}
