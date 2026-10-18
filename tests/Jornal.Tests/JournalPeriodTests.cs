using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalPeriodTests
{
    [Fact]
    public async Task SnapshotFallbackAndPreparationDecoratorPreserveTheDayContract()
    {
        IOrderReader fallback = new GlobalJournalSearchTests.Reader
        { Items = [Row(1, Today), Row(2, Today.AddDays(-1)), Row(3, null)] };
        Assert.Equal(1, Assert.Single(await fallback.ReadDayAsync(Today, TestContext.Current.CancellationToken)).Id);
        var source = new Reader(); var preparation = new Preparation();
        var prepared = new OrderDurationPreparingReader(source, preparation);
        Assert.Equal(1, Assert.Single(await prepared.ReadDayAsync(Today, TestContext.Current.CancellationToken)).Id);
        Assert.Equal(0, source.FullCalls); Assert.Single(source.Days); Assert.Equal(1, preparation.Calls);
        await prepared.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, source.FullCalls); Assert.Equal(1, preparation.Calls);
    }
    private sealed class Preparation : IOrderDurationInitializer
    {
        public int Calls;
        public Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalBatchChecksDuplicatesForItsDateWithoutLoadingWholeHistory(bool fail)
    {
        var preview = await BatchImportTests.Preview();
        var fields = preview.Fields;
        var day = DateOnly.ParseExact(fields[nameof(NewOrderViewModel.OrderDate)].Value, "dd.MM.yyyy",
            System.Globalization.CultureInfo.InvariantCulture);
        var known = Row(99, day) with
        {
            SpecificationNumber = fields[nameof(NewOrderViewModel.SpecificationNumber)].Value,
            CuttingType = fields[nameof(NewOrderViewModel.CuttingType)].Value, CustomerName = "Клиент"
        };
        var reader = new Reader { Rows = [Row(1, Today), known] };
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(reader, store);
        model.InitializeStartupPeriod(); await model.ReloadCommand.ExecuteAsync(null);
        reader.Fail = fail;
        var form = model.CreateOrderForm()!;
        form.ImportReader = new BatchImportTests.Reader(preview);
        form.Customer.Value = "Клиент"; form.Manager.Value = "Менеджер";
        await form.ImportFilesAsync(["one.html", "two.html"], TestContext.Current.CancellationToken);
        form.BatchItems[1].Form.SpecificationNumber.Value = "другой";
        Assert.All(form.BatchItems, item => Assert.False(item.HasFailure));
        Assert.Contains(fail ? "Не удалось проверить" : "Похожая заявка", form.BatchItems[0].Warning);
        Assert.Equal(0, reader.FullCalls);
        if (!fail && day != Today) Assert.Equal(1, reader.Days.Count(value => value == day));
    }
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    internal static OrderSummary Row(int id, DateOnly? date) => GlobalJournalSearchTests.Rows()[0] with
    { Id = id, OrderDate = date, SpecificationNumber = $"spec-{id}" };

    internal sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Rows = [Row(1, Today), Row(2, Today.AddDays(-1)), Row(3, null)];
        public int FullCalls;
        public List<DateOnly> Days = [];
        public bool Fail;
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        { FullCalls++; return Fail ? Task.FromException<IReadOnlyList<OrderSummary>>(new IOException()) : Task.FromResult(Rows); }
        public Task<IReadOnlyList<OrderSummary>> ReadDayAsync(DateOnly date, CancellationToken cancellationToken = default)
        {
            Days.Add(date);
            return Fail ? Task.FromException<IReadOnlyList<OrderSummary>>(new IOException()) :
                Task.FromResult<IReadOnlyList<OrderSummary>>(Rows.Where(row => row.OrderDate == date).ToArray());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StartupReadsOnlyTheChosenPeriod(bool today)
    {
        var reader = new Reader();
        var model = new MainWindowViewModel(reader) { ShowTodayOnStartup = today };
        model.InitializeStartupPeriod();
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(today ? DateTime.Today : (DateTime?)null, model.FilterDate);
        Assert.Equal(today ? 1 : 3, model.Orders.Count);
        Assert.Equal(today ? 0 : 1, reader.FullCalls);
        Assert.Equal(today ? new[] { Today } : [], reader.Days);
        model.ShowTodayOnStartup = !today;
        Assert.Equal(today, model.IsTodaySelected); // Настройка действует на следующий запуск.
    }

    [Fact]
    public async Task CalendarReadsDaysAndGlobalSearchLoadsHistoryOnceIncludingNullDates()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        model.InitializeStartupPeriod(); await model.ReloadCommand.ExecuteAsync(null);
        model.TodayCommand.Execute(null); model.TodayCommand.Execute(null);
        Assert.Single(reader.Days); Assert.True(model.IsTodaySelected);
        model.FilterDate = DateTime.Today.AddDays(-1); await model.OrdersLoadTask;
        Assert.Equal(2, Assert.Single(model.Orders).Id); Assert.Equal(0, reader.FullCalls);
        model.ResultText = "Письмо";
        model.SearchText = "spec-3"; await model.OrdersLoadTask;
        Assert.Null(model.FilterDate); Assert.Equal(3, Assert.Single(model.Orders).Id);
        Assert.Equal(1, reader.FullCalls); Assert.Equal("Письмо", model.ResultText);
        model.SearchText = "spec"; await model.OrdersLoadTask;
        Assert.Equal(3, model.Orders.Count);
        model.TodayCommand.Execute(null); await model.OrdersLoadTask;
        Assert.Equal(1, Assert.Single(model.Orders).Id);
        model.AllDatesCommand.Execute(null); await model.OrdersLoadTask;
        Assert.Equal("spec", model.SearchText); Assert.Equal(3, model.Orders.Count);
        Assert.Equal(1, reader.FullCalls); Assert.Equal(2, reader.Days.Count);
    }

    [Fact]
    public async Task RefreshOfSelectedDayDiscardsHistoryAndNextSearchReadsFreshHistory()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        model.TodayCommand.Execute(null);
        reader.Rows = [Row(4, Today), Row(5, Today.AddDays(-2))];
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(4, Assert.Single(model.Orders).Id); Assert.Single(reader.Days);
        model.SearchText = "spec-5"; await model.OrdersLoadTask;
        Assert.Equal(5, Assert.Single(model.Orders).Id); Assert.Equal(2, reader.FullCalls);
        model.ClearSearchCommand.Execute(null);
        Assert.Equal([4, 5], model.Orders.Select(row => row.Id).Order().ToArray());
    }

    [Fact]
    public async Task FailedHistoryDoesNotLookEmptyOrEnableActionsOnPartialResultsAndF5Retries()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        model.InitializeStartupPeriod(); await model.ReloadCommand.ExecuteAsync(null);
        reader.Fail = true;
        model.ResultText = "Подготовленное письмо";
        model.SearchText = "spec-2"; await model.OrdersLoadTask;
        Assert.True(model.HasError); Assert.False(model.ShowEmptyState);
        Assert.Equal(1, Assert.Single(model.Orders).Id);
        Assert.False(model.CanUseJournal); Assert.False(model.CanInteractWithTable);
        Assert.True(model.CanCopyResult); Assert.True(model.CanCheckUpdates);
        model.AddOrdersToResult(model.Orders); Assert.Equal("Подготовленное письмо", model.ResultText);
        Assert.True(model.CanEnterSearch); Assert.True(model.ReloadCommand.CanExecute(null));
        reader.Fail = false; await model.ReloadCommand.ExecuteAsync(null);
        Assert.False(model.HasError); Assert.True(model.CanUseJournal);
        Assert.Equal(2, Assert.Single(model.Orders).Id);
    }

    [Fact]
    public async Task OwnCreationPatchesFullHistoryAndHistoricalCreationFromDayLoadsCompleteHistory()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        model.AcceptCreatedOrder(Row(4, Today.AddDays(-3)));
        model.TodayCommand.Execute(null); model.AllDatesCommand.Execute(null);
        Assert.Contains(model.Orders, row => row.Id == 4); Assert.Equal(1, reader.FullCalls);
        model.TodayCommand.Execute(null); await model.ReloadCommand.ExecuteAsync(null);
        reader.Rows = reader.Rows.Append(Row(5, Today.AddDays(-4))).ToArray();
        model.AcceptCreatedOrder(reader.Rows.Last()); await model.OrdersLoadTask;
        Assert.Null(model.FilterDate); Assert.Contains(model.Orders, row => row.Id == 5);
        Assert.Equal(2, reader.FullCalls);
    }

    [Fact]
    public async Task UnknownWriteOutcomeRequiresFullRefreshEvenFromOneDay()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        model.InitializeStartupPeriod(); await model.ReloadCommand.ExecuteAsync(null);
        model.RequiresReload = true;
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(1, reader.FullCalls); Assert.Single(reader.Days);
        Assert.False(model.RequiresReload); Assert.True(model.IsTodaySelected);
    }

    [Fact]
    public async Task ChangingDayCannotDismissAnUnknownWriteOutcome()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        model.InitializeStartupPeriod(); await model.ReloadCommand.ExecuteAsync(null);
        model.RequiresReload = true;
        model.FilterDate = DateTime.Today.AddDays(-1); await model.OrdersLoadTask;
        Assert.Equal(1, reader.FullCalls); Assert.Single(reader.Days);
        Assert.True(model.RequiresReload);
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(2, reader.FullCalls); Assert.False(model.RequiresReload);
    }

    private sealed class DeferredReader : IOrderReader
    {
        public List<(DateOnly? Date, TaskCompletionSource<IReadOnlyList<OrderSummary>> Completion, CancellationToken Token)> Requests = [];
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Add(null, cancellationToken);
        public Task<IReadOnlyList<OrderSummary>> ReadDayAsync(DateOnly date, CancellationToken cancellationToken = default) => Add(date, cancellationToken);
        private Task<IReadOnlyList<OrderSummary>> Add(DateOnly? date, CancellationToken token)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<OrderSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((date, completion, token)); return completion.Task;
        }
    }

    [Fact]
    public async Task LateCancelledDayCannotReplaceNewGlobalResults()
    {
        var reader = new DeferredReader(); var model = new MainWindowViewModel(reader);
        model.InitializeStartupPeriod();
        var initial = model.ReloadCommand.ExecuteAsync(null);
        Assert.True(model.CanEnterSearch); Assert.False(model.CanUseJournal); Assert.False(model.ShowEmptyState);
        model.SearchText = "spec-2";
        var global = model.OrdersLoadTask;
        Assert.True(reader.Requests[0].Token.IsCancellationRequested);
        reader.Requests[1].Completion.SetResult([Row(2, Today.AddDays(-1))]);
        await global;
        reader.Requests[0].Completion.SetResult([Row(1, Today)]);
        await initial;
        Assert.Equal(2, Assert.Single(model.Orders).Id);
        Assert.False(model.IsLoadingOrders); Assert.Null(model.FilterDate);
    }

    [Fact]
    public async Task CancellationDiscardsEvenAReaderThatIgnoresCancellation()
    {
        var reader = new DeferredReader(); var model = new MainWindowViewModel(reader);
        var initial = model.ReloadCommand.ExecuteAsync(null);
        model.CancelOrderLoading();
        reader.Requests[0].Completion.SetResult([Row(1, Today)]);
        await initial;
        Assert.Empty(model.Orders); Assert.False(model.IsLoadingOrders); Assert.False(model.HasError);
    }

    [Fact]
    public async Task EmptyDayIsExplicitAndDoesNotFallBackToHistory()
    {
        var reader = new Reader { Rows = [Row(1, Today.AddDays(-1))] };
        var model = new MainWindowViewModel(reader);
        model.InitializeStartupPeriod(); await model.ReloadCommand.ExecuteAsync(null);
        Assert.Empty(model.Orders); Assert.True(model.ShowEmptyState);
        Assert.Contains(DateTime.Today.ToString("dd.MM.yyyy"), model.EmptyOrdersText);
        Assert.Equal(0, reader.FullCalls);
    }
}
