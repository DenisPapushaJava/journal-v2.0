using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineNameRefreshTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LateResponseCannotUndoNewerNamesFromF5(bool olderFailed)
    {
        var store = new DemoOrderStore(); var source = new Reader(store);
        var model = new MainWindowViewModel(source);
        await model.ReloadCommand.ExecuteAsync(null);
        var oldSettings = await store.ReadMachineSettingsAsync(Token);
        var pending = Pending(); source.Next = pending.Task;
        var late = model.RefreshMachineNamesAsync(Token);
        await Rename(store, "Новое название");
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.Equal("Новое название", Laser(model).CuttingType);
        if (olderFailed) pending.SetException(new PriceReadException("Ошибка прежнего чтения"));
        else pending.SetResult(oldSettings);
        await late;
        Assert.Equal("Новое название", Laser(model).CuttingType);
        model.UpdateSelection([Laser(model)], Laser(model)); model.AddOrdersToResult([Laser(model)]);
        Assert.Contains("\t   НОВОЕ НАЗВАНИЕ", model.ResultText);
    }

    [Fact]
    public async Task CancelledNewerRequestKeepsTheVisibleSnapshotAndDoesNotReviveAnOlderResponse()
    {
        var store = new DemoOrderStore(); var source = new Reader(store);
        var model = new MainWindowViewModel(source); await model.ReloadCommand.ExecuteAsync(null);
        var older = Pending(); source.Next = older.Task;
        var late = model.RefreshMachineNamesAsync(Token);
        var newer = Pending(); source.Next = newer.Task;
        using var cancellation = new CancellationTokenSource();
        var cancelled = model.RefreshMachineNamesAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await Rename(store, "Устаревший ответ");
        older.SetResult(await store.ReadMachineSettingsAsync(Token)); await late;
        Assert.Equal("Лазер", Laser(model).CuttingType);
        newer.SetResult(await store.ReadMachineSettingsAsync(Token));
        await model.RefreshMachineNamesAsync(Token);
        Assert.Equal("Устаревший ответ", Laser(model).CuttingType);
    }

    [Fact]
    public async Task AlreadyCancelledCallDoesNotStartAReadOrSupersedeTheRunningRequest()
    {
        var store = new DemoOrderStore(); var source = new Reader(store);
        var model = new MainWindowViewModel(source); await model.ReloadCommand.ExecuteAsync(null);
        var pending = Pending(); source.Next = pending.Task;
        var running = model.RefreshMachineNamesAsync(Token);
        var reads = source.Reads;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.RefreshMachineNamesAsync(cancellation.Token));
        Assert.Equal(reads, source.Reads);
        await Rename(store, "Актуальный ответ");
        pending.SetResult(await store.ReadMachineSettingsAsync(Token)); await running;
        Assert.Equal("Актуальный ответ", Laser(model).CuttingType);
    }

    [Fact]
    public async Task SuccessfulReconnectionInvalidatesPendingReadsEvenWhenTheReaderInstanceIsReused()
    {
        var store = new DemoOrderStore(); var source = new Reader(store);
        var path = Path.Combine(Path.GetTempPath(), "journal-names-reconnect.mdb");
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: path)) };
        var model = OrderSourceTests.Model(new(settings, _ => source));
        await model.ReloadCommand.ExecuteAsync(null);
        var old = await store.ReadMachineSettingsAsync(Token);
        var pending = Pending(); source.Next = pending.Task;
        var late = model.RefreshMachineNamesAsync(Token);
        await Rename(store, "После подключения");
        var connection = model.CreateDatabaseSettingsForm()!; connection.UseDatabase = true; connection.DatabasePath = path;
        await connection.ConnectCommand.ExecuteAsync(null); Assert.True(connection.IsConnected, connection.Error);
        pending.SetResult(old); await late;
        Assert.Equal("После подключения", Laser(model).CuttingType);
    }

    [Fact]
    public async Task UnchangedNamesDoNotRebuildTheTableOrClearSelection()
    {
        var store = new DemoOrderStore(); var source = new Reader(store);
        var model = new MainWindowViewModel(source); await model.ReloadCommand.ExecuteAsync(null);
        var row = Laser(model); model.UpdateSelection([row], row);
        var selection = model.SelectionText;
        var reapplied = 0; model.ReadSnapshotApplying += (_, _) => reapplied++;
        await model.RefreshMachineNamesAsync(Token); await model.RefreshMachineNamesAsync(Token);
        Assert.Same(row, Laser(model)); Assert.Equal(selection, model.SelectionText); Assert.Equal(0, reapplied);
    }

    private static OrderRowViewModel Laser(MainWindowViewModel model) => model.Orders.Single(row => row.Source.CuttingType == "Лазер");
    private static TaskCompletionSource<IReadOnlyList<MachineSettings>> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static async Task Rename(DemoOrderStore store, string name)
    {
        var laser = (await store.ReadMachineSettingsAsync(Token)).Single(row => row.Name == "Лазер");
        await store.UpdateMachineSettingsAsync(new(laser, name, laser.SpecificationProfileId), Token);
    }
    private sealed class Reader(DemoOrderStore store) : IOrderReader, IPriceReader, IMachineSettingsReader
    {
        public int Reads;
        public Task<IReadOnlyList<MachineSettings>>? Next;
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken token = default) => store.ReadAsync(token);
        public Task<PriceTable> ReadAsync(string name, CancellationToken token = default) => store.ReadAsync(name, token);
        public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken token = default)
        {
            Reads++; var next = Next; Next = null;
            return next is null ? store.ReadMachineSettingsAsync(token) : next.WaitAsync(token);
        }
    }
}
