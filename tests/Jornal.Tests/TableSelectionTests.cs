using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TableSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MouseSelectionHasOneEditingTargetAndClearKeepsResult(bool dark)
    {
        var (window, model, grid) = Open(dark);
        try
        {
            var edit = window.FindControl<JournalButton>("EditOrderButton")!;
            var clear = window.FindControl<JournalButton>("ClearSelectionButton")!;
            Assert.False(edit.IsEnabled); Assert.False(clear.IsEnabled);
            Click(window, Box(grid, 1)); Click(window, Box(grid, 2));
            AssertSelected(grid, 1, 2);
            Assert.False(edit.IsEnabled); Assert.True(clear.IsEnabled);
            Assert.Equal("Выберите одну заявку", ToolTip.GetTip(edit));
            Assert.Null(await model.CreateEditFormAsync(TestContext.Current.CancellationToken));
            Assert.True(model.CanAddResult); Assert.True(model.CanUpdateSelectedOrders); Assert.True(model.CanTrashOrder);
            Save(window, $"selection-batch-{dark}");

            Click(window, Cell(grid, 3));
            AssertSelected(grid, 3); Assert.True(edit.IsEnabled);
            Click(window, Cell(grid, 1), RawInputModifiers.Control);
            AssertSelected(grid, 1, 3); Assert.False(edit.IsEnabled);
            Click(window, Cell(grid, 3), RawInputModifiers.Control);
            AssertSelected(grid, 1); Assert.True(edit.IsEnabled);

            Click(window, Cell(grid, 3));
            Click(window, Cell(grid, 2), RawInputModifiers.Shift);
            AssertSelected(grid, 1, 2, 3); Assert.False(edit.IsEnabled);
            Click(window, Box(grid, 2)); Click(window, Box(grid, 3));
            AssertSelected(grid, 1); Assert.True(edit.IsEnabled);
            Assert.Equal(1, (await model.CreateEditFormAsync(TestContext.Current.CancellationToken))!.Id);

            model.AddOrdersToResult(model.Orders.Where(row => row.Id == 1));
            var result = model.ResultText;
            Assert.False(string.IsNullOrEmpty(result));
            Click(window, clear);
            AssertSelected(grid);
            Assert.Equal("Выбрано: 0", window.FindControl<TextBlock>("SelectionCountText")!.Text);
            Assert.False(edit.IsEnabled); Assert.False(clear.IsEnabled);
            Assert.False(model.CanAddResult); Assert.False(model.CanUpdateSelectedOrders); Assert.False(model.CanTrashOrder);
            Assert.Equal(result, model.ResultText);
            Assert.Null(await model.CreateEditFormAsync(TestContext.Current.CancellationToken));
        }
        finally { Close(window); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortingKeepsSelectionAndEachFilterChangeClearsIt(bool dark)
    {
        var (window, model, grid) = Open(dark);
        try
        {
            Click(window, Box(grid, 1)); Click(window, Box(grid, 3));
            var date = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(header => header.Content is "Дата");
            Click(window, date);
            Assert.Equal(2, grid.CollectionView.Cast<OrderRowViewModel>().First().Id);
            AssertSelected(grid, 1, 3);
            Click(window, date);
            Assert.Equal(3, grid.CollectionView.Cast<OrderRowViewModel>().First().Id);
            AssertSelected(grid, 1, 3);

            model.SearchText = "Менеджер 1"; Pump();
            Assert.Equal(2, model.Orders.Count); AssertSelected(grid);
            Click(window, Box(grid, 1)); Click(window, Box(grid, 3));
            model.SearchScopeIndex = 1; Pump();
            Assert.Equal(2, model.Orders.Count); AssertSelected(grid);
            Click(window, Box(grid, 1));
            model.SetStatusFilterCommand.Execute(OrderStatusFilter.Ready); Pump();
            AssertSelected(grid);
            model.ResetFiltersCommand.Execute(null); Pump();
            Click(window, Box(grid, 2));
            model.FilterDate = new DateTime(2026, 10, 14); Pump();
            AssertSelected(grid);
            Assert.False(model.CanClearSelection); Assert.False(model.CanEditOrder);
        }
        finally { Close(window); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoubleClickOpensOnlyTheClickedOrderAndCheckboxesDoNotOpenEditor(bool dark)
    {
        var (window, _, grid) = Open(dark);
        try
        {
            Click(window, Box(grid, 1)); Click(window, Box(grid, 1));
            Assert.Empty(window.OwnedWindows);
            Click(window, Box(grid, 1)); Click(window, Box(grid, 2));
            AssertSelected(grid, 1, 2);
            var cell = Cell(grid, 3);
            Click(window, cell); Click(window, cell);
            var editor = Assert.Single(window.OwnedWindows.OfType<EditOrderWindow>());
            Assert.Equal(3, Assert.IsType<EditOrderViewModel>(editor.DataContext).Id);
            AssertSelected(grid, 3);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public async Task SingleSelectedOrderIsTheEditingTargetEvenWhenCurrentItemIsDifferent()
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store, store);
        await model.ReloadCommand.ExecuteAsync(null);
        model.UpdateSelection([model.Orders[0]], model.Orders[1]);
        Assert.True(model.CanEditOrder);
        Assert.Equal(model.Orders[0].Id, (await model.CreateEditFormAsync(TestContext.Current.CancellationToken))!.Id);
        model.UpdateSelection([], model.Orders[0]);
        Assert.False(model.CanEditOrder);
        Assert.Null(await model.CreateEditFormAsync(TestContext.Current.CancellationToken));
    }

    private static (MainWindow Window, MainWindowViewModel Model, DataGrid Grid) Open(bool dark)
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store, store) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        window.Show(); Pump();
        return (window, model, window.FindControl<DataGrid>("OrdersGrid")!);
    }

    private static DataGridRow Row(DataGrid grid, int id) => grid.GetVisualDescendants().OfType<DataGridRow>()
        .Single(row => row.IsEffectivelyVisible && row.DataContext is OrderRowViewModel item && item.Id == id);
    private static CheckBox Box(DataGrid grid, int id) => Row(grid, id).GetVisualDescendants().OfType<CheckBox>()
        .Single(box => box.Classes.Contains("row-selection"));
    private static DataGridCell Cell(DataGrid grid, int id) => Row(grid, id).GetVisualDescendants().OfType<DataGridCell>()
        .First(cell => cell.Content is TextBlock);
    private static void AssertSelected(DataGrid grid, params int[] ids) =>
        Assert.Equal(ids.Order(), grid.SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id).Order());
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(MainWindow window, Control control, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Pump();
    }
    private static void Close(MainWindow window)
    {
        foreach (var child in window.OwnedWindows.ToArray()) child.Close();
        window.Close(); Pump();
    }
    private static void Save(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
