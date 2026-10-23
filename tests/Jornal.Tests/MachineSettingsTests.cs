using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineSettingsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NamesAreSeparateFromWorkingKeysPricesAndHistoricOrders()
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
        var before = await store.ReadAsync(Token); var price = await store.Prices.ReadAsync(machine.TableName, Token);
        var settings = await store.ReadMachineSettingsAsync(Token);
        var legacy = settings.Single(row => row.Name == "Лазер");
        var custom = settings.Single(row => row.Key == machine.TableName);
        var saved = await store.UpdateMachineSettingsAsync(new(custom, "Водорез", CuttingMachine.VintechProfile), Token);
        await store.UpdateMachineSettingsAsync(new(legacy, "Лазерный станок", legacy.SpecificationProfileId), Token);
        Assert.Equal(machine.TableName, saved.Key); Assert.Equal(machine.Name, saved.Name);
        Assert.Equal("Водорез", saved.DisplayName);
        var originalCatalog = Assert.Single(await store.ReadMachinesAsync(Token));
        Assert.Equal(machine.Name, originalCatalog.Name); Assert.Equal(CuttingMachine.VintechProfile, originalCatalog.SpecificationProfileId);
        Assert.Equal(before, await store.ReadAsync(Token));
        Assert.Equal(price, await store.Prices.ReadAsync(machine.TableName, Token));
        var reverted = await store.UpdateMachineSettingsAsync(new(saved, saved.Name, saved.SpecificationProfileId), Token);
        Assert.Equal(machine.Name, reverted.DisplayName);
        Assert.Equal(4, MachineSettings.Build([]).Count);
    }

    [Fact]
    public async Task DuplicateOrReservedNamesAndBuiltinImportChangesNeverMutateTheCatalog()
    {
        var store = new DemoOrderStore(); var rows = await store.ReadMachineSettingsAsync(Token);
        var laser = rows.Single(row => row.Name == "Лазер");
        foreach (var name in new[] { "Плазма", "плазма", "Прочие прайсы", " ", new string('А', 256), "Станок\n2" })
            await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateMachineSettingsAsync(new(laser, name, laser.SpecificationProfileId), Token));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateMachineSettingsAsync(new(laser, laser.DisplayName, CuttingMachine.HansProfile), Token));
        Assert.Equal(rows, await store.ReadMachineSettingsAsync(Token));
        await store.UpdateMachineSettingsAsync(new(laser, "Новый станок", laser.SpecificationProfileId), Token);
        var added = MachineCatalogTests.Machine("Новый станок");
        await Assert.ThrowsAsync<ArgumentException>(() => store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(added), Token));
        Assert.Empty(await store.ReadMachinesAsync(Token));
    }

    [Fact]
    public void OlderClientNameCollisionCanBeDisplayedAndCorrectedWithoutChangingWorkingKeys()
    {
        var machine = MachineCatalogTests.Machine("Лазерный станок");
        var labels = new Dictionary<string, string> { [MachineSettings.LegacyKey("Лазер")] = "Лазерный станок" };
        var rows = MachineSettings.Build([machine], labels);
        var names = MachineSettings.Names(rows);
        Assert.Equal("Лазерный станок (Лазер)", names["Лазер"]);
        Assert.NotEqual(names["Лазер"], names[machine.Name]);
        var original = rows.Single(row => row.Name == "Лазер");
        var correction = new MachineSettingsChange(original, "Лазер № 1", original.SpecificationProfileId);
        correction.Verify(rows);
        Assert.Equal("Лазер", correction.Values.Name);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RefreshAfterConflictRetainsOnlyTheUsersOwnChanges(bool ownName)
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
        var settings = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Key == machine.TableName);
        var form = new MachineImportViewModel(settings, store.Prices, store.Prices);
        if (ownName) form.Name = "Моё название";
        else form.Profile = form.Profiles.Single(row => row.Id == CuttingMachine.HansProfile);
        await store.UpdateMachineSettingsAsync(new(settings, ownName ? settings.DisplayName : "Название коллеги",
            ownName ? CuttingMachine.VintechProfile : settings.SpecificationProfileId), Token);
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.RequiresReload);
        await form.ReloadCommand.ExecuteAsync(null); Assert.False(form.RequiresReload);
        Assert.Equal(ownName ? "Моё название" : "Название коллеги", form.Name);
        Assert.Equal(ownName ? CuttingMachine.VintechProfile : CuttingMachine.HansProfile, form.Profile.Id);
        await form.SaveCommand.ExecuteAsync(null); Assert.NotNull(form.SavedMachine);
    }

    [Fact]
    public async Task LocalSettingsRoundTripAreAtomicAndUnchangedSaveDoesNotWrite()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync(); var store = new LocalJournalStore(fixture.Path);
        var before = await store.ReadAsync(Token); var old = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Name == "Лазер");
        var saved = await store.UpdateMachineSettingsAsync(new(old, "Лазер № 1", old.SpecificationProfileId), Token);
        Assert.Contains(saved, await new LocalJournalStore(fixture.Path).ReadMachineSettingsAsync(Token));
        Assert.Equal(before, await store.ReadAsync(Token)); var bytes = await File.ReadAllBytesAsync(fixture.Path, Token);
        await store.UpdateMachineSettingsAsync(new(saved, saved.DisplayName, saved.SpecificationProfileId), Token);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path, Token));
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateMachineSettingsAsync(new(old, "Чужой снимок", old.SpecificationProfileId), Token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure); Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path, Token));
    }

    [Fact]
    public async Task JournalUsesTheDisplayNameForRowsFiltersSearchAndNewTextButKeepsCanonicalValues()
    {
        var store = new DemoOrderStore(); var old = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Name == "Лазер");
        await store.UpdateMachineSettingsAsync(new(old, "Точный раскрой", old.SpecificationProfileId), Token);
        var model = await BatchOrderTests.Model(store);
        var row = model.Orders.Single(row => row.Source.CuttingType == "Лазер");
        Assert.Equal("Точный раскрой", row.CuttingType); Assert.Equal("Лазер", row.Source.CuttingType);
        model.CuttingTypeFilter.BeginEdit();
        Assert.Contains(model.CuttingTypeFilter.Editor.Options, option => option.Key == "Лазер" && option.Label == "Точный раскрой");
        model.SearchText = "Точный раскрой"; Assert.Same(row, Assert.Single(model.Orders));
        model.UpdateSelection([row], row); model.AddOrdersToResult([row]); Assert.Contains("\t   ТОЧНЫЙ РАСКРОЙ", model.ResultText);
        var clipboard = new RecordingJournalClipboard(); await model.CopyCommentAsync(clipboard, true);
        Assert.Contains("на Точный раскрой", clipboard.Text);
        Assert.Equal("Лазер", (await store.ReadAsync(Token)).Single(order => order.Id == row.Id).CuttingType);
        var current = (await store.ReadMachineSettingsAsync(Token)).Single(machine => machine.Name == "Лазер");
        var letter = model.ResultText;
        await store.UpdateMachineSettingsAsync(new(current, "Современный раскрой", current.SpecificationProfileId), Token);
        await model.RefreshMachineNamesAsync(Token);
        Assert.Empty(model.Orders); Assert.Equal(letter, model.ResultText);
        model.SearchText = "Современный раскрой"; Assert.Same(row, Assert.Single(model.Orders));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task RenamingKeepsSelectionAndReappliesTheExistingMachineSort(bool dark)
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store);
        var window = new MainWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = 1280, Height = 720 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.SelectedItem = model.Orders.Single(row => row.Source.CuttingType == "Лазер");
            grid.Columns.Single(column => column.Header is "Вид резки").Sort(System.ComponentModel.ListSortDirection.Ascending);
            var before = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Name == "Лазер");
            await store.UpdateMachineSettingsAsync(new(before, "Я-станок", before.SpecificationProfileId), Token);
            await model.RefreshMachineNamesAsync(Token); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Assert.IsType<OrderRowViewModel>(grid.SelectedItem).Id);
            Assert.Equal("Я-станок", grid.CollectionView.Cast<OrderRowViewModel>().Last().CuttingType);
            Assert.True(model.HasCustomSort);
            await model.ReloadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Assert.IsType<OrderRowViewModel>(grid.SelectedItem).Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreationAndEditingShowTheAliasWithoutChangingTheSelectedWorkingValue(bool dark)
    {
        var store = new DemoOrderStore(); var old = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Name == "Лазер");
        await store.UpdateMachineSettingsAsync(new(old, "Лазер № 1", old.SpecificationProfileId), Token);
        var form = ManualOrderTests.ValidForm(store); form.CuttingType.Value = "Лазер"; await form.LoadMachinesAsync(Token);
        var window = new NewOrderWindow { DataContext = form, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Лазер", form.CuttingType.Value);
            var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "Choice" && ReferenceEquals(control.DataContext, form.CuttingType));
            Assert.Equal("Лазер", combo.SelectedItem);
            Assert.Contains(combo.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Лазер № 1");
            Assert.Equal("Лазер", form.BuildDraft().CuttingType);
            var record = (await store.Maintenance.ReadRecordAsync(1, Token))!;
            var edit = new EditOrderViewModel(record, store.Maintenance, "Демо", _ => Task.FromResult<OrderRecord?>(record));
            await edit.LoadMachinesAsync(Token);
            Assert.Equal("Лазер № 1", edit.CuttingType.ChoiceName(edit.CuttingType.Value));
            Assert.False(edit.HasChanges);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task SchemaPreparationIsExplicitAndASecondClientReusesAnExistingTable()
    {
        var store = new DemoOrderStore(); var initializers = new Initializers();
        var reader = new OrderDurationPreparingReader(store, initializers, labels: initializers);
        await reader.ReadSnapshotAsync(null, Token); Assert.Equal(0, initializers.Labels);
        await reader.ReadAsync(Token); await reader.ReadAsync(Token); Assert.Equal(1, initializers.Labels);
        await reader.ReadSnapshotAsync(null, Token); Assert.Equal(1, initializers.Labels);
    }
    [Fact]
    public async Task UnavailableLabelsKeepTheCanonicalCatalogAndPricesUsable()
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
        var reader = new UnavailableNames(store);
        var form = new NewOrderViewModel(reader); await form.LoadMachinesAsync(Token);
        Assert.Contains(machine.Name, form.CuttingType.Choices!);
        var prices = new PriceEditorViewModel(reader, store.Prices, "Демо", "Демо", PriceStorageKind.Session);
        await prices.RefreshCommand.ExecuteAsync(null);
        Assert.NotEmpty(prices.Rows); Assert.False(prices.CanEditMachineImport); Assert.False(prices.CanAddMachine);
        Assert.True(prices.HasError);
    }
    private sealed class UnavailableNames(DemoOrderStore store) : IPriceReader, IMachineCatalogReader, IMachineSettingsReader, IManualOrderCreator
    {
        public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<MachineSettings>>(new PriceReadException("Названия недоступны"));
        public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default) => store.ReadMachinesAsync(cancellationToken);
        public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default) => store.Prices.ReadAsync(tableName, cancellationToken);
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) => store.CreateAsync(draft, cancellationToken);
    }
    private sealed class Initializers : IOrderDurationInitializer, IMachineLabelsInitializer
    {
        public int Labels;
        public Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PrepareMachineLabelsAsync(CancellationToken cancellationToken = default) { Labels++; return Task.CompletedTask; }
    }
}
