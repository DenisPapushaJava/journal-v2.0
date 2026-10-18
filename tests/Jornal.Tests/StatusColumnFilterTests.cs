using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class StatusColumnFilterTests
{
    internal const int PersonalColor = -25089;
    internal const int OtherColor = -16776961;
    internal static readonly DateOnly Date = new(2026, 10, 18);
    internal static OrderSummary[] Rows() => new int?[] { OrderColors.Work, OrderColors.Ready, OrderColors.Shipped, PersonalColor, OtherColor, OrderColors.None, null }
        .Select((color, index) => new OrderSummary(index + 1, Date, $"1018-{index + 1}", "Технолог", "Заказчик", "Лазер", "Ст3", 2m, 100m, color)).ToArray();
    internal sealed class Reader : IOrderReader
    {
        public OrderSummary[] Items { get; init; } = Rows();
        public int Calls { get; private set; }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult<IReadOnlyList<OrderSummary>>(Items); }
    }
    private static async Task<MainWindowViewModel> Open(bool personal = false)
    {
        var model = new MainWindowViewModel(new Reader()) { UseTechnologistColor = personal };
        await model.ReloadCommand.ExecuteAsync(null); return model;
    }
    private static int[] Ids(MainWindowViewModel model) => model.Orders.Select(row => row.Id).Order().ToArray();
    private static void Select(StatusFilterEditorViewModel draft, params OrderStatusFilter[] choices)
    { foreach (var option in draft.Options) option.IsChecked = choices.Contains(option.Value); }

    [Fact]
    public async Task MultipleStatusesComposeWithSearchAndDateAndColumnClearKeepsOtherFilters()
    {
        var model = await Open(); model.SearchText = "Технолог"; model.FilterDate = Date.ToDateTime(TimeOnly.MinValue);
        model.StatusFilter = OrderStatusFilter.Work | OrderStatusFilter.Ready;
        Assert.Equal([1, 2], Ids(model)); Assert.Equal("Статус: 2 из 4", model.StatusFilterSummary);
        Assert.Equal("Статус: В работе, Готово", model.StatusFilterDescription);
        model.FilterDate = model.FilterDate.Value.AddDays(-1); Assert.Empty(model.Orders);
        model.ClearStatusFilterCommand.Execute(null); Assert.False(model.HasStatusFilter);
        Assert.Equal("Технолог", model.SearchText); Assert.Equal(Date.AddDays(-1), DateOnly.FromDateTime(model.FilterDate!.Value));
        model.ResetFiltersCommand.Execute(null); Assert.Equal(7, model.Orders.Count);
    }

    [Fact]
    public async Task DraftNeverChangesRowsSelectionOrResultBeforeApplyAndCannotApplyEmptySet()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null); model.ResultText = "Готовое письмо";
        model.UpdateSelection(model.Orders.Take(2), model.Orders[0]); model.BeginStatusFilterEdit();
        var draft = model.StatusFilterEditor; Assert.True(draft.AllChecked);
        draft.ToggleAllCommand.Execute(null); Assert.False(draft.AllChecked); Assert.False(draft.CanApply);
        Assert.False(model.ApplyStatusFilterEdit()); Assert.Equal(7, model.Orders.Count); Assert.Equal("Выбрано: 2", model.SelectionText);
        Select(draft, OrderStatusFilter.Work, OrderStatusFilter.Ready); Assert.Null(draft.AllChecked);
        Assert.Equal(7, model.Orders.Count); Assert.True(model.ApplyStatusFilterEdit());
        Assert.Equal([1, 2], Ids(model)); Assert.Equal("Выбрано: 0", model.SelectionText); Assert.Equal("Готовое письмо", model.ResultText);
        model.BeginStatusFilterEdit(); draft.ToggleAllCommand.Execute(null); Assert.True(draft.AllChecked);
        Assert.True(model.ApplyStatusFilterEdit()); Assert.False(model.HasStatusFilter); Assert.Equal(7, model.Orders.Count);
        Assert.Equal(1, reader.Calls); Assert.Equal(Rows(), reader.Items);
    }

    [Fact]
    public async Task PersonalAndOtherUnmarkedAreDisjointAndCoverNullAndLegacyColors()
    {
        var model = await Open(true); model.BeginStatusFilterEdit();
        Assert.Equal(["Мои заявки", "Другие без статуса", "В работе", "Готово", "Отгружено"], model.StatusFilterEditor.Options.Select(option => option.Label));
        var seen = new List<int>();
        foreach (var option in model.StatusFilterEditor.Options)
        {
            model.StatusFilter = option.Value; seen.AddRange(Ids(model));
            if (option.Value == OrderStatusFilter.Mine) Assert.Equal([4], Ids(model));
            if (option.Value == OrderStatusFilter.Unmarked) Assert.Equal([5, 6, 7], Ids(model));
        }
        Assert.Equal(Enumerable.Range(1, 7), seen.Order());
        model.StatusFilter = OrderStatusFilter.Mine | OrderStatusFilter.Ready;
        Assert.Equal([2, 4], Ids(model)); Assert.Equal("Статус: 2 из 5", model.StatusFilterSummary);
        model.UseTechnologistColor = false; Assert.Equal(OrderStatusFilter.Ready, model.StatusFilter);
        Assert.Equal([2], Ids(model)); model.BeginStatusFilterEdit();
        Assert.Equal("Без статуса", model.StatusFilterEditor.Options[0].Label);
        Assert.DoesNotContain(model.StatusFilterEditor.Options, option => option.Value == OrderStatusFilter.Mine);
    }

    [Fact]
    public async Task DisablingPersonalColorClearsMineOnlyAndEnablingPreservesUnmarkedRows()
    {
        var model = await Open(); model.StatusFilter = OrderStatusFilter.Unmarked; var before = Ids(model);
        model.UseTechnologistColor = true; Assert.Equal(before, Ids(model));
        Assert.Equal(OrderStatusFilter.Unmarked | OrderStatusFilter.Mine, model.StatusFilter);
        model.StatusFilter = OrderStatusFilter.Mine; model.UseTechnologistColor = false;
        Assert.False(model.HasStatusFilter); Assert.Equal(7, model.Orders.Count);
    }

    [Fact]
    public async Task CurrentPersonalColorReappliesOnlyRelevantFilterAndInvalidInputDoesNotChangeIt()
    {
        var model = await Open(true); model.StatusFilter = OrderStatusFilter.Mine;
        model.UpdateSelection(model.Orders, model.Orders[0]);
        model.SelectTechnologistColor(OtherColor); Assert.Equal([5], Ids(model)); Assert.Equal("Выбрано: 0", model.SelectionText);
        model.TechnologistColorCode = "не цвет"; Assert.Equal([5], Ids(model));
        model.StatusFilter = OrderStatusFilter.Work; var row = model.Orders[0]; model.UpdateSelection([row], row);
        model.SelectTechnologistColor(PersonalColor); Assert.Same(row, model.Orders[0]); Assert.Equal("Выбрано: 1", model.SelectionText);
    }

    [Fact]
    public async Task AssigningStatusRemovesPersonalMarkFromCurrentFilterWithoutLosingResult()
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store, store) { UseTechnologistColor = true };
        await model.ReloadCommand.ExecuteAsync(null); model.UpdateSelection([model.Orders[0]], model.Orders[0]);
        await model.SetOrderColorCommand.ExecuteAsync(PersonalColor); model.StatusFilter = OrderStatusFilter.Mine;
        var mine = Assert.Single(model.Orders); model.UpdateSelection([mine], mine); model.ResultText = "Письмо";
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Ready);
        Assert.Empty(model.Orders); Assert.Equal("Выбрано: 0", model.SelectionText); Assert.Equal("Письмо", model.ResultText);
        Assert.Equal(OrderColors.Ready, (await store.ReadAsync(TestContext.Current.CancellationToken)).Single(row => row.Id == mine.Id).Color);
    }

    [Fact]
    public async Task ReopeningDraftDiscardsUnappliedChoicesAndBusyJournalRejectsApply()
    {
        var model = await Open(true); model.StatusFilter = OrderStatusFilter.Work;
        model.BeginStatusFilterEdit(); Select(model.StatusFilterEditor, OrderStatusFilter.Mine);
        model.BeginStatusFilterEdit(); Assert.True(model.StatusFilterEditor.Options.Single(option => option.Value == OrderStatusFilter.Work).IsChecked);
        Select(model.StatusFilterEditor, OrderStatusFilter.Mine); model.IsUpdatingOrders = true;
        Assert.False(model.ApplyStatusFilterEdit()); Assert.Equal([1], Ids(model)); model.IsUpdatingOrders = false;
        Assert.True(model.ApplyStatusFilterEdit()); Assert.Equal([4], Ids(model));
    }
}
