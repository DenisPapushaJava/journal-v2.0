using System.Reflection;
using Avalonia.Controls;
using Avalonia.Collections;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class CreatedOrderRevealTests
{
    private static readonly DateOnly Day = DateOnly.FromDateTime(DateTime.Today);

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task HistoricalCreationWaitsForTheListBeforeSelectingAndRevealingTheSavedRow(bool dark, bool batch)
    {
        var reader = new Reader();
        var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark });
        var window = new MainWindow(preferences) { DataContext = model, Width = 1200, Height = 700 };
        try
        {
            window.InitializeStartupPeriod();
            await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            model.StatusFilter = OrderStatusFilter.Ready; Pump();
            Assert.Empty(model.Orders);
            var store = new DemoOrderStore();
            var form = ManualOrderTests.ValidForm(store);
            form.OrderDate.Value = Day.AddDays(-1).ToString("dd.MM.yyyy");
            if (batch)
            {
                form = new NewOrderViewModel(store) { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
                await form.ImportFilesAsync(["one.html", "two.html"], TestContext.Current.CancellationToken);
                form.Manager.Value = "Иванов"; form.Customer.Value = "Пример";
                foreach (var item in form.BatchItems) item.Form.OrderDate.Value = Day.AddDays(-1).ToString("dd.MM.yyyy");
            }
            var showing = Show(window, form); Pump();
            await form.SaveCommand.ExecuteAsync(null);
            await Idle();
            Assert.True(form.IsSaved);
            Assert.Single(reader.FullReads);
            Assert.False(showing.IsCompleted, showing.Exception?.ToString() ?? "Reveal completed before the read");
            Assert.Empty(Grid(window).SelectedItems);
            reader.FullReads[0].SetResult(reader.Rows.Concat(form.CreatedOrders).ToArray());
            await showing; Pump();
            Assert.Equal(form.CreatedOrder!.Id, Assert.IsType<OrderRowViewModel>(Grid(window).SelectedItem).Id);
            Assert.Equal(form.CreatedOrders.Select(row => row.Id).Order(),
                Grid(window).SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id).Order());
            Assert.Contains(Grid(window).GetVisualDescendants().OfType<DataGridRow>(),
                row => row.DataContext is OrderRowViewModel item && item.Id == form.CreatedOrder.Id);
            Assert.Equal(batch ? 5 : 4, (await store.ReadAsync()).Count);
            Assert.False(model.RequiresReload);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public async Task SameDayCreationKeepsManualSortingAndFindsTheRowAfterCollectionReset()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.InitializeStartupPeriod(); await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            Grid(window).Columns[2].IsVisible = false;
            Grid(window).CollectionView.SortDescriptions.Clear();
            Grid(window).CollectionView.SortDescriptions.Add(DataGridSortDescription.FromPath(
                nameof(OrderRowViewModel.StoredCuttingPrice), System.ComponentModel.ListSortDirection.Ascending)); Pump();
            var form = ManualOrderTests.ValidForm(new DemoOrderStore());
            form.OrderDate.Value = Day.ToString("dd.MM.yyyy");
            var showing = Show(window, form); Pump(); await form.SaveCommand.ExecuteAsync(null);
            await showing; Pump();
            Assert.True(form.IsSaved); Assert.Empty(reader.FullReads);
            Assert.Equal(form.CreatedOrder!.Id, Assert.IsType<OrderRowViewModel>(Grid(window).SelectedItem).Id);
            Assert.True(model.HasCustomSort); Assert.False(Grid(window).Columns[2].IsVisible);
            Assert.Equal(nameof(OrderRowViewModel.StoredCuttingPrice), Assert.Single(Grid(window).CollectionView.SortDescriptions).PropertyPath);
        }
        finally { Close(window); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingRowOrReadFailureNeverScrollsToAStaleOrNullItem(bool fail)
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.InitializeStartupPeriod(); await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
            form.OrderDate.Value = Day.AddDays(-1).ToString("dd.MM.yyyy");
            var showing = Show(window, form); Pump(); await form.SaveCommand.ExecuteAsync(null); await Idle();
            if (fail) reader.FullReads[0].SetException(new IOException("Simulated unavailable database"));
            else reader.FullReads[0].SetResult([]);
            await showing; Pump();
            Assert.True(window.IsVisible); Assert.True(form.IsSaved);
            Assert.Empty(Grid(window).SelectedItems);
            Assert.Equal(fail, model.HasError);
            Assert.Equal(4, (await store.ReadAsync()).Count);
        }
        finally { Close(window); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ClosingOrReplacingTheModelCancelsPendingRevealEvenWhenReaderIgnoresCancellation(bool replaceModel)
    {
        var reader = new Reader { IgnoreCancellation = true }; var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.InitializeStartupPeriod(); await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            var form = ManualOrderTests.ValidForm(new DemoOrderStore());
            form.OrderDate.Value = Day.AddDays(-1).ToString("dd.MM.yyyy");
            var showing = Show(window, form); Pump(); await form.SaveCommand.ExecuteAsync(null); await Idle();
            var replacement = new MainWindowViewModel(new DemoOrderStore());
            if (replaceModel) { window.DataContext = replacement; await replacement.ReloadCommand.ExecuteAsync(null); }
            else window.Close();
            await showing.WaitAsync(TimeSpan.FromSeconds(2));
            reader.FullReads[0].SetResult(reader.Rows.Concat(form.CreatedOrders).ToArray());
            await model.OrdersLoadTask; Pump();
            Assert.Empty(Grid(window).SelectedItems);
            Assert.False(model.RequiresReload); Assert.False(replacement.RequiresReload);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public async Task SupersedingReadDoesNotApplyTheOldPostSaveSelection()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.InitializeStartupPeriod(); await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            var form = ManualOrderTests.ValidForm(new DemoOrderStore());
            form.OrderDate.Value = Day.AddDays(-1).ToString("dd.MM.yyyy");
            var showing = Show(window, form); Pump(); await form.SaveCommand.ExecuteAsync(null); await Idle();
            var refreshing = model.ReloadCommand.ExecuteAsync(null);
            await showing; Assert.Equal(2, reader.FullReads.Count);
            reader.FullReads[1].SetResult(reader.Rows.Concat(form.CreatedOrders).ToArray());
            await refreshing; Pump();
            Assert.Empty(Grid(window).SelectedItems);
            Assert.False(model.RequiresReload);
        }
        finally { Close(window); }
    }

    [AvaloniaFact]
    public async Task PostSaveSelectionFailureIsContainedAndF5RecoversWithoutAnotherWrite()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.InitializeStartupPeriod(); await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
            form.OrderDate.Value = Day.AddDays(-1).ToString("dd.MM.yyyy");
            var showing = Show(window, form); Pump(); await form.SaveCommand.ExecuteAsync(null); await Idle();
            var failures = 0;
            void FailSelection(object? sender, SelectionChangedEventArgs args)
            {
                if (!Grid(window).SelectedItems.Cast<OrderRowViewModel>().Any(row => row.Id == form.CreatedOrder!.Id)) return;
                Grid(window).SelectionChanged -= FailSelection;
                failures++; throw new ArgumentOutOfRangeException("index");
            }
            Grid(window).SelectionChanged += FailSelection;
            var saved = reader.Rows.Concat(form.CreatedOrders).ToArray();
            reader.FullReads[0].SetResult(saved);
            await showing; Pump();
            Assert.Equal(1, failures); Assert.True(window.IsVisible); Assert.True(model.RequiresReload);
            Assert.Contains("Заявка сохранена", model.ErrorMessage); Assert.Contains("F5", model.ErrorMessage);
            var refresh = model.ReloadCommand.ExecuteAsync(null); reader.FullReads[1].SetResult(saved);
            await refresh; Pump();
            Assert.False(model.RequiresReload); Assert.False(model.HasError);
            Assert.Equal(4, (await store.ReadAsync()).Count);
        }
        finally { Close(window); }
    }

    private sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Rows { get; } = Enumerable.Range(100, 250)
            .Select(id => new OrderSummary(id, Day, $"1023-{id}", "Иванов", "Пример", "Лазер", "Ст3", 3, 100, OrderColors.Work)).ToArray();
        public List<TaskCompletionSource<IReadOnlyList<OrderSummary>>> FullReads { get; } = [];
        public bool IgnoreCancellation { get; init; }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<OrderSummary>>();
            FullReads.Add(completion);
            return IgnoreCancellation ? completion.Task : completion.Task.WaitAsync(cancellationToken);
        }
        public Task<IReadOnlyList<OrderSummary>> ReadDayAsync(DateOnly date, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderSummary>>(Rows.Where(row => row.OrderDate == date).ToArray());
    }

    private static Task Show(MainWindow window, NewOrderViewModel form) => (Task)typeof(MainWindow)
        .GetMethod("ShowNewOrderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [form])!;
    private static DataGrid Grid(MainWindow window) => window.FindControl<DataGrid>("OrdersGrid")!;
    private static async Task Idle() => await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Close(MainWindow window)
    {
        foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close();
        window.Close(); Pump();
    }
}
