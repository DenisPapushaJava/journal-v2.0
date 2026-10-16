using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Jornal.Core.Pricing;
using Xunit;

namespace Jornal.Tests;

public sealed class SheetCountTests
{
    [Theory]
    [InlineData("2+1")]
    [InlineData("02 / комплект")]
    [InlineData("1,2,3")]
    [InlineData("1.2.3")]
    [InlineData("1,2.3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("комплект")]
    [InlineData("1 000")]
    [InlineData("2e3")]
    [InlineData("")]
    [InlineData("2\0")]
    public async Task InvalidCountIsRejectedByFormsAndWriteContractsWithoutLosingInput(string value)
    {
        var draft = Draft() with { SheetCount = value };
        Assert.Throws<OrderWriteException>(draft.Validate);
        var original = OrderMaintenanceTests.Record() with { SheetCount = "2" };
        Assert.Throws<OrderWriteException>(() => new OrderEditChange(original, original with { SheetCount = value }).Validate());

        var store = new DemoOrderStore();
        await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateAsync(draft, TestContext.Current.CancellationToken));
        var create = ManualOrderTests.ValidForm(store);
        create.SheetCount.Value = value;
        await create.SaveCommand.ExecuteAsync(null);
        Assert.False(create.IsSaved); Assert.True(create.SheetCount.HasError); Assert.Equal(value, create.SheetCount.Value);
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);

        var backend = new OrderMaintenanceTests.Backend { Current = original };
        var edit = new EditOrderViewModel(original, backend, "Тестовый источник", _ => Task.FromResult<OrderRecord?>(backend.Current));
        edit.SheetCount.Value = value;
        await edit.SaveCommand.ExecuteAsync(null);
        Assert.Null(edit.SavedRecord); Assert.True(edit.SheetCount.HasError); Assert.Equal(value, edit.SheetCount.Value);
        Assert.Equal(0, backend.Updates); Assert.Equal(original, backend.Current);

        create.SheetCount.Value = "3";
        await create.SaveCommand.ExecuteAsync(null);
        Assert.True(create.IsSaved); Assert.False(create.SheetCount.HasError); Assert.Equal("3", create.CreatedOrder!.SheetCount);
        edit.SheetCount.Value = "3";
        Assert.Equal("3", edit.BuildChange().Values.SheetCount); Assert.False(edit.SheetCount.HasError);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    [InlineData("12000")]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("1,3")]
    [InlineData("1.3")]
    [InlineData("0.5")]
    [InlineData("3.0")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("+2")]
    public async Task ManualNumberCanBeWrittenUsingTheExistingTextField(string value)
    {
        var draft = Draft() with { SheetCount = value };
        draft.Validate();
        Assert.Equal(value, draft.ToOrder(100, null).SheetCount);
        var original = OrderMaintenanceTests.Record();
        new OrderEditChange(original, original with { SheetCount = value }).Validate();
        var create = ManualOrderTests.ValidForm(new DemoOrderStore());
        create.SheetCount.Value = value;
        await create.SaveCommand.ExecuteAsync(null);
        Assert.True(create.IsSaved); Assert.Equal(value, create.CreatedOrder!.SheetCount);
        var backend = new OrderMaintenanceTests.Backend { Current = original };
        var edit = new EditOrderViewModel(original, backend, "Тестовый источник", _ => Task.FromResult<OrderRecord?>(backend.Current));
        edit.SheetCount.Value = value;
        await edit.SaveCommand.ExecuteAsync(null);
        await edit.SaveCommand.ExecuteAsync(null);
        Assert.Equal(value, edit.SavedRecord!.SheetCount);
    }

    [Theory]
    [InlineData("1,3")]
    [InlineData("02 / комплект")]
    [InlineData(null)]
    [InlineData("")]
    public async Task UnchangedHistoricalCountIsNeverGuessedOrRewritten(string? value)
    {
        var original = OrderMaintenanceTests.Record() with { SheetCount = value };
        original.Validate();
        var backend = new OrderMaintenanceTests.Backend { Current = original };
        var edit = new EditOrderViewModel(original, backend, "Тестовый источник", _ => Task.FromResult<OrderRecord?>(backend.Current));
        edit.Customer.Value = "Исправленный заказчик";
        Assert.Equal(value, edit.BuildChange().Values.SheetCount);
        await edit.SaveCommand.ExecuteAsync(null);
        await edit.SaveCommand.ExecuteAsync(null);
        Assert.Equal(value, edit.SavedRecord!.SheetCount);
    }

    [Fact]
    public async Task FractionalManualCountSurvivesReopeningTheLinuxLocalCopy()
    {
        var root = Directory.CreateTempSubdirectory("journal-sheet-count-");
        try
        {
            var path = Path.Combine(root.FullName, "orders.journal-test.json");
            await LocalJournalStore.InitializeAsync(path, [], PriceTables.All.Select(name =>
                    new PriceTable(name, true, [new(2.5m, "2,5", 17)])).ToArray(),
                "synthetic.mdb", new string('a', 64), TestContext.Current.CancellationToken);
            var saved = await new LocalJournalStore(path).CreateAsync(Draft() with { SheetCount = "1,3" }, TestContext.Current.CancellationToken);
            var reopened = await new LocalJournalStore(path).ReadAsync(TestContext.Current.CancellationToken);
            Assert.Equal("1,3", saved.SheetCount); Assert.Equal(saved, Assert.Single(reopened));
        }
        finally { root.Delete(true); }
    }

    private static ManualOrderDraft Draft() => new(new(2026, 1, 2), "0102-44", "Имя", "Клиент", "Лазер", "5083H111", 2.5m,
        "1500x3000", "2", 25, "38,5", 1.25m, "", "");
}
