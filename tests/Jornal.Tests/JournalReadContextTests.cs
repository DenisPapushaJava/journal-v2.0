using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Infrastructure.Demo;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalReadContextTests
{
    private sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Rows = Enumerable.Range(1, 200).Select(id => JournalPeriodTests.Row(id, DateOnly.FromDateTime(DateTime.Today)) with
        { SpecificationNumber = id.ToString(), WeightKilograms = id.ToString() }).ToArray();
        public TaskCompletionSource<IReadOnlyList<OrderSummary>>? Pending;
        public bool Fail;
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Fail
            ? Task.FromException<IReadOnlyList<OrderSummary>>(new IOException("synthetic")) : Pending?.Task ?? Task.FromResult(Rows);
    }

    [AvaloniaTheory]
    [InlineData(false, false, 1)] [InlineData(true, false, 1.5)]
    [InlineData(false, true, 1.5)] [InlineData(true, true, 2)]
    public async Task RefreshPreservesLatestSelectionViewportFocusSortAndLetter(bool automatic, bool dark, double scale)
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 800 };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var weight = grid.Columns.Single(column => Equals(column.Header, "Вес, кг"));
            grid.SelectedItem = model.Orders.Single(row => row.Id == 180);
            grid.CurrentColumn = weight; grid.Focus(); Pump();
            reader.Pending = new();
            var pending = Refresh(model, automatic); Pump();
            Assert.True(model.IsRefreshingOrders); Assert.False(model.IsLoadingOrders);
            Assert.True(grid.IsEffectivelyEnabled); Assert.True(grid.IsKeyboardFocusWithin);
            Assert.False(model.CanUseJournal); Assert.False(model.CanUpdateSelectedOrders);
            // Пользователь продолжает работать, пока идёт сетевое чтение.
            grid.SelectedItems.Clear();
            grid.SelectedItem = model.Orders.Single(row => row.Id == 140);
            grid.SelectedItems.Add(model.Orders.Single(row => row.Id == 141));
            grid.ScrollIntoView(model.Orders.Single(row => row.Id == 150), null); Pump();
            Bar(grid, Orientation.Vertical).ScrollTo(Bar(grid, Orientation.Vertical).Value + 7); Pump();
            var before = Viewport(grid);
            var sort = grid.CollectionView.SortDescriptions.ToArray();
            model.ResultText = "Подготовленное письмо";
            // Новая строка в начале изменяет индексы всех выбранных/видимых записей.
            var added = reader.Rows[0] with { Id = 201, SpecificationNumber = "201" };
            reader.Rows = reader.Rows.Append(added).Select(row => row.Id == 140 ? row with { WeightKilograms = "777" } : row).ToArray();
            var response = reader.Pending; reader.Pending = null; response.SetResult(reader.Rows);
            await pending; Pump();
            Assert.Equal(new[] { 140, 141 }, Selected(grid)); Assert.Equal("Выбрано: 2", model.SelectionText);
            AssertViewport(before, grid); Assert.True(grid.IsKeyboardFocusWithin);
            Assert.Same(weight, grid.CurrentColumn); Assert.Equal(sort, grid.CollectionView.SortDescriptions.ToArray());
            Assert.Equal("Подготовленное письмо", model.ResultText); Assert.False(model.IsRefreshingOrders);
            // Неизменный ответ вообще не пересоздаёт представление.
            var resets = 0; model.Orders.CollectionChanged += (_, _) => resets++;
            await Refresh(model, automatic); Pump();
            Assert.Equal(0, resets); AssertViewport(before, grid); Assert.Equal(new[] { 140, 141 }, Selected(grid));
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task MissingRowsLeaveSelectionWithoutSelectingANeighborAndDeletedAnchorUsesNextRow(bool automatic, bool removeAll)
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!; grid.Focus();
            grid.SelectedItem = model.Orders.Single(row => row.Id == 140);
            grid.SelectedItems.Add(model.Orders.Single(row => row.Id == 141));
            grid.ScrollIntoView(model.Orders.Single(row => row.Id == 150), null); Pump();
            var before = Viewport(grid);
            var order = grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            var next = order[Array.IndexOf(order, before.Id) + 1];
            reader.Rows = reader.Rows.Where(row => row.Id != before.Id && row.Id != 140 && (!removeAll || row.Id != 141)).ToArray();
            await Refresh(model, automatic); Pump();
            Assert.Equal(removeAll ? Array.Empty<int>() : new[] { 141 }, Selected(grid));
            Assert.Contains($"Из выбора исключено: {(removeAll ? 2 : 1)}", model.UpdatedText);
            Assert.DoesNotContain("удален", model.UpdatedText, StringComparison.OrdinalIgnoreCase);
            AssertViewport((next, before.Top), grid); Assert.True(grid.IsKeyboardFocusWithin);
            grid.SelectedItem = model.Orders.Single(row => row.Id == next); Pump();
            Assert.DoesNotContain("Из выбора", model.UpdatedText);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedRefreshKeepsVisibleContextAndNeverStealsFocusFromSearch(bool automatic)
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.SelectedItem = model.Orders.Single(row => row.Id == 140);
            grid.ScrollIntoView(model.Orders.Single(row => row.Id == 150), null); Pump();
            var before = Viewport(grid); var rows = model.Orders.ToArray();
            var search = window.FindControl<Control>("SearchBox")!;
            var input = (TextBox)search; input.Focus(); Pump();
            reader.Fail = true; await Refresh(model, automatic); Pump();
            Assert.Equal(rows, model.Orders); AssertViewport(before, grid); Assert.Equal(new[] { 140 }, Selected(grid));
            Assert.True(input.IsKeyboardFocusWithin); Assert.True(grid.IsEffectivelyEnabled);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public async Task RowChangedOutOfFilterIsExcludedWhileFilterRemainsAndHorizontalOffsetSurvives()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            var filter = model.MaterialFilter;
            // Создаём настоящий фильтр по одному из двух материалов.
            reader.Rows = reader.Rows.Select(row => row.Id == 1 ? row with { MaterialName = "Другой" } : row).ToArray();
            await model.ReloadCommand.ExecuteAsync(null);
            filter.BeginEdit(); filter.Editor.Options.Single(option => option.Key == "Другой").IsChecked = false;
            Assert.True(filter.ApplyEdit());
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.Columns[4].Width = new DataGridLength(1000); Pump();
            grid.SelectedItem = model.Orders.Single(row => row.Id == 140);
            grid.ScrollIntoView(model.Orders.Single(row => row.Id == 150), grid.Columns.Last()); Pump();
            var horizontal = Bar(grid, Orientation.Horizontal); horizontal.ScrollTo(47); Pump();
            var before = Viewport(grid);
            reader.Rows = reader.Rows.Select(row => row.Id == 140 ? row with { MaterialName = "Другой" } : row).ToArray();
            await model.ReloadCommand.ExecuteAsync(null); Pump();
            Assert.Empty(Selected(grid)); Assert.True(filter.HasFilter); Assert.Contains("Из выбора исключено: 1", model.UpdatedText);
            AssertViewport(before, grid); Assert.InRange(horizontal.Value, 46.5, 47.5);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task SelectedRecordCanMoveInCustomSortWithoutMovingViewport(bool automatic)
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => Equals(control.Content, "Вес, кг"));
            var point = header.TranslatePoint(new Point(10, header.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
            Assert.True(model.HasCustomSort);
            grid.SelectedItem = model.Orders.Single(row => row.Id == 40);
            grid.Focus(); grid.ScrollIntoView(model.Orders.Single(row => row.Id == 70), null); Pump();
            var before = Viewport(grid); var sort = grid.CollectionView.SortDescriptions.ToArray();
            reader.Rows = reader.Rows.Select(row => row.Id == 40 ? row with { WeightKilograms = "9999" } : row).ToArray();
            await Refresh(model, automatic); Pump();
            Assert.Equal(new[] { 40 }, Selected(grid)); Assert.Equal(sort, grid.CollectionView.SortDescriptions.ToArray());
            AssertViewport(before, grid);
            Assert.Equal(40, grid.CollectionView.Cast<OrderRowViewModel>().Last().Id);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task BriefReadDoesNotBlinkCommandsOrFiltersAndSlowReadShowsWriteBlocking(bool automatic, bool dark)
    {
        var reader = new Reader(); var store = new DemoOrderStore();
        var model = new MainWindowViewModel(reader, store, store) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.SelectedItem = model.Orders[0]; grid.SelectedItems.Add(model.Orders[1]); Pump();
            var create = window.FindControl<Button>("NewOrderButton")!;
            var comment = window.FindControl<Button>("CopyRowCommentButton")!;
            var filters = grid.GetVisualDescendants().OfType<JournalButton>().Where(button => button.Classes.Contains("status-filter-trigger")).ToArray();
            Assert.NotEmpty(filters); Assert.All(filters, button => Assert.True(button.IsEffectivelyEnabled));
            Assert.True(create.IsEffectivelyEnabled); Assert.False(comment.IsEffectivelyEnabled);
            var createOpacity = create.Opacity; var commentOpacity = comment.Opacity;
            var foreground = create.Foreground?.ToString(); var background = create.Background?.ToString();
            var filterColors = filters.Select(button => button.Foreground?.ToString()).ToArray();
            reader.Pending = new(); var pending = Refresh(model, automatic); Pump();
            Assert.False(create.IsEffectivelyEnabled); Assert.Equal(createOpacity, create.Opacity);
            Assert.Equal(foreground, create.Foreground?.ToString()); Assert.Equal(background, create.Background?.ToString());
            Assert.Equal(filterColors, filters.Select(button => button.Foreground?.ToString()).ToArray());
            Assert.Equal(commentOpacity, comment.Opacity); // Ранее недоступные команды не подсвечиваются.
            Assert.All(filters, button => { Assert.True(button.IsEffectivelyEnabled); Assert.Equal(1, button.Opacity); });
            await Task.Delay(380, TestContext.Current.CancellationToken); Pump();
            Assert.True(create.Opacity < createOpacity); Assert.False(create.IsEffectivelyEnabled);
            var response = reader.Pending; reader.Pending = null; response.SetResult(reader.Rows); await pending; Pump();
            Assert.True(create.IsEffectivelyEnabled); Assert.Equal(createOpacity, create.Opacity);
            Assert.Equal(commentOpacity, comment.Opacity); Assert.All(filters, button => Assert.True(button.IsEffectivelyEnabled));
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task OpeningFilterDuringPendingReadPreservesItsDraftAndDiscardsLateResponse(bool status)
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); window.Activate(); Pump(); await window.LoadInitialOrdersAsync();
            reader.Pending = new(); var pending = model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            Func<bool> isOpen;
            if (status)
            {
                var header = grid.GetVisualDescendants().OfType<StatusFilterHeader>().Single();
                header.ShowFilter(); isOpen = () => header.IsFilterOpen;
            }
            else
            {
                var header = grid.GetVisualDescendants().OfType<ColumnFilterHeader>().First();
                header.ShowFilter(); isOpen = () => header.IsFilterOpen;
            }
            Pump(); Assert.True(isOpen()); Assert.False(model.IsRefreshingOrders);
            var response = reader.Pending; reader.Pending = null; response.SetResult([]);
            Assert.Equal(JournalRefreshOutcome.Deferred, await pending); Pump();
            Assert.True(isOpen()); Assert.Equal(200, model.Orders.Count);
        }
        finally { window.Close(); Pump(); }
    }

    private static async Task Refresh(MainWindowViewModel model, bool automatic)
    {
        if (automatic) await model.RefreshAutomaticallyAsync();
        else await model.ReloadCommand.ExecuteAsync(null);
    }
    private static int[] Selected(DataGrid grid) => grid.SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id).Order().ToArray();
    private static JournalScrollBar Bar(DataGrid grid, Orientation orientation) => grid.GetVisualDescendants().OfType<JournalScrollBar>().Single(bar => bar.Orientation == orientation);
    private static (int Id, double Top) Viewport(DataGrid grid)
    {
        grid.UpdateLayout();
        var presenter = grid.GetVisualDescendants().OfType<DataGridRowsPresenter>().Single();
        return grid.GetVisualDescendants().OfType<DataGridRow>().Where(row => row.IsVisible && row.DataContext is OrderRowViewModel)
            .Select(row => (Id: ((OrderRowViewModel)row.DataContext!).Id, Top: row.TranslatePoint(default, presenter)!.Value.Y, Height: row.Bounds.Height))
            .Where(row => row.Top + row.Height > 0 && row.Top < presenter.Bounds.Height).OrderBy(row => row.Top)
            .Select(row => (row.Id, row.Top)).First();
    }
    private static void AssertViewport((int Id, double Top) before, DataGrid grid)
    {
        var after = Viewport(grid); Assert.Equal(before.Id, after.Id); Assert.InRange(after.Top, before.Top - 0.5, before.Top + 0.5);
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
