using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class TableSortResetTests
{
    private static readonly DateOnly Day = new(2026, 10, 18);
    private static readonly int[] DefaultIds = [4, 5, 1, 2, 3];
    private static OrderSummary[] Rows() =>
    [
        new(1, Day, "1018-900", "Анна", "Клиент", "Лазер", "Ст3", 3, 20, OrderColors.Work),
        new(2, Day, "1018-859", "Борис", "Клиент", "Лазер", "Ст3", 3, 100, OrderColors.Work),
        new(3, Day.AddDays(-1), "1017-1100", "Анна", "Клиент", "Лазер", "Ст3", 3, 3, OrderColors.Work),
        new(4, Day, "1018-1121", "Борис", "Клиент", "Лазер", "Ст3", 3, 5, OrderColors.Work),
        new(5, Day, "1018-0900", "Анна", "Клиент", "Лазер", "Ст3", 3, 20, OrderColors.Work)
    ];
    private sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Items = Rows();
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items);
    }
    private static DataGrid Grid(MainWindow window) => window.FindControl<DataGrid>("OrdersGrid")!;
    private static int[] Ids(MainWindow window) => Grid(window).CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
    private static DataGridColumnHeader Header(MainWindow window, string title)
    {
        var grid = Grid(window);
        grid.ScrollIntoView(grid.ItemsSource.Cast<OrderRowViewModel>().First(), grid.Columns.Single(column => Equals(column.Header, title))); Pump();
        return grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(header => Equals(header.Content, title));
    }

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    public void ThreeClicksReturnToDefaultAndSortChipPreservesDateFiltersSelectionAndLetter(bool dark, double scale)
    {
        var model = new MainWindowViewModel(new Reader()) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); Pump(); Assert.Equal(DefaultIds, Ids(window)); Assert.False(model.HasCustomSort);
            var price = Header(window, "Стоимость");
            var headerPosition = price.TranslatePoint(default, window)!.Value;
            var tableHeight = window.FindControl<Border>("TablePanel")!.Bounds.Height;
            var footerHeight = window.FindControl<Grid>("TableStatusBar")!.Bounds.Height;
            Assert.Equal(24, footerHeight);
            Assert.Null(window.FindControl<Control>("DiagnosticsButton"));
            void AssertStableTable()
            {
                Assert.Equal(headerPosition, price.TranslatePoint(default, window)!.Value);
                Assert.Equal(tableHeight, window.FindControl<Border>("TablePanel")!.Bounds.Height);
                Assert.Equal(footerHeight, window.FindControl<Grid>("TableStatusBar")!.Bounds.Height);
            }
            Click(window, price); Assert.Equal([3, 4, 1, 5, 2], Ids(window));
            AssertStableTable();
            Assert.Equal("Порядок: Стоимость ↑", model.SortingSummary);
            Click(window, price); Assert.Equal([2, 1, 5, 4, 3], Ids(window));
            AssertStableTable();
            Assert.Equal("Порядок: Стоимость ↓", model.SortingSummary);
            Click(window, price); Assert.Equal(DefaultIds, Ids(window)); AssertDefault(window, model);
            AssertStableTable();

            model.SearchScopeIndex = (int)OrderSearchField.Material; model.SearchText = "Ст3";
            model.FilterDate = Day.ToDateTime(TimeOnly.MinValue); model.StatusFilter = OrderStatusFilter.Work; Pump();
            var grid = Grid(window);
            foreach (var row in model.Orders.Where(row => row.Id is 2 or 4)) grid.SelectedItems.Add(row);
            model.ResultText = "Готовое письмо";
            Click(window, price);
            Assert.Equal([4, 1, 5, 2], Ids(window));
            var chip = window.FindControl<Button>("ActiveSortButton")!;
            Assert.True(chip.IsEffectivelyVisible);
            Assert.True(window.FindControl<AppliedFilterStrip>("ActiveColumnFilters")!.IsEffectivelyVisible);
            var position = chip.TranslatePoint(default, window)!.Value;
            Assert.InRange(position.X, 0, window.ClientSize.Width - chip.Bounds.Width);
            Save(window, $"sort-indicator-{dark}-{scale}");
            chip.Focus(); Press(window, Key.Space, PhysicalKey.Space);
            Assert.Equal([2, 1, 5, 4], Ids(window)); AssertDefault(window, model);
            Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), model.FilterDate);
            Assert.Equal("Ст3", model.SearchText); Assert.Equal(OrderStatusFilter.Work, model.StatusFilter);
            Assert.Equal("Готовое письмо", model.ResultText);
            Assert.Equal([2, 4], grid.SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id).Order().ToArray());
            Assert.True(grid.IsKeyboardFocusWithin);
            Assert.False(chip.IsEffectivelyVisible); // Фильтр статуса остаётся видимым.
            Assert.True(window.FindControl<AppliedFilterStrip>("ActiveColumnFilters")!.IsEffectivelyVisible);
            model.UpdatedText = "Заявка сохранена · " + new string('Б', 255); Pump();
            var updated = window.FindControl<TextBlock>("UpdatedStatusText")!;
            var updatedPoint = updated.TranslatePoint(default, window)!.Value;
            Assert.True(updatedPoint.X + updated.Bounds.Width <= window.ClientSize.Width);
            Assert.Contains(updated.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.Equal(model.UpdatedText, ToolTip.GetTip(updated));
            AssertStableTable();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalResetClearsSearchFiltersAndSortButClearSearchKeepsSort(bool dark)
    {
        var model = new MainWindowViewModel(new Reader()) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump();
            model.SearchScopeIndex = (int)OrderSearchField.Material; model.SearchExactMatch = true;
            model.SearchText = "Ст3"; model.FilterDate = Day.ToDateTime(TimeOnly.MinValue);
            model.StatusFilter = OrderStatusFilter.Work; Pump();
            Click(window, Header(window, "Стоимость"));
            model.ResultText = "Письмо";
            Click(window, window.FindControl<JournalButton>("ResetJournalButton")!);
            AssertDefault(window, model); Assert.Equal(DefaultIds, Ids(window));
            Assert.Equal("", model.SearchText); Assert.Null(model.FilterDate);
            Assert.Equal(0, model.SearchScopeIndex); Assert.False(model.SearchExactMatch);
            Assert.False(model.HasAnyColumnFilter); Assert.Equal("Письмо", model.ResultText);
            Assert.Empty(window.FindControl<AppliedFilterStrip>("ActiveColumnFilters")!.Filters!);
            Assert.True(window.FindControl<TextBlock>("DefaultSortText")!.IsEffectivelyVisible);
            Click(window, Header(window, "Стоимость"));
            model.SearchText = "Ст3"; model.ClearSearchCommand.Execute(null); Pump();
            Assert.True(model.HasCustomSort); Assert.Equal([3, 4, 1, 5, 2], Ids(window));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlResetHiddenColumnsAndMultipleSortsAlwaysHaveAnExplicitWayBack(bool dark)
    {
        var model = new MainWindowViewModel(new Reader()) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump();
            Click(window, Header(window, "Стоимость"));
            var price = Grid(window).Columns.Single(column => Equals(column.Header, "Стоимость"));
            price.IsVisible = false; Pump();
            Assert.True(window.FindControl<Button>("ActiveSortButton")!.IsEffectivelyVisible);
            Assert.Equal("Порядок: Стоимость ↑", model.SortingSummary);
            Click(window, window.FindControl<Button>("ActiveSortButton")!);
            AssertDefault(window, model); Assert.Equal(DefaultIds, Ids(window));
            price.IsVisible = true; Pump();
            Click(window, Header(window, "Стоимость"));
            Click(window, Header(window, "Стоимость"), RawInputModifiers.Control);
            AssertDefault(window, model); Assert.Equal(DefaultIds, Ids(window));

            Click(window, Header(window, "Менеджер"));
            Click(window, Header(window, "Стоимость"), RawInputModifiers.Shift);
            Assert.Equal(2, Grid(window).CollectionView.SortDescriptions.Count);
            Assert.Equal("Порядок: Менеджер ↑ +1", model.SortingSummary);
            Assert.Equal("Менеджер ↑ → Стоимость ↑", model.SortingDescription);
            Assert.Equal([3, 1, 5, 4, 2], Ids(window));
            Click(window, window.FindControl<Button>("ActiveSortButton")!);
            AssertDefault(window, model); Assert.Equal(DefaultIds, Ids(window));
            Grid(window).CollectionView.SortDescriptions.Clear(); Pump();
            AssertDefault(window, model); Assert.Equal(DefaultIds, Ids(window));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CreationAndRefreshKeepUserSortUntilTheUserExplicitlyResetsIt()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump(); Click(window, Header(window, "Стоимость"));
            var newest = Rows()[0] with { Id = 6, SpecificationNumber = "1018-1130", StoredCuttingPrice = 1 };
            model.AcceptCreatedOrder(newest); Pump();
            Assert.Equal([6, 3, 4, 1, 5, 2], Ids(window)); Assert.True(model.HasCustomSort);
            reader.Items = Rows().Append(newest).Reverse().ToArray();
            await model.ReloadCommand.ExecuteAsync(null); Pump();
            Assert.True(model.HasCustomSort); Assert.Equal(6, Ids(window)[0]);
            model.ResetFiltersCommand.Execute(null); Pump();
            AssertDefault(window, model); Assert.Equal([6, 4, 5, 1, 2, 3], Ids(window));
            var date = Header(window, "Дата"); Click(window, date);
            Assert.True(model.HasCustomSort); Assert.Contains("Дата ↑", model.SortingSummary);
            Click(window, date); Assert.True(model.HasCustomSort); Assert.Contains("Дата ↓", model.SortingSummary);
            Click(window, date); AssertDefault(window, model);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SortCanBeResetEvenWhenFiltersHideEveryRow(bool dark)
    {
        var model = new MainWindowViewModel(new Reader()) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump(); Click(window, Header(window, "Стоимость"));
            model.FilterDate = Day.AddYears(-1).ToDateTime(TimeOnly.MinValue); Pump();
            Assert.Empty(Ids(window)); Assert.True(model.ShowEmptyState);
            var chip = window.FindControl<Button>("ActiveSortButton")!;
            Assert.True(chip.IsEffectivelyVisible); Assert.True(chip.IsEffectivelyEnabled);
            Click(window, chip); AssertDefault(window, model);
            Assert.Empty(Ids(window)); Assert.NotNull(model.FilterDate);
            // Общий сброс возвращает и заявки, и стандартный порядок.
            Click(window, window.FindControl<JournalButton>("ResetJournalButton")!);
            Assert.Equal(DefaultIds, Ids(window)); AssertDefault(window, model);
        }
        finally { window.Close(); }
    }

    private static void AssertDefault(MainWindow window, MainWindowViewModel model)
    {
        var sort = Assert.Single(Grid(window).CollectionView.SortDescriptions);
        Assert.Equal(nameof(OrderRowViewModel.OrderDateSortKey), sort.PropertyPath);
        Assert.Equal(model.FilterDate is null ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending, sort.Direction);
        Assert.False(model.HasCustomSort);
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(MainWindow window, Control control, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left, modifiers); window.MouseUp(point, MouseButton.Left, modifiers); Pump();
    }
    private static void Press(MainWindow window, Key key, PhysicalKey physical)
    { window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); Pump(); }
    private static void Save(MainWindow window, string name)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), ".local", "sort-reset"); Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
