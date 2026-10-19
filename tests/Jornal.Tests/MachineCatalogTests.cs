using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineCatalogTests
{
    internal static CuttingMachine Machine(string name = "Гидроабразивная") => new(name, "PriceMachine_" + Guid.NewGuid().ToString("N"), CuttingMachine.WaterjetProfile);
    internal static MachineCreation Creation(CuttingMachine machine, params CuttingMachine[] expected) => new(machine, [new(2.5m, "2,5", 170), new(70m, "70", 600)], expected);
    internal static ManualOrderDraft Draft(CuttingMachine machine) => new(new(2026, 10, 7), "1007-1700", "Менеджер", "Заказчик", machine.Name,
        "12Х18Н10Т", 70m, "1500x3000", "1", 2, "150", 1m, "", "") { Machine = machine, CuttingDurationMinutes = 164 };

    [Fact]
    public void NewMachinesKeepLegacyPriceShapeAndRejectUnsafeOrAmbiguousDefinitions()
    {
        var machine = Machine(); var creation = Creation(machine).Snapshot();
        Assert.Equal(2, creation.Rates.Count); Assert.Equal("2,5", creation.Rates[0].ColumnName);
        foreach (var table in new[] { "PriceP", "PriceMachine_;DROP TABLE Orders", "PriceMachine_" + new string('x', 32) })
            Assert.Throws<ArgumentException>(() => (machine with { TableName = table }).Validate());
        Assert.Throws<ArgumentException>(() => (machine with { Name = "Лазер" }).Validate());
        Assert.Throws<ArgumentException>(() => (creation with { Rates = [new(2.5m, "2,5", 1), new(2.50m, "2,50", 2)] }).Snapshot());
        Assert.Throws<ArgumentException>(() => (creation with { Rates = [new(2.5m, "2,5", null)] }).Snapshot());
        Assert.Throws<ArgumentException>(() => (creation with { ExpectedMachines = [machine] }).Snapshot());
        Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.SelectTable(new("Лазер", 70m, 2, 1m), machine));
        Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.SelectTable(new(machine.Name, 70m, 2, 1m)));
    }

    [Fact]
    public async Task LocalCatalogTariffsCreateEditAndReopeningUseTheSameSharedMapping()
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-machines-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "book.journal-test.json");
            await LocalJournalStore.InitializeAsync(path, [], LocalJournalFixture.Prices, "fixture.mdb", new string('a', 64), TestContext.Current.CancellationToken);
            var store = new LocalJournalStore(path); var machine = Machine(); var creation = Creation(machine);
            Assert.Empty(await store.ReadMachinesAsync(TestContext.Current.CancellationToken));
            Assert.Equal(machine, await store.CreateMachineAsync(creation, TestContext.Current.CancellationToken));
            Assert.Equal(machine, Assert.Single(await new LocalJournalStore(path).ReadMachinesAsync(TestContext.Current.CancellationToken)));
            var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateMachineAsync(Creation(Machine("Второй станок")), TestContext.Current.CancellationToken));
            Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
            var quote = await new CuttingPriceService(store).CalculateAsync(Draft(machine).PriceRequest, TestContext.Current.CancellationToken);
            Assert.Equal(1200m, quote.StoredPrice);
            var created = await store.CreateAsync(Draft(machine) with { ExpectedQuote = quote }, TestContext.Current.CancellationToken);
            Assert.Equal(1200m, created.StoredCuttingPrice); Assert.Equal(machine.Name, created.CuttingType);
            var original = (await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
            var changed = await store.UpdateAsync(new(original, original with { CutLength = 3 }) { Machine = machine }, TestContext.Current.CancellationToken);
            Assert.Equal(1800m, changed.Price);
            var prices = await store.ReadAsync(machine.TableName, TestContext.Current.CancellationToken);
            var expanded = prices with { Rates = [.. prices.Rates, new(90m, "90", 800)] };
            await store.SaveAsync(new(prices, expanded), TestContext.Current.CancellationToken);
            Assert.Equal(3, (await store.ReadAsync(machine.TableName, TestContext.Current.CancellationToken)).Rates.Count);
            Assert.Equal(changed, await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken));
            var forged = machine with { TableName = Machine("Другой").TableName };
            await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateAsync(Draft(forged), TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DemoCreationAndPriceFormExposeNewMachineWithoutChangingLegacyDemoPricing()
    {
        var store = new DemoOrderStore(); var prices = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "Демо", PriceStorageKind.Session);
        await prices.RefreshCommand.ExecuteAsync(null); var form = prices.CreateMachineForm()!;
        form.Name = "Гидроабразивная"; form.Profile = form.Profiles.Single(choice => choice.Id == CuttingMachine.WaterjetProfile); form.Thickness = "70"; form.Rate = "600";
        form.AddRateCommand.Execute(null); Assert.Empty(await store.ReadMachinesAsync(TestContext.Current.CancellationToken));
        await form.SaveCommand.ExecuteAsync(null); Assert.False(form.HasError, form.Error);
        Assert.False(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.HasError, form.Error);
        Assert.Single(await store.ReadMachinesAsync(TestContext.Current.CancellationToken));
        await prices.ShowCreatedMachineAsync(form.CreatedMachine!);
        Assert.Equal(form.CreatedMachine!.TableName, prices.TableName);
        var orderForm = new NewOrderViewModel(store); await orderForm.LoadMachinesAsync(TestContext.Current.CancellationToken);
        Assert.Contains(form.CreatedMachine.Name, orderForm.CuttingType.Choices!);
        orderForm.CuttingType.Value = form.CreatedMachine.Name; Assert.True(orderForm.HasPricing);
        Assert.Equal(1200m, (await store.CreateAsync(Draft(form.CreatedMachine), TestContext.Current.CancellationToken)).StoredCuttingPrice);
        var legacy = Draft(form.CreatedMachine) with { CuttingType = "Лазер", ThicknessMillimeters = 2.5m, Machine = null };
        Assert.Null((await store.CreateAsync(legacy, TestContext.Current.CancellationToken)).StoredCuttingPrice);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainCreationMustBeReadBackBeforeRetryAndAppliedWritesAreNotRepeated(bool applied)
    {
        var store = new DemoOrderStore(); var creator = new UncertainCreator(store.Prices, applied);
        var form = new NewMachineViewModel(creator, store.Prices, [], PriceStorageKind.Session)
        { Name = "Гидроабразивная", Thickness = "70", Rate = "600" };
        form.AddRateCommand.Execute(null);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.RequiresReload); Assert.False(form.SaveCommand.CanExecute(null)); Assert.Equal(1, creator.Calls);
        await form.ReloadCommand.ExecuteAsync(null);
        if (applied)
        {
            Assert.NotNull(form.CreatedMachine); Assert.Equal(form.CreatedMachine, Assert.Single(await store.ReadMachinesAsync(TestContext.Current.CancellationToken)));
            Assert.Equal(1, creator.Calls);
        }
        else
        {
            Assert.Null(form.CreatedMachine); Assert.False(form.RequiresReload); Assert.True(form.SaveCommand.CanExecute(null));
            await form.SaveCommand.ExecuteAsync(null); Assert.Equal(2, creator.Calls); Assert.NotNull(form.CreatedMachine);
        }
    }
    private sealed class UncertainCreator(IMachineCreator inner, bool applied) : IMachineCreator
    {
        public int Calls { get; private set; }
        public async Task<CuttingMachine> CreateMachineAsync(MachineCreation creation, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls > 1) return await inner.CreateMachineAsync(creation, cancellationToken);
            if (applied) await inner.CreateMachineAsync(creation, cancellationToken);
            throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Неизвестный исход.");
        }
    }

    [Fact]
    public async Task HostMustConfirmBothMachineAndEveryTariffBeforeAcknowledgingCreation()
    {
        var creation = Creation(Machine()); var table = new PriceTable(creation.Machine.TableName, true, creation.Rates);
        Assert.Equal(creation.Machine, await PriceEditingTests.Host(new(1, Machine: creation.Machine, PriceTable: table)).CreateMachineAsync(creation, TestContext.Current.CancellationToken));
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => PriceEditingTests.Host(new(1, Machine: creation.Machine,
            PriceTable: table with { Rates = table.Rates.Take(1).ToArray() })).CreateMachineAsync(creation, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, failure.Failure);
    }
}
