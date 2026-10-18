using Jornal.Core.Orders;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class AutomaticJournalReadTests
{
    [Fact]
    public async Task UnchangedSnapshotDoesNotResetTableAndChangedLetterRowsAreFlaggedWithoutRewritingText()
    {
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        var resets = 0; model.Orders.CollectionChanged += (_, _) => resets++;
        model.UpdateSelection(model.Orders, model.Orders[0]); model.AddOrdersToResult([model.Orders[0]]);
        var letter = model.ResultText;
        await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, resets); Assert.Equal("Выбрано: 3", model.SelectionText);
        reader.Rows = reader.Rows.Select(row => row.Id == 1 ? row with { StoredCuttingPrice = 777m } : row).ToArray();
        await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, resets); Assert.Equal(letter, model.ResultText); Assert.True(model.HasResultError);
        Assert.Contains("изменились", model.ResultMessage);
    }

    [Fact]
    public async Task RemovedRowsLeaveSelectionAndInvalidResponseCannotOverwriteTheLastSnapshot()
    {
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null); model.UpdateSelection(model.Orders, model.Orders[0]);
        reader.Rows = reader.Rows.Where(row => row.Id != 2).ToArray();
        await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Выбрано: 2", model.SelectionText);
        var previous = model.Orders.ToArray(); reader.Rows = [reader.Rows[0], reader.Rows[0]];
        Assert.Equal(JournalRefreshOutcome.Failed, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(previous, model.Orders); Assert.Equal("Выбрано: 2", model.SelectionText);
    }

    [Fact]
    public async Task RefreshPreservesSelectionFiltersAndLetterAndInvalidatesHistory()
    {
        var reader = new JournalPeriodTests.Reader();
        reader.Rows = reader.Rows.Select(row => row.Id == 3 ? row with { MaterialName = "Другая марка" } : row).ToArray();
        var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null); // Полная история уже есть в памяти.
        model.TodayCommand.Execute(null);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        model.ResultText = "Подготовленное письмо";
        reader.Rows = reader.Rows.Select(row => row.Id == 1 ? row with { WeightKilograms = "44" } : row with { CustomerName = "Новый заказчик" }).ToArray();
        Assert.Equal(JournalRefreshOutcome.Refreshed, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Выбрано: 1", model.SelectionText); Assert.True(model.IsTodaySelected);
        Assert.Equal("44", Assert.Single(model.Orders).WeightText);
        Assert.Equal("Подготовленное письмо", model.ResultText); Assert.Equal(1, reader.FullCalls);
        model.SearchText = "Новый заказчик"; await model.OrdersLoadTask;
        Assert.Equal(2, reader.FullCalls); Assert.Equal(2, model.Orders.Count);
        var filter = model.MaterialFilter;
        filter.BeginEdit();
        filter.Editor.Options.Single(option => option.Key == "Другая марка").IsChecked = false;
        Assert.True(filter.ApplyEdit()); Assert.True(filter.HasFilter);
        // Уточняющий фильтр и глобальный запрос остаются при фоновом чтении.
        var before = model.SearchText;
        Assert.Equal(JournalRefreshOutcome.Refreshed, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(before, model.SearchText);
        Assert.True(filter.HasFilter); Assert.Single(model.Orders);
    }

    [Fact]
    public async Task FailureKeepsSnapshotSelectionLetterAndWriteConflictRequirement()
    {
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        var previous = model.Orders.ToArray(); model.UpdateSelection(previous.Take(2), previous[0]);
        model.ResultText = "Письмо"; model.ErrorMessage = "Предыдущее сообщение"; reader.Fail = true;
        Assert.Equal(JournalRefreshOutcome.Failed, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(previous, model.Orders); Assert.Equal("Выбрано: 2", model.SelectionText);
        Assert.Equal("Письмо", model.ResultText); Assert.Equal("Предыдущее сообщение", model.ErrorMessage);
        Assert.Contains("Автообновление недоступно", model.UpdatedText); Assert.False(model.IsLoadingOrders);
        model.RequiresReload = true; var calls = reader.FullCalls;
        Assert.Equal(JournalRefreshOutcome.Deferred, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.True(model.RequiresReload); Assert.Equal(calls, reader.FullCalls);
        reader.Fail = false; await model.ReloadCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.DoesNotContain("недоступно", model.UpdatedText);
    }

    [Fact]
    public async Task TwoBackgroundRequestsAreSerializedAndCancelledLateResponseCannotReplaceF5()
    {
        var reader = new DeferredReader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        reader.Deferred = true;
        var automatic = model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(JournalRefreshOutcome.Deferred, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Calls); Assert.False(model.CanCreateOrder);
        reader.Deferred = false; reader.Rows = [JournalPeriodTests.Row(9, null)];
        await model.ReloadCommand.ExecuteAsync(null);
        reader.Response.SetResult([JournalPeriodTests.Row(1, null)]);
        Assert.Equal(JournalRefreshOutcome.Deferred, await automatic);
        Assert.Equal(9, Assert.Single(model.Orders).Id); Assert.False(model.IsLoadingOrders);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("import")]
    [InlineData("customize")]
    [InlineData("search")]
    [InlineData("disabled")]
    public async Task BusyStatesNeverStartABackgroundRead(string state)
    {
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        switch (state)
        {
            case "write": model.IsUpdatingOrders = true; break;
            case "import": model.IsImportingSpecification = true; break;
            case "customize": model.IsCustomizing = true; break;
            case "search": model.IsSearchPending = true; break;
            default: model.AutomaticJournalRefresh = false; break;
        }
        Assert.Equal(JournalRefreshOutcome.Deferred, await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.FullCalls);
    }

    [Fact]
    public async Task BackgroundReadCannotPrepareOrChangeSchemaEvenWhenPreparationPreviouslyFailed()
    {
        var reader = new JournalPeriodTests.Reader(); var preparation = new UnavailablePreparation();
        var model = new MainWindowViewModel(new OrderDurationPreparingReader(reader, preparation));
        await model.ReloadCommand.ExecuteAsync(null); Assert.Equal(1, preparation.Calls);
        await model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, reader.FullCalls); Assert.Equal(1, preparation.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ChangingQueryDuringRefreshClearsSelectionAndDiscardsTheLateResponse(bool period)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var reader = new DeferredReader { Rows = [JournalPeriodTests.Row(1, today), JournalPeriodTests.Row(2, today.AddDays(-1))] };
        var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        reader.Deferred = true;
        var pending = model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken);
        reader.Deferred = false;
        if (period) model.TodayCommand.Execute(null);
        else model.SearchText = "spec-2";
        await model.OrdersLoadTask;
        Assert.Equal("Выбрано: 0", model.SelectionText);
        Assert.Equal(period ? 1 : 2, Assert.Single(model.Orders).Id);
        reader.Response.SetResult([JournalPeriodTests.Row(99, today)]);
        Assert.Equal(JournalRefreshOutcome.Deferred, await pending);
        Assert.Equal(period ? 1 : 2, Assert.Single(model.Orders).Id);
        Assert.Equal("Выбрано: 0", model.SelectionText);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureCannotBringBackANoticeDismissedByNewSelection(bool automatic)
    {
        var reader = new DeferredReader { Rows = [JournalPeriodTests.Row(1, null), JournalPeriodTests.Row(2, null)] };
        var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null); model.UpdateSelection(model.Orders, model.Orders[0]);
        reader.Rows = reader.Rows.Skip(1).ToArray(); await model.ReloadCommand.ExecuteAsync(null);
        Assert.Contains("Из выбора исключено: 1", model.UpdatedText);
        reader.Deferred = true;
        var pending = automatic ? (Task)model.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken) : model.ReloadCommand.ExecuteAsync(null);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        reader.Response.SetException(new IOException()); await pending;
        Assert.DoesNotContain("Из выбора исключено", model.UpdatedText);
        Assert.Equal("Выбрано: 1", model.SelectionText);
    }

    private sealed class UnavailablePreparation : IOrderDurationInitializer
    {
        public int Calls;
        public Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default)
        { Calls++; throw new OrderWriteException(OrderWriteFailure.NotSaved, "Не подготовлено"); }
    }

    private sealed class DeferredReader : IOrderReader
    {
        public bool Deferred; public int Calls;
        public IReadOnlyList<OrderSummary> Rows = [JournalPeriodTests.Row(1, null)];
        public readonly TaskCompletionSource<IReadOnlyList<OrderSummary>> Response = new();
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; return Deferred ? Response.Task : Task.FromResult(Rows); }
    }
}
