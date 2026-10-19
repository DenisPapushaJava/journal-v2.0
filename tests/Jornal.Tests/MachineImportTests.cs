using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineImportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryOfferedProfileCanBeCreatedAndReopenedWithoutChangingThePriceShape()
    {
        var store = new DemoOrderStore();
        foreach (var choice in MachineProfileChoice.Available)
        {
            var machines = await store.ReadMachinesAsync(Token);
            var form = new NewMachineViewModel(store.Prices, store.Prices, machines, PriceStorageKind.Session)
            { Name = "Станок " + machines.Count, Profile = choice, Thickness = "6", Rate = "123" };
            form.AddRateCommand.Execute(null);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.False(form.HasError, form.Error);
            var machine = Assert.Single(await store.ReadMachinesAsync(Token), machine => machine.Name == form.Name);
            Assert.Equal(choice.Id, machine.SpecificationProfileId);
            var reopened = new MachineImportViewModel(machine, store.Prices, store.Prices);
            Assert.Equal(choice, reopened.Profile);
            Assert.False(reopened.CanSave); await reopened.SaveCommand.ExecuteAsync(null);
            Assert.Equal(new PriceRate(6m, "6", 123), Assert.Single((await store.Prices.ReadAsync(machine.TableName, Token)).Rates));
        }
        Assert.Equal(BlockHtmlSpecificationParser.Profile, CuttingMachine.VintechProfile);
        Assert.Equal(HansPdfSpecificationParser.Profile, CuttingMachine.HansProfile);
        Assert.Equal(WaterjetPdfSpecificationParser.Profile, CuttingMachine.WaterjetProfile);
        Assert.Contains(MachineProfileChoice.Available, choice => choice.Name == "Vintech · HTML");
    }

    [Fact]
    public async Task LocalProfileUpdatePreservesHistoricalOrdersTariffsAndOtherMachinesAndRejectsStaleEditors()
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-machine-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "copy.journal-test.json");
            await LocalJournalStore.InitializeAsync(path, [], LocalJournalFixture.Prices, "fixture.mdb", new string('a', 64), Token);
            var store = new LocalJournalStore(path); var machine = MachineCatalogTests.Machine();
            await store.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
            var other = MachineCatalogTests.Machine("Второй");
            await store.CreateMachineAsync(MachineCatalogTests.Creation(other, machine), Token);
            await store.CreateAsync(MachineCatalogTests.Draft(machine), Token);
            var before = await store.ReadAsync(Token); var price = await store.ReadAsync(machine.TableName, Token);
            var change = new MachineImportChange(machine, CuttingMachine.VintechProfile);
            Assert.Equal(change.Values, await store.UpdateMachineImportAsync(change, Token));
            Assert.Contains(change.Values, await new LocalJournalStore(path).ReadMachinesAsync(Token));
            Assert.Contains(other, await store.ReadMachinesAsync(Token));
            Assert.Equal(before, await store.ReadAsync(Token));
            var staleOrder = await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateAsync(MachineCatalogTests.Draft(machine), Token));
            Assert.Equal(OrderWriteFailure.Conflict, staleOrder.Failure);
            var afterPrice = await store.ReadAsync(machine.TableName, Token);
            Assert.Equal(price.Name, afterPrice.Name); Assert.Equal(price.Rates, afterPrice.Rates);
            var bytes = await File.ReadAllBytesAsync(path, Token);
            var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateMachineImportAsync(new(machine, null), Token));
            Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, Token));
            await store.UpdateMachineImportAsync(new(change.Values, change.Values.SpecificationProfileId), Token);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, Token));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownWriteRequiresReadBackAndDoesNotRepeatAnAppliedChange(bool applied)
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
        var editor = new UncertainEditor(store.Prices, applied);
        var form = new MachineImportViewModel(machine, editor, store.Prices);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(0, editor.Calls);
        form.Profile = form.Profiles.Single(choice => choice.Id == CuttingMachine.HansProfile);
        Assert.True(form.HasChanges); Assert.True(form.CanSave);
        form.Profile = form.Profiles.Single(choice => choice.Id == machine.SpecificationProfileId);
        Assert.False(form.CanSave); Assert.False(form.HasChanges);
        form.Profile = form.Profiles.Single(choice => choice.Id == CuttingMachine.HansProfile);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.RequiresReload); Assert.False(form.CanSave); Assert.Equal(1, editor.Calls);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(1, editor.Calls);
        await form.ReloadCommand.ExecuteAsync(null);
        if (applied) { Assert.NotNull(form.SavedMachine); Assert.False(form.CanSave); Assert.Equal(1, editor.Calls); }
        else { Assert.True(form.CanSave); await form.SaveCommand.ExecuteAsync(null); Assert.NotNull(form.SavedMachine); Assert.Equal(2, editor.Calls); }
    }

    [Fact]
    public async Task ConflictingProfileCanBeRefreshedWithoutLosingTheUserChoice()
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
        var form = new MachineImportViewModel(machine, store.Prices, store.Prices);
        form.Profile = form.Profiles.Single(choice => choice.Id == CuttingMachine.HansProfile);
        await store.Prices.UpdateMachineImportAsync(new(machine, null), Token);
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.RequiresReload);
        await form.ReloadCommand.ExecuteAsync(null); Assert.True(form.CanSave);
        Assert.Equal(CuttingMachine.HansProfile, form.Profile.Id);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(CuttingMachine.HansProfile, Assert.Single(await store.ReadMachinesAsync(Token)).SpecificationProfileId);
    }

    [Theory]
    [InlineData(CuttingMachine.VintechProfile)]
    [InlineData(CuttingMachine.HansProfile)]
    [InlineData(CuttingMachine.WaterjetProfile)]
    public async Task ImportResolvesOnlyCompatibleMachinesAndKeepsMetadataAndOwnTariffs(string profile)
    {
        var preview = await Preview(profile);
        var machine = MachineCatalogTests.Machine("Новый станок") with { SpecificationProfileId = profile };
        var store = new DemoOrderStore();
        var thickness = decimal.Parse(preview.Fields["Thickness"].Value, System.Globalization.CultureInfo.InvariantCulture);
        await store.Prices.CreateMachineAsync(new(machine, [new(thickness, PriceTableChange.ColumnName(thickness), 123)], []), Token);
        var form = new NewOrderViewModel(store); await form.LoadMachinesAsync(Token);
        form.ApplyImport(preview);
        // Vintech/Hans имеют также встроенный станок: без выбора не берём первый из двух.
        if (profile != CuttingMachine.WaterjetProfile) Assert.Empty(form.CuttingType.Value);
        else Assert.Equal(machine.Name, form.CuttingType.Value);
        form.CuttingType.Value = machine.Name; form.ApplyImport(preview);
        Assert.Equal(machine.Name, form.CuttingType.Value);
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Заказчик"; form.Material.NeedsReview = false;
        await form.CalculatePriceCommand.ExecuteAsync(null); Assert.False(form.HasPriceError, form.PriceError);
        var draft = form.BuildDraft();
        Assert.Equal(machine, draft.Machine);
        Assert.Equal(machine.TableName, (await new CuttingPriceService(store.Prices).CalculateAsync(draft.PriceRequest, Token)).TableName);
        if (profile == CuttingMachine.HansProfile) { Assert.Equal("2", draft.MaterialId); Assert.Equal("N2", draft.CuttingGas); }
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.IsSaved, form.SaveError);
        var original = OrderRecord.FromSummary(form.CreatedOrder!);
        var edit = new EditOrderViewModel(original, store.Maintenance, "Демо", _ => Task.FromResult<OrderRecord?>(original))
        { ImportReader = new BatchImportTests.Reader(preview) };
        await edit.LoadMachinesAsync(Token);
        await edit.RequestImportAsync([profile == CuttingMachine.VintechProfile ? "spec.html" : "spec.pdf"], Token);
        edit.ReplaceImportCommand.Execute(null); edit.Material.NeedsReview = false;
        Assert.Equal(machine.Name, edit.CuttingType.Value);
        Assert.Equal(machine, edit.BuildChange().Machine);
        form = new NewOrderViewModel(store); await form.LoadMachinesAsync(Token); form.ApplyImport(preview);
        form.CuttingType.Value = "Несуществующий";
        Assert.NotNull(SpecificationMachineBinding.Validate(form.ImportPreview, form.CuttingType.Value, null));
        Assert.NotNull(SpecificationMachineBinding.Validate(preview, machine.Name, machine with { SpecificationProfileId = null }));
    }

    [Fact]
    public async Task BatchCardsRequireAnExplicitMachineForAmbiguousProfiles()
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine() with { SpecificationProfileId = CuttingMachine.VintechProfile };
        await store.Prices.CreateMachineAsync(new(machine, [new(6, "6", 123)], []), Token);
        var form = new NewOrderViewModel(store) { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        await form.ImportFilesAsync(["one.html", "two.html"], Token);
        Assert.Equal(2, form.BatchItems.Count);
        foreach (var item in form.BatchItems) { Assert.Empty(item.Form.CuttingType.Value); Assert.False(item.HasValidParameters); }
        foreach (var item in form.BatchItems) { item.Form.CuttingType.Value = machine.Name; Assert.True(item.HasValidParameters); }
    }

    [Fact]
    public async Task HostMustAcknowledgeTheExactProfileAndNoOtherPayload()
    {
        var machine = MachineCatalogTests.Machine(); var change = new MachineImportChange(machine, null);
        Assert.Equal(change.Values, await PriceEditingTests.Host(new(1, Machine: change.Values)).UpdateMachineImportAsync(change, Token));
        foreach (var response in new[] { new AccessHostResponse(1, Machine: machine), new(1), new(1, Machine: change.Values, PriceTable: new(machine.TableName, true, [])) })
        {
            var failure = await Assert.ThrowsAsync<OrderWriteException>(() => PriceEditingTests.Host(response).UpdateMachineImportAsync(change, Token));
            Assert.Equal(OrderWriteFailure.OutcomeUnknown, failure.Failure);
        }
    }

    internal static async Task<SpecificationImportPreview> Preview(string profile) => profile switch
    {
        CuttingMachine.VintechProfile => await BatchImportTests.Preview(),
        CuttingMachine.HansProfile => SpecificationImportPreview.Create("spec.pdf", new HansPdfSpecificationParser().Parse(HansSample.Document())),
        _ => SpecificationImportPreview.Create("spec.pdf", new WaterjetPdfSpecificationParser().Parse(WaterjetPdfParserTests.Document()))
    };

    private sealed class UncertainEditor(IMachineImportEditor inner, bool applied) : IMachineImportEditor
    {
        public int Calls { get; private set; }
        public async Task<CuttingMachine> UpdateMachineImportAsync(MachineImportChange change, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls > 1) return await inner.UpdateMachineImportAsync(change, cancellationToken);
            if (applied) await inner.UpdateMachineImportAsync(change, cancellationToken);
            throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Неизвестный исход.");
        }
    }
}
