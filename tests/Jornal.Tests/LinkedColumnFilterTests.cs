using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class LinkedColumnFilterTests
{
    internal static readonly DateOnly Date = new(2026, 10, 18);
    internal static OrderSummary[] Rows()
    {
        OrderSummary Row(int id, string manager, string customer, string machine, string material, decimal? thickness, int? color, bool bending = false)
        {
            var row = new OrderSummary(id, Date, $"1018-{id}", manager, customer, machine, material, thickness, 100, color);
            return row with { Annotations = OrderAnnotations.For(row, bending, "") };
        }
        return [Row(1, "Анна", "Альфа", "Лазер", "Ст3", 2, OrderColors.Work, true),
            Row(2, "Борис", "Альфа", "Лазер", "Ст3", 10, OrderColors.Ready),
            Row(3, "Анна", "Бета", "Плазма", "09Г2С", 20, OrderColors.Shipped),
            Row(4, "Борис", "Бета", "ГАР", "5083H111", 3, -25089, true),
            Row(5, "", "", "", "", null, null) with { AnnotationsUnavailable = true },
            Row(6, "Анна", "Альфа", "Лазер", "Ст3", 0.8m, null) with { OrderDate = Date.AddDays(-1) }];
    }
    internal sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Items { get; set; } = Rows();
        public int Calls { get; private set; }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Items); }
    }
    private static async Task<MainWindowViewModel> Open(Reader? reader = null)
    {
        var model = new MainWindowViewModel(reader ?? new()); await model.ReloadCommand.ExecuteAsync(null); return model;
    }
    internal static void Select(OrderColumnFilterViewModel filter, params string[] keys)
    {
        filter.BeginEdit();
        // Явный выбор даже единственного доступного пункта фиксирует ограничение.
        foreach (var option in filter.Editor.Options) option.IsChecked = false;
        foreach (var option in filter.Editor.Options.Where(option => keys.Contains(option.Key))) option.IsChecked = true;
        Assert.True(filter.ApplyEdit());
    }
    private static int[] Ids(MainWindowViewModel model) => model.Orders.Select(row => row.Id).Order().ToArray();

    [Fact]
    public async Task ValuesWithinColumnUseOrAndColumnsComposeWithStatusSearchAndDate()
    {
        var model = await Open(); model.UseTechnologistColor = true;
        Select(model.ManagerFilter, "Анна", "Борис"); Assert.Equal([1, 2, 3, 4, 6], Ids(model));
        Select(model.CuttingTypeFilter, "Лазер"); Assert.Equal([1, 2, 6], Ids(model));
        Select(model.ThicknessFilter, "2", "10"); Assert.Equal([1, 2], Ids(model));
        model.StatusFilter = OrderStatusFilter.Work | OrderStatusFilter.Ready;
        model.SearchScopeIndex = 5; model.SearchText = "Ст3"; model.FilterDate = Date.ToDateTime(TimeOnly.MinValue);
        Select(model.BendingFilter, "yes"); Assert.Equal([1], Ids(model));
        model.BendingFilter.ClearCommand.Execute(null); Assert.Equal([1, 2], Ids(model));
        Assert.True(model.CuttingTypeFilter.HasFilter); Assert.True(model.ThicknessFilter.HasFilter);
        Assert.NotNull(model.FilterDate); Assert.Equal("Ст3", model.SearchText);
        model.ResetFiltersCommand.Execute(null); Assert.Equal(6, model.Orders.Count); Assert.False(model.HasAnyColumnFilter);
        Assert.False(model.HasStatusFilter); Assert.Null(model.FilterDate); Assert.Equal("", model.SearchText); Assert.Equal(0, model.SearchScopeIndex);
    }

    [Fact]
    public async Task OptionsExcludeOwnConstraintButRespectAllOtherColumnsAndCounts()
    {
        var model = await Open(); Select(model.CuttingTypeFilter, "Лазер");
        model.ManagerFilter.BeginEdit(); Assert.Equal(["Анна", "Борис"], model.ManagerFilter.Editor.Options.Select(option => option.Key));
        Assert.Equal([2, 1], model.ManagerFilter.Editor.Options.Select(option => option.Count));
        Select(model.ManagerFilter, "Анна");
        model.CuttingTypeFilter.BeginEdit(); Assert.Equal(["Лазер", "Плазма"], model.CuttingTypeFilter.Editor.Options.Select(option => option.Key));
        Assert.Equal([2, 1], model.CuttingTypeFilter.Editor.Options.Select(option => option.Count));
        model.ThicknessFilter.BeginEdit(); Assert.Equal(["0.8", "2"], model.ThicknessFilter.Editor.Options.Select(option => option.Key));
        model.BeginStatusFilterEdit(); Assert.Equal(1, model.StatusFilterEditor.Options.Single(option => option.Value == OrderStatusFilter.Work).Count);
        Assert.Equal(1, model.StatusFilterEditor.Options.Single(option => option.Value == OrderStatusFilter.Unmarked).Count);
        Assert.Equal(0, model.StatusFilterEditor.Options.Single(option => option.Value == OrderStatusFilter.Ready).Count);
        model.FilterDate = Date.ToDateTime(TimeOnly.MinValue); model.ThicknessFilter.BeginEdit(); Assert.Single(model.ThicknessFilter.Editor.Options);
        model.StatusFilter = OrderStatusFilter.Ready; model.ManagerFilter.BeginEdit();
        Assert.Equal(["Анна", "Борис"], model.ManagerFilter.Editor.Options.Select(option => option.Key));
        Assert.Equal([0, 1], model.ManagerFilter.Editor.Options.Select(option => option.Count));
    }

    [Fact]
    public async Task ContradictingSelectionRemainsRecoverableAndReapplyingUnchangedKeepsConstraint()
    {
        var model = await Open(); Select(model.CuttingTypeFilter, "ГАР"); Select(model.ManagerFilter, "Борис");
        Select(model.MaterialFilter, "5083H111"); model.StatusFilter = OrderStatusFilter.Work;
        Assert.Empty(model.Orders); model.MaterialFilter.BeginEdit();
        var option = Assert.Single(model.MaterialFilter.Editor.Options); Assert.True(option.IsChecked); Assert.Equal(0, option.Count);
        Assert.True(model.MaterialFilter.ApplyEdit()); Assert.True(model.MaterialFilter.HasFilter); Assert.Empty(model.Orders);
        model.ClearStatusFilterCommand.Execute(null); Assert.Equal([4], Ids(model));
        model.CuttingTypeFilter.ClearCommand.Execute(null); Assert.Equal([4], Ids(model));
        model.MaterialFilter.ClearCommand.Execute(null); model.ManagerFilter.ClearCommand.Execute(null); Assert.Equal(6, model.Orders.Count);
    }

    [Fact]
    public async Task DraftSearchCancelAndEmptyApplyNeverChangeRowsSelectionResultOrReadDatabase()
    {
        var reader = new Reader(); var model = await Open(reader);
        model.UpdateSelection(model.Orders.Take(2), model.Orders[0]); model.ResultText = "Письмо";
        var filter = model.MaterialFilter; filter.BeginEdit(); filter.Editor.ToggleAllCommand.Execute(null);
        Assert.False(filter.Editor.CanApply); Assert.False(filter.ApplyEdit());
        filter.Editor.Options.Single(option => option.Key == "Ст3").IsChecked = true;
        filter.Editor.SearchText = "09г"; Assert.Equal("09Г2С", Assert.Single(filter.Editor.VisibleOptions).Label);
        Assert.Equal(6, model.Orders.Count); Assert.Equal("Выбрано: 2", model.SelectionText); Assert.Equal("Письмо", model.ResultText);
        filter.BeginEdit(); Assert.True(filter.Editor.AllChecked); Assert.Equal("", filter.Editor.SearchText);
        Select(filter, "Ст3"); Assert.Equal([1, 2, 6], Ids(model)); Assert.Equal("Выбрано: 0", model.SelectionText); Assert.Equal("Письмо", model.ResultText);
        filter.BeginEdit(); filter.Editor.SearchText = "ничего"; Assert.True(filter.Editor.HasNoMatches); Assert.True(filter.Editor.CanApply);
        Assert.True(filter.ApplyEdit()); Assert.Equal([1, 2, 6], Ids(model)); Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task ThicknessSortsNumericallyAndMissingBendingIsNeverClassifiedAsNo()
    {
        var model = await Open(); model.ThicknessFilter.BeginEdit();
        Assert.Equal(["(Пусто)", "0,8", "2", "3", "10", "20"], model.ThicknessFilter.Editor.Options.Select(option => option.Label));
        Select(model.BendingFilter, "no"); Assert.Equal([2, 3, 6], Ids(model));
        model.BendingFilter.ClearCommand.Execute(null); Select(model.BendingFilter, "unknown"); Assert.Equal([5], Ids(model));
        model.ResetFiltersCommand.Execute(null); Select(model.ManagerFilter, ""); Assert.Equal([5], Ids(model));
    }

    [Fact]
    public async Task KeysAreCaseInsensitiveWithoutJoiningDifferentGradesAndReloadKeepsMissingSelections()
    {
        var rows = Rows(); var reader = new Reader { Items = rows.Append(rows[0] with { Id = 7, ManagerName = "анна", MaterialName = "ст3" }).ToArray() };
        var model = await Open(reader); model.ManagerFilter.BeginEdit();
        Assert.Equal(4, model.ManagerFilter.Editor.Options.Single(option => option.Key == "Анна").Count);
        Select(model.MaterialFilter, "Ст3"); Assert.Equal([1, 2, 6, 7], Ids(model));
        reader.Items = [rows[2]]; await model.ReloadCommand.ExecuteAsync(null); Assert.Empty(model.Orders);
        model.MaterialFilter.BeginEdit(); Assert.Equal(["09Г2С", "Ст3"], model.MaterialFilter.Editor.Options.Select(option => option.Key));
        Assert.Equal(0, model.MaterialFilter.Editor.Options.Single(option => option.Key == "Ст3").Count);
        model.MaterialFilter.ClearCommand.Execute(null); Assert.Equal([3], Ids(model)); Assert.Equal(2, reader.Calls);
    }

    [Fact]
    public async Task SelectingAllInContextExplicitlyClearsThatColumnOnlyAndBusyApplyIsRejected()
    {
        var model = await Open(); Select(model.ManagerFilter, "Анна"); Select(model.CuttingTypeFilter, "Лазер");
        model.ManagerFilter.BeginEdit(); model.ManagerFilter.Editor.ToggleAllCommand.Execute(null);
        Assert.True(model.ManagerFilter.Editor.AllChecked); model.IsUpdatingOrders = true;
        Assert.False(model.ManagerFilter.ApplyEdit()); Assert.Equal([1, 6], Ids(model));
        model.IsUpdatingOrders = false; Assert.True(model.ManagerFilter.ApplyEdit());
        Assert.False(model.ManagerFilter.HasFilter); Assert.True(model.CuttingTypeFilter.HasFilter); Assert.Equal([1, 2, 6], Ids(model));
    }

    [Fact]
    public async Task ReplacingSpecificationRefreshesFacetValuesAndRespectsCurrentConstraints()
    {
        var model = await Open(); Select(model.CuttingTypeFilter, "Лазер"); Select(model.ThicknessFilter, "2");
        var changed = Rows()[0] with { CuttingType = "ГАР", ThicknessMillimeters = 3 };
        model.AcceptEditedOrder(OrderRecord.FromSummary(changed)); Assert.Empty(model.Orders);
        model.CuttingTypeFilter.BeginEdit(); Assert.Equal("Лазер", Assert.Single(model.CuttingTypeFilter.Editor.Options).Key);
        Assert.All(model.CuttingTypeFilter.Editor.Options, option => Assert.Equal(0, option.Count));
        model.ThicknessFilter.ClearCommand.Execute(null); model.CuttingTypeFilter.BeginEdit();
        Assert.Equal(2, model.CuttingTypeFilter.Editor.Options.Single(option => option.Key == "ГАР").Count);
        Assert.Equal(2, model.CuttingTypeFilter.Editor.Options.Single(option => option.Key == "Лазер").Count);
    }

    [Fact]
    public async Task SelectingOnlyAvailableValueKeepsExplicitConstraintWhenParentFiltersAreCleared()
    {
        var model = await Open(); Select(model.ManagerFilter, "Анна"); Select(model.CuttingTypeFilter, "Плазма");
        Select(model.MaterialFilter, "09Г2С"); Assert.True(model.MaterialFilter.HasFilter);
        model.ManagerFilter.ClearCommand.Execute(null); model.CuttingTypeFilter.ClearCommand.Execute(null);
        Assert.Equal([3], Ids(model)); Assert.True(model.MaterialFilter.HasFilter);
        model.ManagerFilter.BeginEdit(); Assert.Single(model.ManagerFilter.Editor.Options);
        model.ManagerFilter.Editor.ToggleAllCommand.Execute(null); model.ManagerFilter.Editor.ToggleAllCommand.Execute(null);
        Assert.True(model.ManagerFilter.ApplyEdit()); Assert.False(model.ManagerFilter.HasFilter);
        model.MaterialFilter.ClearCommand.Execute(null); Assert.Equal(6, model.Orders.Count);
    }

    [Fact]
    public async Task StaleBendingAnnotationsBelongToUnknownRatherThanYesOrNo()
    {
        var rows = Rows(); rows[0] = rows[0] with { SpecificationNumber = "другой номер" };
        var model = await Open(new Reader { Items = rows }); Select(model.BendingFilter, "yes");
        Assert.Equal([4], Ids(model)); model.BendingFilter.ClearCommand.Execute(null);
        Select(model.BendingFilter, "unknown"); Assert.Equal([1, 5], Ids(model));
    }

    [Fact]
    public async Task SmallDecimalThicknessNeverBecomesExponentOrRoundedValue()
    {
        var rows = Rows(); var model = await Open(new Reader { Items = rows.Append(rows[0] with { Id = 7, ThicknessMillimeters = 0.000001m }).ToArray() });
        model.ThicknessFilter.BeginEdit(); var small = model.ThicknessFilter.Editor.Options[1];
        Assert.Equal("0.000001", small.Key); Assert.Equal("0,000001", small.Label);
        Select(model.ThicknessFilter, "0.000001"); Assert.Equal([7], Ids(model));
    }
}
