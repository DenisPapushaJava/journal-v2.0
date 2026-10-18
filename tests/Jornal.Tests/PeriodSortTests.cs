using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class PeriodSortTests
{
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.Today);
    private static OrderSummary Row(int id, DateOnly? date, string number) =>
        new(id, date, number, "Менеджер", "Заказчик", "Лазер", "Ст3", 3, id * 100, OrderColors.Work);
    private static Reader Source() => new()
    {
        Rows = [Row(1, Day, "1018-900"), Row(2, Day, "1018-859"), Row(3, Day.AddDays(-1), "1017-1800"),
            Row(4, Day, "1018-1121"), Row(5, null, "Неизвестная дата"), Row(6, Day, "1018-0900")]
    };

    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 1.5)] [InlineData(true, 1.5)]
    public async Task StandardOrderFollowsTheExplicitPeriodIncludingStartupSearchAndRefresh(bool dark, double scale)
    {
        var reader = Source(); var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        model.InitializeStartupPeriod();
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 720 };
        window.SetRenderScaling(scale);
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            var grid = Grid(window); var date = Header(grid, "Дата");
            AssertIds(grid, 2, 1, 6, 4); AssertStandard(window, model, ListSortDirection.Ascending, "Порядок: Номер ↑");
            Assert.Equal(0, reader.FullCalls); Assert.Equal(1, reader.DayCalls);
            var position = date.TranslatePoint(default, window); var height = grid.Bounds.Height;
            Save(window, $"period-day-{dark}-{scale}");
            model.AllDatesCommand.Execute(null); await model.OrdersLoadTask; Pump();
            AssertIds(grid, 4, 6, 1, 2, 3, 5); AssertStandard(window, model, ListSortDirection.Descending, "Порядок: Дата и номер ↓");
            Assert.Equal(1, reader.FullCalls); Assert.Equal(position, date.TranslatePoint(default, window)); Assert.Equal(height, grid.Bounds.Height);
            Save(window, $"period-all-{dark}-{scale}");
            model.FilterDate = Day.AddDays(-1).ToDateTime(TimeOnly.MinValue); Pump();
            AssertIds(grid, 3); AssertStandard(window, model, ListSortDirection.Ascending, "Порядок: Номер ↑");
            model.TodayCommand.Execute(null); Pump(); AssertIds(grid, 2, 1, 6, 4);

            // Глобальный поиск остаётся историей, даже если найден только один день.
            model.ResultText = "Ручные правки письма";
            model.SearchScopeIndex = (int)OrderSearchField.Specification; model.SearchText = "1018-"; Pump();
            Assert.Null(model.FilterDate); AssertIds(grid, 4, 6, 1, 2);
            AssertStandard(window, model, ListSortDirection.Descending, "Порядок: Дата и номер ↓");
            reader.Rows = reader.Rows.Reverse().ToArray(); await model.ReloadCommand.ExecuteAsync(null); Pump();
            AssertIds(grid, 4, 6, 1, 2); Assert.Equal("Ручные правки письма", model.ResultText);
            Assert.Equal(2, reader.FullCalls); Assert.False(model.HasCustomSort);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ExplicitDateSortRemainsManualEvenWhenItMatchesTheCurrentStandard()
    {
        var model = new MainWindowViewModel(Source()); var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump(); var grid = Grid(window); var date = Header(grid, "Дата");
            AssertIds(grid, 4, 6, 1, 2, 3, 5); Assert.False(model.HasCustomSort);
            Click(date); Assert.True(model.HasCustomSort); Assert.Contains("Дата ↑", model.SortingSummary);
            Click(date); Assert.True(model.HasCustomSort); Assert.Contains("Дата ↓", model.SortingSummary);
            model.TodayCommand.Execute(null); await model.OrdersLoadTask; Pump();
            AssertIds(grid, 4, 6, 1, 2); Assert.True(model.HasCustomSort);
            await model.ReloadCommand.ExecuteAsync(null); Pump(); AssertIds(grid, 4, 6, 1, 2);
            Click(date); AssertIds(grid, 2, 1, 6, 4); Assert.False(model.HasCustomSort);
            model.AllDatesCommand.Execute(null); await model.OrdersLoadTask; Pump(); AssertIds(grid, 4, 6, 1, 2, 3, 5);

            // Явное DESC из контекстного меню тоже остаётся ручным при смене периода.
            RightClick(date); var menu = grid.ContextMenu!;
            Click(menu.Items.OfType<MenuItem>().Single(item => item.Name == "ColumnMenuDescending"));
            Assert.True(model.HasCustomSort); model.TodayCommand.Execute(null); await model.OrdersLoadTask; Pump();
            AssertIds(grid, 4, 6, 1, 2); Assert.True(model.HasCustomSort);
            model.ResetSortingCommand.Execute(null); Pump(); AssertIds(grid, 2, 1, 6, 4); Assert.False(model.HasCustomSort);
            model.AllDatesCommand.Execute(null); await model.OrdersLoadTask; Pump();
            Click(Header(grid, "Стоимость")); model.TodayCommand.Execute(null); await model.OrdersLoadTask; Pump();
            AssertIds(grid, 1, 2, 4, 6); Assert.True(model.HasCustomSort);
            model.ResetFiltersCommand.Execute(null); await model.OrdersLoadTask; Pump();
            AssertIds(grid, 4, 6, 1, 2, 3, 5); Assert.False(model.HasCustomSort); Assert.Null(model.FilterDate);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task SameDayCreationKeepsTheDayAndPartialCacheWhileMixedOrUnknownDatesShowHistory(bool batch)
    {
        var reader = Source(); var model = new MainWindowViewModel(reader); model.InitializeStartupPeriod();
        var window = new MainWindow { DataContext = model };
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); Pump(); var grid = Grid(window);
            model.StatusFilter = OrderStatusFilter.Ready; Pump(); Assert.Empty(grid.CollectionView);
            model.ResultText = "Сохранённое письмо";
            var added = batch ? new[] { Row(7, Day, "1018-1200"), Row(8, Day, "1018-1145") } : [Row(7, Day, "1018-1200")];
            reader.Rows = reader.Rows.Concat(added).ToArray();
            model.AcceptCreatedOrders(added); await model.OrdersLoadTask; Pump();
            Assert.Equal(Day.ToDateTime(TimeOnly.MinValue), model.FilterDate);
            AssertIds(grid, batch ? [2, 1, 6, 4, 8, 7] : [2, 1, 6, 4, 7]);
            Assert.Equal(OrderStatusFilter.All, model.StatusFilter); Assert.Equal("Сохранённое письмо", model.ResultText);
            Assert.Equal(0, reader.FullCalls); Assert.Equal(1, reader.DayCalls); Assert.False(model.HasCustomSort);

            // Поздний импорт более ранней спецификации занимает своё место, а не конец дня.
            var earlier = Row(9, Day, "1018-800"); reader.Rows = reader.Rows.Append(earlier).ToArray();
            model.AcceptCreatedOrder(earlier); Pump(); Assert.Equal(9, grid.CollectionView.Cast<OrderRowViewModel>().First().Id);
            var historical = Row(10, batch ? Day.AddDays(-2) : null, "1016-900");
            var mixed = new[] { Row(11, Day, "1018-1300"), historical };
            reader.Rows = reader.Rows.Concat(mixed).ToArray();
            model.AcceptCreatedOrders(mixed); await model.OrdersLoadTask; Pump();
            Assert.Null(model.FilterDate); Assert.Equal(1, reader.FullCalls);
            Assert.Equal(11, grid.CollectionView.Cast<OrderRowViewModel>().First().Id);
            Assert.Contains(grid.CollectionView.Cast<OrderRowViewModel>(), row => row.Id == 10);
            Assert.False(model.HasCustomSort); Assert.Equal("Сохранённое письмо", model.ResultText);
        }
        finally { window.Close(); }
    }

    private sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Rows = [];
        public int FullCalls, DayCalls;
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        { FullCalls++; return Task.FromResult(Rows); }
        public Task<IReadOnlyList<OrderSummary>> ReadDayAsync(DateOnly day, CancellationToken cancellationToken = default)
        { DayCalls++; return Task.FromResult<IReadOnlyList<OrderSummary>>(Rows.Where(row => row.OrderDate == day).ToArray()); }
    }
    private static DataGrid Grid(MainWindow window) => window.FindControl<DataGrid>("OrdersGrid")!;
    private static DataGridColumnHeader Header(DataGrid grid, string text) => grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(item => Equals(item.Content, text));
    private static void AssertIds(DataGrid grid, params int[] expected) => Assert.Equal(expected, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
    private static void AssertStandard(MainWindow window, MainWindowViewModel model, ListSortDirection direction, string caption)
    {
        Assert.False(model.HasCustomSort); Assert.Equal(direction, Assert.Single(Grid(window).CollectionView.SortDescriptions).Direction);
        var label = window.FindControl<TextBlock>("DefaultSortText")!;
        Assert.True(label.IsEffectivelyVisible); Assert.Equal(caption, label.Text);
    }
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!; var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(p); root.MouseDown(p, MouseButton.Left); root.MouseUp(p, MouseButton.Left); Pump();
    }
    private static void RightClick(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!; var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(p); root.MouseDown(p, MouseButton.Right); root.MouseUp(p, MouseButton.Right); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
