using System.Collections.Specialized;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class GlobalJournalSearchTests
{
    internal static OrderSummary[] Rows() => LinkedColumnFilterTests.Rows().Select(row => row with
    {
        DeliveryNumber = row.Id is 1 or 2 or 6 ? "БЛЦБ-9285" : "БЛЦБ-9286"
    }).ToArray();
    internal sealed class Reader : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Items { get; set; } = Rows();
        public int Calls { get; private set; }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Items); }
    }
    private static int[] Ids(MainWindowViewModel model) => model.Orders.Select(row => row.Id).Order().ToArray();

    [Fact]
    public async Task NewQueryFindsOrdersOutsideEmptyFiltersThenFacetsNarrowTheMatches()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        var first = model.Orders.Single(row => row.Id == 1);
        model.ResultText = "Подготовленное письмо";
        model.UpdateSelection([first], first);
        LinkedColumnFilterTests.Select(model.CuttingTypeFilter, "ГАР");
        model.StatusFilter = OrderStatusFilter.Ready;
        model.FilterDate = new(2000, 1, 1);
        Assert.Empty(model.Orders);
        model.SearchText = "9285";
        Assert.Equal([1, 2, 6], Ids(model)); Assert.Null(model.FilterDate);
        Assert.False(model.HasAnyColumnFilter); Assert.False(model.HasStatusFilter);
        Assert.Equal("Выбрано: 0", model.SelectionText); Assert.False(model.CanEditOrder);
        Assert.Same(first, model.Orders.Single(row => row.Id == 1));
        Assert.Equal("Подготовленное письмо", model.ResultText);

        model.ManagerFilter.BeginEdit();
        Assert.Equal(["Анна", "Борис"], model.ManagerFilter.Editor.Options.Select(option => option.Key));
        Assert.Equal([2, 1], model.ManagerFilter.Editor.Options.Select(option => option.Count));
        LinkedColumnFilterTests.Select(model.ManagerFilter, "Анна"); Assert.Equal([1, 6], Ids(model));
        model.FilterDate = LinkedColumnFilterTests.Date.ToDateTime(TimeOnly.MinValue); Assert.Equal([1], Ids(model));
        model.StatusFilter = OrderStatusFilter.Shipped; Assert.Empty(model.Orders);
        model.SearchText = "9286";
        Assert.Equal([3, 4, 5], Ids(model)); Assert.False(model.HasAnyColumnFilter); Assert.Null(model.FilterDate);
        Assert.Equal(1, reader.Calls); // Набор текста не перечитывает сетевой файл.
    }

    [Fact]
    public async Task ScopeChangeStartsGlobalSearchAndClearingPreservesOnlyExplicitSearchField()
    {
        var model = new MainWindowViewModel(new Reader()); await model.ReloadCommand.ExecuteAsync(null);
        model.SearchText = "Анна"; Assert.Equal([1, 3, 6], Ids(model));
        model.FilterDate = new(2000, 1, 1); model.StatusFilter = OrderStatusFilter.Ready;
        model.SearchScopeIndex = (int)OrderSearchField.Manager;
        Assert.Equal([1, 3, 6], Ids(model)); Assert.Null(model.FilterDate); Assert.False(model.HasAnyColumnFilter);
        model.SearchScopeIndex = (int)OrderSearchField.DeliveryNumber; Assert.Empty(model.Orders);
        model.SearchText = "9285";
        LinkedColumnFilterTests.Select(model.ThicknessFilter, "10"); Assert.Equal([2], Ids(model));
        model.SearchText = " 9285  "; Assert.True(model.ThicknessFilter.HasFilter); Assert.Equal([2], Ids(model));
        model.ClearSearchCommand.Execute(null);
        Assert.Equal(6, model.Orders.Count); Assert.False(model.HasAnyColumnFilter);
        Assert.Equal((int)OrderSearchField.DeliveryNumber, model.SearchScopeIndex); Assert.Equal("", model.SearchText);
        model.ResetFiltersCommand.Execute(null); Assert.Equal(0, model.SearchScopeIndex);
    }

    [Fact]
    public async Task SearchUsesTheWholeLargeSnapshotEvenWhenTheVisibleListIsEmpty()
    {
        var seed = Rows()[0];
        var reader = new Reader
        {
            Items = Enumerable.Range(1, 22_000).Select(id => seed with
            {
                Id = id, SpecificationNumber = $"1018-{id}",
                DeliveryNumber = id == 22_000 ? "998877" : null
            }).ToArray()
        };
        var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        var last = model.Orders[^1];
        model.FilterDate = new(2000, 1, 1);
        Assert.Empty(model.Orders);
        var changes = new List<NotifyCollectionChangedAction>();
        model.Orders.CollectionChanged += (_, e) => changes.Add(e.Action);
        model.SearchText = "998877";
        Assert.Same(last, Assert.Single(model.Orders));
        Assert.Equal("Заявок: 1 из 22000", model.CountText);
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);
        Assert.Equal(1, reader.Calls);
    }

    [Fact]
    public async Task ReloadKeepsQueryAndFacetsAndFindsNewRowsWhileReplacingTheViewOnce()
    {
        var reader = new Reader(); var model = new MainWindowViewModel(reader);
        await model.ReloadCommand.ExecuteAsync(null);
        var first = model.Orders.Single(row => row.Id == 1);
        var changes = new List<NotifyCollectionChangedAction>();
        model.Orders.CollectionChanged += (_, e) => changes.Add(e.Action);
        model.SearchText = "9285";
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);
        LinkedColumnFilterTests.Select(model.ManagerFilter, "Анна");
        reader.Items = reader.Items.Append(reader.Items[0] with { Id = 7, SpecificationNumber = "1018-900" }).ToArray();
        changes.Clear(); await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal([1, 6, 7], Ids(model)); Assert.Equal("9285", model.SearchText); Assert.True(model.ManagerFilter.HasFilter);
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes); Assert.Equal(2, reader.Calls);
        Assert.Same(first, model.Orders.Single(row => row.Id == 1));
    }
}
