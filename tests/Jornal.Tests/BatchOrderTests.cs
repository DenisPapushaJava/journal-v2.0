using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class BatchOrderTests
{
    [Theory]
    [InlineData(OrderColors.Work)]
    [InlineData(OrderColors.Ready)]
    [InlineData(OrderColors.Shipped)]
    [InlineData(OrderColors.None)]
    [InlineData(-1234567)]
    public async Task ColorBatchChangesOnlySelectedColors(int color)
    {
        var store = new DemoOrderStore();
        var original = await store.ReadAsync(TestContext.Current.CancellationToken);
        var changed = await store.SetColorAsync([1, 3, 1], color, TestContext.Current.CancellationToken);
        Assert.Equal(2, changed.Count);
        var reloaded = await store.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(original.Select(order => order.Id is 1 or 3 ? order with { Color = color } : order), reloaded);
    }

    [Fact]
    public async Task DeliveryPreservesTextAndAllOtherFieldsAndCanBeCleared()
    {
        var store = new DemoOrderStore();
        var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        const string number = "БЛЦБ-'Б'-00012345";
        await store.SetDeliveryNumberAsync([1, 2], number, TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(order => order.Id is 1 or 2 ? order with { DeliveryNumber = number } : order), await store.ReadAsync(TestContext.Current.CancellationToken));
        await store.SetDeliveryNumberAsync([1, 2], "", TestContext.Current.CancellationToken);
        Assert.All((await store.ReadAsync(TestContext.Current.CancellationToken)).Where(order => order.Id is 1 or 2), order => Assert.Equal("", order.DeliveryNumber));
    }

    [Fact]
    public async Task InvalidBatchAndCancellationNeverApplyAPartialChange()
    {
        var store = new DemoOrderStore();
        var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetColorAsync([1, 999], OrderColors.Ready, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetDeliveryNumberAsync([1, 999], "123", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetColorAsync([], OrderColors.None, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetDeliveryNumberAsync([1], new string('1', 256), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SetDeliveryNumberAsync([1], "123\n456", TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SetColorAsync([1, 2], 0, cancellation.Token));
        Assert.Equal(before, await store.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ViewModelPreservesRowObjectsSelectionAndResultAfterAStatusChange()
    {
        var store = new DemoOrderStore();
        var model = await Model(store);
        var rows = model.Orders.ToArray();
        model.UpdateSelection([rows[0], rows[2]], rows[0]);
        model.AddOrdersToResult([rows[0]]);
        var result = model.ResultText;
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Ready);
        Assert.Same(rows[0], model.Orders[0]);
        Assert.Same(rows[2], model.Orders[2]);
        Assert.Equal("Готово", rows[0].StatusText);
        Assert.Equal("ready", rows[2].StatusKey);
        Assert.Equal("Выбрано: 2", model.SelectionText);
        Assert.Equal(result, model.ResultText);
        Assert.Equal(1250, rows[0].StoredCuttingPrice);
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.All(model.Orders.Where(row => row.Id is 1 or 3), row => Assert.Equal(OrderColors.Ready, row.Color));
    }

    [Fact]
    public async Task StatusAndDeliveryChangesReapplyFiltersWithoutClearingTheResult()
    {
        var store = new DemoOrderStore();
        var model = await Model(store);
        model.SetStatusFilterCommand.Execute(OrderStatusFilter.Work);
        var row = Assert.Single(model.Orders);
        model.AddOrdersToResult([row]);
        model.UpdateSelection([row], row);
        var result = model.ResultText;
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Ready);
        Assert.Empty(model.Orders);
        Assert.True(model.HasNoOrders);
        Assert.Equal(OrderStatusFilter.Work, model.StatusFilter);
        Assert.Equal("Выбрано: 0", model.SelectionText);
        Assert.Equal(result, model.ResultText);
        model.ResetFiltersCommand.Execute(null);
        model.SearchScopeIndex = 4;
        model.SearchText = "БЛЦБ-00010001";
        row = Assert.Single(model.Orders);
        model.UpdateSelection([row], row);
        var form = model.CreateDeliveryNumberForm()!;
        form.Number = "БЛЦБ-NEW";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved);
        Assert.Empty(model.Orders);
        Assert.Equal("БЛЦБ-00010001", model.SearchText);
        Assert.Equal(result, model.ResultText);
        model.ResetFiltersCommand.Execute(null);
        Assert.Equal("БЛЦБ-NEW", model.Orders.Single(item => item.Id == 1).DeliveryNumber);
    }

    [Fact]
    public async Task FailedBatchKeepsRowsSelectionAndFormInputAndAllowsRetry()
    {
        var store = new DemoOrderStore();
        var editor = new FailingEditor(store);
        var model = await Model(store, editor);
        var rows = model.Orders.ToArray();
        model.UpdateSelection(rows, rows[0]);
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Shipped);
        Assert.True(model.HasError);
        Assert.Equal("Выбрано: 3", model.SelectionText);
        Assert.Equal(OrderColors.Work, rows[0].Color);
        var form = model.CreateDeliveryNumberForm()!;
        form.Number = "БЛЦБ-123";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.IsSaved);
        Assert.True(form.HasError);
        Assert.Equal("БЛЦБ-123", form.Number);
        editor.Fail = false;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved);
        Assert.False(model.HasError);
        Assert.All(model.Orders, row => Assert.Equal("БЛЦБ-123", row.DeliveryNumber));
    }

    [Fact]
    public async Task MixedDeliveryNumbersRequireAnExplicitValueOrClear()
    {
        var store = new DemoOrderStore();
        var model = await Model(store);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        var form = model.CreateDeliveryNumberForm()!;
        Assert.True(form.HasDifferentNumbers);
        Assert.Equal("", form.Number);
        Assert.False(form.SaveCommand.CanExecute(null));
        form.ClearNumberCommand.Execute(null);
        Assert.True(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.All(await store.ReadAsync(TestContext.Current.CancellationToken), order => Assert.Equal("", order.DeliveryNumber));
    }

    [Fact]
    public async Task BatchActionsRequireAnEditorSelectionAndNormalMode()
    {
        var store = new DemoOrderStore();
        var model = await Model(store);
        Assert.False(model.CanUpdateSelectedOrders);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        Assert.True(model.CanUpdateSelectedOrders);
        model.IsCustomizing = true;
        Assert.False(model.SetOrderColorCommand.CanExecute(OrderColors.Ready));
        Assert.Null(model.CreateDeliveryNumberForm());
        model.IsCustomizing = false;
        var readerOnly = new MainWindowViewModel(store);
        await readerOnly.ReloadCommand.ExecuteAsync(null);
        readerOnly.UpdateSelection(readerOnly.Orders, readerOnly.Orders[0]);
        Assert.False(readerOnly.CanUpdateSelectedOrders);
    }

    internal static async Task<MainWindowViewModel> Model(DemoOrderStore store, IOrderBatchEditor? editor = null)
    {
        var model = new MainWindowViewModel(store, store, editor ?? store);
        await model.ReloadCommand.ExecuteAsync(null);
        return model;
    }

    private sealed class FailingEditor(DemoOrderStore store) : IOrderBatchEditor
    {
        public bool Fail { get; set; } = true;
        public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<int> ids, int color, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("Проверка отказа записи") : store.SetColorAsync(ids, color, cancellationToken);
        public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<int> ids, string number, CancellationToken cancellationToken = default) =>
            Fail ? throw new IOException("Проверка отказа записи") : store.SetDeliveryNumberAsync(ids, number, cancellationToken);
    }
}
