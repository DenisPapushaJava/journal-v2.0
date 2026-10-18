using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class ContextMenuTests
{
    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 1.5)] [InlineData(true, 1.5)]
    public void RightClickPreservesTheBatchAndEscapeReturnsFocusWithoutAnAction(bool dark, double scale)
    {
        var (window, model, grid) = Open(dark, scale);
        try
        {
            Click(Box(grid, 1)); Click(Box(grid, 2));
            RightClick(Cell(grid, 1));
            var menu = grid.ContextMenu!;
            Assert.True(menu.IsOpen); Selected(grid, 1, 2);
            Assert.Equal("Выбрано: 2", ((MenuItem)menu.Items[0]!).Header);
            Assert.False(Item(menu, "OrderMenuEdit").IsEnabled);
            Assert.False(Item(menu, "OrderMenuRowComment").IsEnabled);
            Assert.True(Item(menu, "OrderMenuResult").IsEnabled);
            Assert.Equal("В корзину (2)…", Item(menu, "OrderMenuTrash").Header);
            Assert.All(menu.GetVisualDescendants().OfType<MenuItem>().Where(item => item.IsEffectivelyVisible), item => InBounds(item));
            Save(TopLevel.GetTopLevel(menu)!, $"context-batch-{dark}-{scale}");
            Press(TopLevel.GetTopLevel(menu)!, Key.Escape, PhysicalKey.Escape);
            Assert.False(menu.IsOpen); Assert.True(window.IsVisible); Selected(grid, 1, 2);
            Assert.True(grid.IsKeyboardFocusWithin); Assert.Empty(model.ResultText);

            RightClick(Cell(grid, 3)); Selected(grid, 3);
            Assert.True(Item(menu, "OrderMenuEdit").IsEnabled);
            Assert.True(Item(menu, "OrderMenuRowComment").IsEnabled);
            Click(Item(menu, "OrderMenuEdit"));
            Assert.False(menu.IsOpen);
            var edit = Assert.Single(window.OwnedWindows.OfType<EditOrderWindow>());
            Assert.Equal(3, ((EditOrderViewModel)edit.DataContext!).Id);
            edit.Close(); Pump();
            Assert.Equal(3, model.Orders.Count);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public void KeyboardAndBlankAreaMenusHaveTheRightScopeAndOldItemsCannotRun()
    {
        var (window, model, grid) = Open();
        try
        {
            Click(Box(grid, 1)); Click(Box(grid, 2)); grid.Focus();
            Press(window, Key.F10, PhysicalKey.F10, RawInputModifiers.Shift);
            var menu = grid.ContextMenu!;
            Assert.True(menu.IsOpen); Selected(grid, 1, 2);
            var old = Item(menu, "OrderMenuResult");
            model.SetStatusFilterCommand.Execute(OrderStatusFilter.Ready); Pump();
            Assert.False(menu.IsOpen);
            model.ResetFiltersCommand.Execute(null); Pump();
            Click(Box(grid, 3)); grid.Focus();
            Press(window, Key.Apps, PhysicalKey.ContextMenu);
            Assert.True(menu.IsOpen);
            old.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Assert.Empty(model.ResultText); Assert.True(menu.IsOpen);
            Click(Item(menu, "OrderMenuResult"));
            Assert.Contains("16x1500x6000", model.ResultText); Assert.False(menu.IsOpen);

            var blank = grid.TranslatePoint(new Point(grid.Bounds.Width / 2, grid.Bounds.Height - 35), window)!.Value;
            RightClick(window, blank);
            Assert.True(menu.IsOpen);
            Assert.Equal(new[] { "OrderMenuNew", "OrderMenuRefresh" }, menu.Items.OfType<MenuItem>().Select(item => item.Name));
            Selected(grid, 3);
            Click(Item(menu, "OrderMenuNew"));
            Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public async Task BatchStatusUsesExistingWriteAndTrashUsesExistingConfirmation()
    {
        var (window, model, grid) = Open();
        try
        {
            Click(Box(grid, 1)); Click(Box(grid, 2)); RightClick(Cell(grid, 2));
            var menu = grid.ContextMenu!;
            var parent = Item(menu, "OrderMenuColors"); Click(parent); Pump();
            var ready = parent.Items.OfType<MenuItem>().Single(item => item.Name == "OrderMenuReady");
            Assert.True(ready.IsEffectivelyVisible); Click(ready);
            if (model.SetOrderColorCommand.ExecutionTask is { } task) await task; Pump();
            Assert.Equal(OrderColors.Ready, model.Orders.Single(row => row.Id == 1).Color);
            Assert.Equal(OrderColors.Ready, model.Orders.Single(row => row.Id == 2).Color);
            Assert.Equal(OrderColors.None, model.Orders.Single(row => row.Id == 3).Color);
            RightClick(Cell(grid, 2)); Click(Item(menu, "OrderMenuTrash"));
            var confirm = Assert.Single(window.OwnedWindows.OfType<OrderConfirmationWindow>());
            var form = (OrderConfirmationViewModel)confirm.DataContext!;
            Assert.Contains("2", form.Details); Assert.False(form.IsConfirmed);
            Press(confirm, Key.Escape, PhysicalKey.Escape); Assert.Equal(3, model.Orders.Count);
        }
        finally { Close(window); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void HeaderMenuControlsSortingFiltersAndColumnVisibility(bool dark)
    {
        var (window, model, grid) = Open(dark);
        try
        {
            Click(Box(grid, 1)); Click(Box(grid, 2));
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(item => item.Content is "Толщ., мм");
            var column = grid.Columns.Single(item => item.Header is "Толщ., мм");
            RightClick(header); var menu = grid.ContextMenu!;
            Assert.False(model.HasCustomSort); Selected(grid, 1, 2);
            Click(Item(menu, "ColumnMenuDescending"));
            Assert.Equal(ListSortDirection.Descending, grid.CollectionView.SortDescriptions[0].Direction);
            RightClick(header); Click(Item(menu, "ColumnMenuAscending"));
            Assert.Equal(ListSortDirection.Ascending, grid.CollectionView.SortDescriptions[0].Direction);
            Assert.True(model.HasCustomSort);
            RightClick(header); Click(Item(menu, "ColumnMenuDefault")); Assert.False(model.HasCustomSort);
            RightClick(header); Click(Item(menu, "ColumnMenuFilter"));
            var filter = header.GetVisualDescendants().OfType<Jornal.Desktop.Controls.ColumnFilterHeader>().Single();
            var trigger = filter.GetVisualDescendants().OfType<Button>().Single();
            Assert.True(trigger.Flyout!.IsOpen); trigger.Flyout.Hide(); Pump();
            RightClick(header); Click(Item(menu, "ColumnMenuHide")); Assert.False(column.IsVisible);
            Selected(grid, 1, 2);
            var first = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(item => item.Content is Button);
            RightClick(first);
            Assert.Equal("ColumnMenuSettings", Assert.Single(menu.Items.OfType<MenuItem>()).Name);
        }
        finally { Close(window); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ResultMenuSeparatesSelectedTextFromTheFormattedEmailAndKeepsManualChanges(bool dark)
    {
        var (window, model, grid) = Open(dark);
        try
        {
            Click(Box(grid, 1));
            var text = window.FindControl<TextBox>("ResultTextBox")!;
            text.Text = "Ручной комментарий\nЛист и цена"; Pump();
            text.Focus(); text.SelectionStart = 0; text.SelectionEnd = 6;
            Press(window, Key.Apps, PhysicalKey.ContextMenu);
            var menu = text.ContextMenu!;
            Assert.True(menu.IsOpen); Assert.Null(text.ContextFlyout);
            Click(Item(menu, "ResultMenuCopy"));
            Assert.Equal("Ручной", await window.Clipboard!.TryGetTextAsync());
            text.Focus(); Press(window, Key.F10, PhysicalKey.F10, RawInputModifiers.Shift);
            Save(TopLevel.GetTopLevel(menu)!, $"context-result-{dark}");
            Click(Item(menu, "ResultMenuEmail"));
            using (var data = await window.Clipboard!.TryGetDataAsync())
            {
                Assert.NotNull(data);
                Assert.Equal(text.Text, await data.TryGetTextAsync());
                Assert.NotEmpty((await data.TryGetValueAsync(ResultClipboardPayload.HtmlFormat))!);
                Assert.NotEmpty((await data.TryGetValueAsync(ResultClipboardPayload.RtfFormat))!);
            }
            text.Focus(); Press(window, Key.Apps, PhysicalKey.ContextMenu);
            Click(Item(menu, "ResultMenuClear")); Assert.Empty(model.ResultText); Selected(grid, 1);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public async Task TrashMenuSelectsItsRowProtectsPendingAndNeverClearsTheWholeBin()
    {
        var source = new TrashSource("mdb", Path.Combine(Path.GetTempPath(), "context-menu-test.mdb"));
        var archive = new MemoryTrashStore();
        using (var lease = await archive.OpenAsync(source, TestContext.Current.CancellationToken))
            foreach (var (id, state) in new[] { (1, TrashState.Deleted), (42, TrashState.DeletePending) })
                await lease.SaveAsync(new(1, Guid.NewGuid(), source, OrderMaintenanceTests.Record() with { Id = id, Specification = $"1018-{id}" }, DateTimeOffset.UtcNow, state));
        var backend = new OrderMaintenanceTests.Backend();
        var model = new TrashViewModel(new(backend, source, archive), "Тестовая база", (_, _) => throw new InvalidOperationException(),
            _ => Task.CompletedTask, action => action(), _ => { });
        var window = new TrashWindow { DataContext = model, Width = 860, Height = 540 };
        try
        {
            window.Show(); await model.RefreshCommand.ExecutionTask!; Pump();
            var grid = window.FindControl<DataGrid>("TrashGrid")!;
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.DataContext is TrashItem item && item.Entry.Record.Id == 1);
            RightClick(row); var menu = grid.ContextMenu!;
            Assert.True(menu.IsOpen); Assert.Equal(1, model.Selected!.Entry.Record.Id);
            Assert.Equal(new[] { "TrashMenuRestore", "TrashMenuPurge" }, menu.Items.OfType<MenuItem>().Where(item => item.Name is not null).Select(item => item.Name));
            Press(TopLevel.GetTopLevel(menu)!, Key.Escape, PhysicalKey.Escape); Assert.True(window.IsVisible); Assert.False(menu.IsOpen);
            RightClick(row); Click(Item(menu, "TrashMenuRestore"));
            var restore = Assert.Single(window.OwnedWindows.OfType<OrderConfirmationWindow>());
            Assert.False(((OrderConfirmationViewModel)restore.DataContext!).IsDestructive);
            Press(restore, Key.Escape, PhysicalKey.Escape);
            RightClick(row); Click(Item(menu, "TrashMenuPurge"));
            var confirm = Assert.Single(window.OwnedWindows.OfType<OrderConfirmationWindow>());
            Press(confirm, Key.Escape, PhysicalKey.Escape); Assert.Equal(2, model.Items.Count);
            var pending = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.DataContext is TrashItem item && item.Entry.Record.Id == 42);
            RightClick(pending); Assert.True(menu.IsOpen);
            Assert.False(Item(menu, "TrashMenuRestore").IsEnabled); Assert.False(Item(menu, "TrashMenuPurge").IsEnabled);
            Assert.Equal(0, backend.Deletes); Assert.Equal(0, backend.Restores);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public void ReadOnlySourceAndScreenEditingKeepTheSameRestrictionsAsVisibleButtons()
    {
        var reader = new ReadOnlyReader();
        var model = new MainWindowViewModel(reader);
        model.InitializeSource(new(Path.Combine(Path.GetTempPath(), "context-read-only.mdb"), reader, null, null));
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 720 };
        try
        {
            window.Show(); Pump(); var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            Click(Box(grid, 1)); RightClick(Cell(grid, 1)); var menu = grid.ContextMenu!;
            Assert.True(Item(menu, "OrderMenuResult").IsEnabled);
            Assert.False(Item(menu, "OrderMenuEdit").IsEnabled);
            Assert.False(Item(menu, "OrderMenuDelivery").IsEnabled);
            Assert.False(Item(menu, "OrderMenuColors").IsEnabled);
            Assert.False(Item(menu, "OrderMenuTrash").IsEnabled);
            Click(Item(menu, "OrderMenuResult"));
            var text = window.FindControl<TextBox>("ResultTextBox")!;
            RightClick(text); var resultMenu = text.ContextMenu!;
            Assert.True(resultMenu.IsOpen); Assert.True(Item(resultMenu, "ResultMenuEmail").IsEnabled);
            Press(TopLevel.GetTopLevel(resultMenu)!, Key.Escape, PhysicalKey.Escape);
            RightClick(Cell(grid, 1)); Assert.True(menu.IsOpen);
            Click(window.FindControl<JournalButton>("PersonalizeButton")!); Assert.False(menu.IsOpen);
            RightClick(Cell(grid, 1)); Assert.False(menu.IsOpen);
            RightClick(text); Assert.True(resultMenu.IsOpen);
            Assert.False(Item(resultMenu, "ResultMenuCut").IsEnabled);
            Assert.False(Item(resultMenu, "ResultMenuPaste").IsEnabled);
            Assert.False(Item(resultMenu, "ResultMenuEmail").IsEnabled);
            Assert.False(Item(resultMenu, "ResultMenuClear").IsEnabled);
        }
        finally { Close(window); }
    }

    private static (MainWindow, MainWindowViewModel, DataGrid) Open(bool dark = false, double scale = 1)
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store, store) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 720 };
        window.SetRenderScaling(scale); window.Show(); Pump();
        return (window, model, window.FindControl<DataGrid>("OrdersGrid")!);
    }
    private static DataGridRow Row(DataGrid grid, int id) => grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext is OrderRowViewModel item && item.Id == id);
    private static CheckBox Box(DataGrid grid, int id) => Row(grid, id).GetVisualDescendants().OfType<CheckBox>().Single(box => box.Classes.Contains("row-selection"));
    private static DataGridCell Cell(DataGrid grid, int id) => Row(grid, id).GetVisualDescendants().OfType<DataGridCell>().First(cell => cell.Content is TextBlock);
    private static void Selected(DataGrid grid, params int[] ids) => Assert.Equal(ids.Order(), grid.SelectedItems.Cast<OrderRowViewModel>().Select(item => item.Id).Order());
    private static MenuItem Item(ContextMenu menu, string name) => menu.Items.OfType<MenuItem>().Single(item => item.Name == name);
    private static Point Center(Control control, TopLevel root) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!; var point = Center(control, root);
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void RightClick(Control control) { var root = TopLevel.GetTopLevel(control)!; RightClick(root, Center(control, root)); }
    private static void RightClick(TopLevel root, Point point)
    { root.MouseMove(point); root.MouseDown(point, MouseButton.Right); root.MouseUp(point, MouseButton.Right); Pump(); }
    private static void Press(TopLevel root, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    { root.KeyPress(key, modifiers, physical, null); if (root.IsVisible) root.KeyRelease(key, modifiers, physical, null); Pump(); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Close(Window window) { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); Pump(); }
    private static void InBounds(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!; var point = control.TranslatePoint(default, root)!.Value;
        Assert.InRange(point.X, 0, root.ClientSize.Width - control.Bounds.Width + 1);
        Assert.InRange(point.Y, 0, root.ClientSize.Height - control.Bounds.Height + 1);
    }
    private static void Save(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1));
        bitmap.Render(root); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
    private sealed class ReadOnlyReader : IOrderReader
    {
        private readonly DemoOrderStore _store = new();
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => _store.ReadAsync(cancellationToken);
    }
}
