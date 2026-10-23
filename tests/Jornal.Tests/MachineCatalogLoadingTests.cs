using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineCatalogLoadingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string FirstPath => Path.Combine(Path.GetTempPath(), "journal-names-first.mdb");
    private static string SecondPath => Path.Combine(Path.GetTempPath(), "journal-names-second.mdb");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SourceSwitchAppliesItsOwnNamesBeforeRowsAndTextAreAvailable(bool namesUnavailable)
    {
        var first = await Source("Станок первой базы"); var second = await Source("Станок второй базы");
        second.FailNames = namesUnavailable;
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: FirstPath)) };
        var model = OrderSourceTests.Model(new(settings, path => path == FirstPath ? first : second));
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal("Станок первой базы", model.Orders.Single(row => row.Source.CuttingType == "Лазер").CuttingType);
        var connection = model.CreateDatabaseSettingsForm()!;
        connection.UseDatabase = true; connection.DatabasePath = SecondPath;
        await connection.ConnectCommand.ExecuteAsync(null);
        Assert.True(connection.IsConnected, connection.Error); Assert.Equal(SecondPath, model.DatabasePath);
        var expected = namesUnavailable ? "Лазер" : "Станок второй базы";
        var row = model.Orders.Single(row => row.Source.CuttingType == "Лазер");
        Assert.Equal(expected, row.CuttingType);
        model.CuttingTypeFilter.BeginEdit();
        Assert.Contains(model.CuttingTypeFilter.Editor.Options, option => option.Key == "Лазер" && option.Label == expected);
        model.UpdateSelection([row], row); model.AddOrdersToResult([row]);
        Assert.Contains("\t   " + expected.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), model.ResultText);
        var clipboard = new RecordingJournalClipboard(); await model.CopyCommentAsync(clipboard, true);
        Assert.Contains("на " + expected, clipboard.Text);
        Assert.DoesNotContain("Станок первой базы", model.ResultText);
        var reapplied = 0; model.ReadSnapshotApplying += (_, _) => reapplied++;
        var nameReads = second.SettingsReads;
        Assert.Equal(JournalRefreshOutcome.Refreshed, await model.RefreshAutomaticallyAsync(Token));
        Assert.Equal(0, reapplied); Assert.Equal(nameReads, second.SettingsReads);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedOrCancelledConnectionKeepsPreviousSourceAndNames(bool cancel)
    {
        var first = await Source("Первый станок"); var second = await Source("Второй станок");
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: FirstPath)) };
        var model = OrderSourceTests.Model(new(settings, path => path == FirstPath ? first : second));
        await model.ReloadCommand.ExecuteAsync(null);
        settings.Fail = !cancel;
        var pending = new TaskCompletionSource<IReadOnlyList<MachineSettings>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancel) second.PendingNames = pending.Task;
        var connection = model.CreateDatabaseSettingsForm()!; connection.UseDatabase = true; connection.DatabasePath = SecondPath;
        var connecting = connection.ConnectCommand.ExecuteAsync(null);
        if (cancel)
        {
            Assert.True(model.IsLoadingOrders); connection.ConnectCommand.Cancel();
        }
        await connecting;
        Assert.False(connection.IsConnected); Assert.Equal(FirstPath, model.DatabasePath);
        Assert.Equal("Первый станок", model.Orders.Single(row => row.Source.CuttingType == "Лазер").CuttingType);
        if (cancel) pending.SetResult(await second.Store.ReadMachineSettingsAsync(Token));
        Assert.Equal("Первый станок", model.Orders.Single(row => row.Source.CuttingType == "Лазер").CuttingType);
    }

    [Fact]
    public async Task LateNamesFromThePreviousDatabaseCannotReplaceTheCurrentDictionary()
    {
        var first = await Source("Первый станок"); var second = await Source("Второй станок");
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: FirstPath)) };
        var model = OrderSourceTests.Model(new(settings, path => path == FirstPath ? first : second));
        await model.ReloadCommand.ExecuteAsync(null);
        var pending = new TaskCompletionSource<IReadOnlyList<MachineSettings>>(TaskCreationOptions.RunContinuationsAsynchronously);
        first.PendingNames = pending.Task;
        var late = model.RefreshMachineNamesAsync(Token);
        var connection = model.CreateDatabaseSettingsForm()!; connection.UseDatabase = true; connection.DatabasePath = SecondPath;
        await connection.ConnectCommand.ExecuteAsync(null); Assert.True(connection.IsConnected, connection.Error);
        pending.SetResult(await first.Store.ReadMachineSettingsAsync(Token));
        await late;
        Assert.Equal("Второй станок", model.Orders.Single(row => row.Source.CuttingType == "Лазер").CuttingType);
    }

    [Fact]
    public async Task FormsAndPricesUseOneSnapshotAndFallbackOnlyWhenNamesAreUnavailable()
    {
        var source = await Source("Лазерный станок");
        var machine = MachineCatalogTests.Machine();
        await source.Store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), Token);
        var create = new NewOrderViewModel(source);
        var record = (await source.Store.Maintenance.ReadRecordAsync(1, Token))!;
        var edit = new EditOrderViewModel(record, source, "Тест", _ => Task.FromResult<OrderRecord?>(record));
        var prices = new PriceEditorViewModel(source, source.Store.Prices, "Тест", "Тест", PriceStorageKind.Session);
        foreach (var failed in new[] { false, true })
        {
            source.FailNames = failed; source.CatalogReads = source.SettingsReads = 0;
            await create.LoadMachinesAsync(Token); await edit.LoadMachinesAsync(Token); await prices.RefreshCommand.ExecuteAsync(null);
            Assert.Equal(3, source.SettingsReads); Assert.Equal(failed ? 3 : 0, source.CatalogReads);
            var expected = failed ? "Лазер" : "Лазерный станок";
            Assert.Equal(expected, create.CuttingType.ChoiceName("Лазер"));
            Assert.Equal(expected, edit.CuttingType.ChoiceName("Лазер"));
            Assert.Equal(expected, prices.Groups.Single(group => group.Name == "Лазер").DisplayName);
            Assert.Contains(machine.Name, create.CuttingType.Choices!); Assert.Contains(machine.Name, edit.CuttingType.Choices!);
            Assert.False(edit.HasChanges); Assert.NotEmpty(prices.Rows);
            Assert.Equal(!failed, prices.CanEditMachineImport); Assert.Equal(!failed, prices.CanAddMachine);
        }
    }

    [Fact]
    public async Task CanonicalOnlySourceNeedsJustOneCatalogRead()
    {
        var store = new DemoOrderStore(); var source = new CanonicalOnly(store);
        var snapshot = await MachineCatalogSnapshot.ReadAsync(source, Token);
        Assert.Equal(1, source.Reads); Assert.Null(snapshot.NamesError); Assert.Equal(4, snapshot.Settings.Count);
    }

    [Fact]
    public async Task InvalidSettingsFallBackToTheValidatedCanonicalCatalog()
    {
        var source = await Source("Станок");
        source.PendingNames = Task.FromResult<IReadOnlyList<MachineSettings>>([]);
        var snapshot = await MachineCatalogSnapshot.ReadAsync(source, Token);
        Assert.Equal(1, source.SettingsReads); Assert.Equal(1, source.CatalogReads);
        Assert.NotNull(snapshot.NamesError); Assert.Empty(snapshot.Settings); Assert.Equal("Лазер", snapshot.Names["Лазер"]);
    }

    private static async Task<CountingSource> Source(string label)
    {
        var store = new DemoOrderStore(); var machine = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Name == "Лазер");
        await store.UpdateMachineSettingsAsync(new(machine, label, machine.SpecificationProfileId), Token);
        return new(store);
    }
    private sealed class CanonicalOnly(DemoOrderStore store) : IMachineCatalogReader
    {
        public int Reads;
        public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken token = default)
        { Reads++; return store.ReadMachinesAsync(token); }
    }
    private sealed class CountingSource(DemoOrderStore store) : IOrderReader, IPriceReader, IMachineSettingsReader,
        IMachineCatalogReader, IManualOrderCreator, IOrderMaintenance
    {
        public DemoOrderStore Store => store;
        public int CatalogReads, SettingsReads;
        public bool FailNames;
        public Task<IReadOnlyList<MachineSettings>>? PendingNames;
        public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken token = default)
        {
            SettingsReads++;
            return FailNames ? Task.FromException<IReadOnlyList<MachineSettings>>(new PriceReadException("Названия недоступны")) :
                PendingNames is { } pending ? pending.WaitAsync(token) : store.ReadMachineSettingsAsync(token);
        }
        public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken token = default)
        { CatalogReads++; return store.ReadMachinesAsync(token); }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken token = default) => store.ReadAsync(token);
        public Task<PriceTable> ReadAsync(string name, CancellationToken token = default) => store.ReadAsync(name, token);
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken token = default) => store.CreateAsync(draft, token);
        public Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken token = default) => store.Maintenance.ReadRecordAsync(id, token);
        public Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken token = default) => store.Maintenance.UpdateAsync(change, token);
        public Task DeleteAsync(OrderRecord record, CancellationToken token = default) => store.Maintenance.DeleteAsync(record, token);
        public Task<OrderRecord> RestoreAsync(OrderRecord record, CancellationToken token = default) => store.Maintenance.RestoreAsync(record, token);
    }
}
