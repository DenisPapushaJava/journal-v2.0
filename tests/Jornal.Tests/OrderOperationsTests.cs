using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderOperationsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task ManualCreateEditAndTaskKeepIndependentOperationsWithoutChangingPrice(bool bending, bool chamfer)
    {
        var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
        form.Annotations.HasBending = bending; form.Annotations.HasChamfer = chamfer;
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.IsSaved);
        var row = form.CreatedOrder!;
        Assert.Equal(bending, row.HasBending); Assert.Equal(chamfer, row.HasChamfer);
        Assert.Null(row.StoredCuttingPrice); // Прежний деморежим сохраняет неизвестную цену без принудительного расчёта.
        var text = OrderCommentFormatter.Telegram([row with { DeliveryNumber = "БЛЦБ-42" }]);
        Assert.Equal(bending, text.Contains("ГИБКА")); Assert.Equal(chamfer, text.Contains("ФАСКА"));
        if (bending && chamfer) Assert.Contains("ГИБКА, ФАСКА", text);
        var record = (await store.Maintenance.ReadRecordAsync(row.Id, Token))!;
        var edit = new EditOrderViewModel(record, store.Maintenance, "test", _ => Task.FromResult<OrderRecord?>(record));
        Assert.False(edit.HasChanges); edit.Annotations.HasChamfer = !chamfer;
        Assert.Contains(edit.Changes, item => item.Label == "Фаска");
        var change = edit.BuildChange(); Assert.False(change.RequiresPriceCalculation);
        var saved = await store.Maintenance.UpdateAsync(change, Token);
        Assert.Equal(!chamfer, saved.ToSummary().HasChamfer); Assert.Equal(row.StoredCuttingPrice, saved.Price);
        Assert.Equal(record.Annotations, saved.Annotations);
    }

    [Fact]
    public async Task ReplacementAndIdentityReviewRetainChamferAndBlockUnconfirmedTask()
    {
        var record = OrderMaintenanceTests.Record(); record = record with { Operations = OrderOperations.For(record.ToSummary(), true) };
        var changed = record with { Specification = "Другой номер" };
        var row = changed.ToSummary(); Assert.True(row.OperationsNeedReview); Assert.False(row.HasChamfer);
        Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram([row with { DeliveryNumber = "42" }]));
        Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram([record.ToSummary() with { OperationsUnavailable = true, DeliveryNumber = "42" }]));
        var store = new DemoOrderStore();
        var edit = new EditOrderViewModel(record, store.Maintenance, "test", _ => Task.FromResult<OrderRecord?>(record))
            { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        await edit.RequestImportAsync(["replace.html"], Token); edit.ReplaceImportCommand.Execute(null);
        Assert.True(edit.Annotations.HasChamfer); Assert.True(edit.Annotations.NeedsReview); Assert.False(edit.CanSave);
        edit.Annotations.ConfirmReviewCommand.Execute(null); Assert.False(edit.Annotations.NeedsReview);
        var values = edit.BuildChange().Values; Assert.True(values.ToSummary().HasChamfer);
        Assert.True(values.Operations!.Matches(values.ToSummary()));
    }

    [Fact]
    public async Task BatchKeepsFirstManualChamferAndEveryCardHasItsOwnFlag()
    {
        var form = ManualOrderTests.ValidForm(new DemoOrderStore()); form.Annotations.HasChamfer = true;
        form.ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview());
        await form.ImportFilesAsync(["one.html", "two.html"], Token);
        Assert.True(form.BatchItems[0].Form.Annotations.HasChamfer);
        Assert.All(form.BatchItems.Skip(1), item => Assert.False(item.Form.Annotations.HasChamfer));
        form.BatchItems[2].Form.Annotations.HasChamfer = true;
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.IsSaved);
        Assert.Equal(new[] { true, false, true }, form.CreatedOrders.Select(row => row.HasChamfer));
    }

    [Fact]
    public async Task LocalConflictsAndTrashRoundTripOperationsAndOldArchivesRemainReadable()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var created = await store.CreateAsync(LocalJournalFixture.Draft with { HasChamfer = true }, Token);
        var original = (await store.ReadRecordAsync(created.Id, Token))!;
        Assert.True(original.ToSummary().HasChamfer);
        var changed = await store.UpdateAsync(new(original, original with { Operations = null }), Token);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(original, original with { Customer = "Другой" }), Token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        var restoredFlag = await store.UpdateAsync(new(changed, changed with { Operations = original.Operations }), Token);
        var trash = new OrderTrashService(store, new("local", fixture.Path), new JsonTrashStore(fixture.Directory + "/operations-trash"));
        var entry = await trash.DeleteAsync(restoredFlag, Token);
        Assert.Equal(2, entry.Version); entry.Validate();
        await trash.RestoreAsync(entry.OperationId, Token);
        Assert.Equal(restoredFlag, await new LocalJournalStore(fixture.Path).ReadRecordAsync(created.Id, Token));
        var old = new TrashEntry(1, Guid.NewGuid(), new("local", fixture.Path), changed, DateTimeOffset.UtcNow, TrashState.Deleted);
        old.Validate(); Assert.Null(JsonSerializer.Deserialize<TrashEntry>(JsonSerializer.Serialize(old))!.Record.Operations);
        Assert.Throws<InvalidDataException>(() => (entry with { Version = 1 }).Validate());
    }

    [Fact]
    public async Task OnlyExplicitReadsPrepareOperationsAndFailureCanBeRetriedWithoutHidingRows()
    {
        var store = new DemoOrderStore(); var initializer = new Initializer { Fail = true };
        var reader = new OrderDurationPreparingReader(store, initializer, operations: initializer);
        Assert.NotEmpty(await reader.ReadAsync(Token)); Assert.Equal(1, initializer.Operations);
        Assert.NotNull(reader.PreparationWarning);
        await reader.ReadSnapshotAsync(null, Token); Assert.Equal(1, initializer.Operations);
        initializer.Fail = false;
        await reader.ReadAsync(Token); Assert.Equal(2, initializer.Operations); Assert.Null(reader.PreparationWarning);
        await reader.ReadAsync(Token); Assert.Equal(2, initializer.Operations);
        Assert.Equal(1, initializer.Time);
    }
    private sealed class Initializer : IOrderDurationInitializer, IOrderOperationsInitializer
    {
        public bool Fail; public int Operations, Time;
        public Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default) { Time++; return Task.CompletedTask; }
        public Task PrepareOperationsAsync(CancellationToken cancellationToken = default)
        { Operations++; return Fail ? Task.FromException(new OrderWriteException(OrderWriteFailure.NotSaved, "База занята")) : Task.CompletedTask; }
    }

    [Fact]
    public void CsvAndRecordsRejectUnknownOperationsInsteadOfDiscardingThem()
    {
        var row = OrderMaintenanceTests.Record().ToSummary(); var operation = OrderOperations.For(row, true)!;
        var csv = "OrderId,OperationCode,IdentityHash,FormatVersion\n42,Chamfer," + operation.IdentityHash + ",1\n";
        Assert.Equal(operation, OrderOperationsData.ReadExport(new StringReader(csv), Token)[42]);
        Assert.Throws<OrderReadException>(() => OrderOperationsData.ReadExport(new StringReader(csv.Replace("Chamfer", "FutureOperation")), Token));
        Assert.Throws<OrderReadException>(() => OrderOperationsData.ReadExport(new StringReader(csv + csv.Split('\n')[1] + "\n"), Token));
        Assert.Throws<OrderWriteException>(() => (operation with { Kinds = (OrderOperation)2 }).Validate());
        Assert.False((row with { Operations = operation, SpecificationNumber = "Новый" }).HasChamfer);
        Assert.True((row with { Operations = operation, CuttingType = "Другой станок" }).HasChamfer);
    }

    [Fact]
    public async Task SchemaPreparationIsIdempotentForSequentialAndRacingClients()
    {
        var ready = false; var creates = 0; var delays = 0;
        Task Delay(TimeSpan time, CancellationToken token) { delays++; return Task.CompletedTask; }
        bool Busy(Exception error) => error is IOException;
        void Create() { creates++; ready = true; }
        await SharedSchemaPreparation.EnsureAsync(() => ready, Create, Busy, Token, Delay);
        await SharedSchemaPreparation.EnsureAsync(() => ready, () => throw new Exception("Second client must not create"), Busy, Token, Delay);
        Assert.Equal(1, creates); Assert.Equal(0, delays);
        ready = false;
        await SharedSchemaPreparation.EnsureAsync(() => ready, () => { ready = true; throw new IOException("Other client finished"); }, Busy, Token, Delay);
        // После блокировки готовность другого клиента проверяется после паузы.
        Assert.Equal(1, delays);
    }

    [Fact]
    public async Task SchemaPreparationRetriesOnlyContentionAndStopsWithoutDiscardingUnknownData()
    {
        var attempts = 0; var delays = 0; var ready = false;
        bool Busy(Exception error) => error is IOException;
        Task Delay(TimeSpan time, CancellationToken token) { delays++; return Task.CompletedTask; }
        await SharedSchemaPreparation.EnsureAsync(() => ready, () => { if (++attempts < 3) throw new IOException(); ready = true; }, Busy, Token, Delay);
        Assert.Equal(3, attempts); Assert.Equal(2, delays);
        attempts = delays = 0;
        await Assert.ThrowsAsync<IOException>(() => SharedSchemaPreparation.EnsureAsync(() => false, () => { attempts++; throw new IOException(); }, Busy, Token, Delay));
        Assert.Equal(6, attempts); Assert.Equal(5, delays);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => SharedSchemaPreparation.EnsureAsync(() => false, () => throw new UnauthorizedAccessException(), Busy, Token, Delay));
        Assert.Equal(5, delays);
        await Assert.ThrowsAsync<InvalidDataException>(() => SharedSchemaPreparation.EnsureAsync(() => throw new InvalidDataException(), () => throw new Exception("Must not replace"), Busy, Token, Delay));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SharedSchemaPreparation.EnsureAsync(() => false, () => throw new Exception("Must not create"), Busy, cancellation.Token, Delay));
    }
}
