using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class BatchConcurrencyTests
{
    [Fact]
    public async Task StaleTargetFieldRejectsWholeBatchWhileUnrelatedChangesArePreserved()
    {
        var store = new DemoOrderStore();
        var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        await store.SetColorAsync([before[2]], OrderColors.Ready, TestContext.Current.CancellationToken);
        var current = await store.ReadAsync(TestContext.Current.CancellationToken);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.SetColorAsync(before, OrderColors.Shipped, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Equal(current, await store.ReadAsync(TestContext.Current.CancellationToken));
        var updated = await store.SetDeliveryNumberAsync(before, "Общий номер", TestContext.Current.CancellationToken);
        Assert.Equal(OrderColors.Ready, updated.Single(row => row.Id == 3).Color);
        Assert.All(updated, row => Assert.Equal("Общий номер", row.DeliveryNumber));
    }

    [Fact]
    public void DifferentSnapshotsForTheSameKeyCannotBeSilentlyDeduplicated()
    {
        var row = ResultOutputTests.Order();
        Assert.Throws<ArgumentException>(() => OrderBatchChange.ForColor([row, row with { Color = 123 }], 42));
        Assert.Throws<ArgumentException>(() => OrderBatchChange.ForDeliveryNumber([row, row with { DeliveryNumber = "другое" }], "новое"));
        Assert.Throws<ArgumentException>(() => OrderBatchChange.ForDeliveryNumber([row], "   "));
    }

    [AvaloniaTheory]
    [InlineData(OrderWriteFailure.Conflict)]
    [InlineData(OrderWriteFailure.OutcomeUnknown)]
    public async Task ConflictAndUnknownCommitKeepScreenAndRequireASuccessfulRefresh(OrderWriteFailure failure)
    {
        var store = new DemoOrderStore();
        var editor = new UncertainEditor(store, failure);
        var model = await BatchOrderTests.Model(store, editor);
        model.UpdateSelection(model.Orders, model.Orders[0]); model.AddOrdersToResult(model.Orders);
        var rows = model.Orders.ToArray(); var result = model.ResultText;
        var form = model.CreateDeliveryNumberForm()!; form.Number = "Новый номер";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.True(model.HasError);
        Assert.False(form.SaveCommand.CanExecute(null)); Assert.False(model.SetOrderColorCommand.CanExecute(OrderColors.Ready));
        Assert.False(form.IsSaved); Assert.Equal("Новый номер", form.Number); Assert.Equal(model.ErrorMessage, form.Error);
        Assert.Equal(rows, model.Orders); Assert.Equal(result, model.ResultText); Assert.Equal("Выбрано: 3", model.SelectionText);
        var message = form.Error;
        form.Number = "Правка ввода";
        Assert.Equal(message, form.Error);
        form.ClearNumberCommand.Execute(null);
        Assert.Equal(message, form.Error); Assert.False(form.SaveCommand.CanExecute(null));
        Assert.True(model.ReloadCommand.CanExecute(null));
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.False(model.HasError); Assert.Equal(result, model.ResultText);
        if (failure == OrderWriteFailure.OutcomeUnknown) Assert.All(model.Orders, row => Assert.Equal("Новый номер", row.DeliveryNumber));
        else Assert.Equal("БЛЦБ-00010001", model.Orders[0].DeliveryNumber);
    }

    [AvaloniaFact]
    public async Task FailureToRestoreSelectionAfterCommitIsReportedAsAScreenProblem()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        model.SelectionRestoreRequested += (_, _) => throw new InvalidOperationException("UI failure after commit");
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Ready);
        Assert.True(model.RequiresReload); Assert.Contains("Изменения сохранены", model.ErrorMessage);
        Assert.All(await store.ReadAsync(TestContext.Current.CancellationToken), row => Assert.Equal(OrderColors.Ready, row.Color));
    }

    [AvaloniaFact]
    public async Task EmptyDeliveryDoesNotFallThroughToSearchInOtherFields()
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store);
        model.SearchScopeIndex = 2; model.SearchText = "Менеджер";
        Assert.Empty(model.Orders);
        model.SearchScopeIndex = 0;
        Assert.Equal(3, model.Orders.Count);
    }

    private sealed class UncertainEditor(DemoOrderStore store, OrderWriteFailure failure) : IOrderBatchEditor
    {
        public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color, CancellationToken cancellationToken = default) =>
            Task.FromException<IReadOnlyList<OrderSummary>>(new OrderWriteException(failure, "Обновите список (F5)."));
        public async Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number, CancellationToken cancellationToken = default)
        {
            if (failure == OrderWriteFailure.OutcomeUnknown) await store.SetDeliveryNumberAsync(expected, number, cancellationToken);
            throw new OrderWriteException(failure, "Результат записи не подтверждён. Обновите список (F5).");
        }
    }
}
