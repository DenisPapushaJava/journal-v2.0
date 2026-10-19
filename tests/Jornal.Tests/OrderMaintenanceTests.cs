using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Local;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderMaintenanceTests
{
    // Необычный старый текст намеренный: чтение, архив и восстановление не должны исправлять исходные поля.
    // В примерах обычного ввода/скриншотах используется числовое общее количество.
    public static OrderRecord Record() => new(42, new(2026, 1, 2, 8, 31, 42), null, "0102--42", "Клиент 'А'", "Лазер", "5083H111",
        "1500x6000", "02 / комплект", 1234.5678m, 2.5, 31, "0012.350", "", -16711936, " 1,25 | 20 | 340 | 2 | N2 ");

    [Fact]
    public async Task OptionalRealLocalCopyKeepsAllOtherRowsAndExactlyRestoresTheChosenRecord()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_TEST_LOCAL_COPY");
        Assert.SkipUnless(OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(path), "Локальная тестовая копия Linux не задана.");
        var token = TestContext.Current.CancellationToken;
        var beforeHash = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path!, token));
        var root = Path.Combine(Path.GetTempPath(), "journal-maintenance-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var copy = Path.Combine(root, "disposable.journal-test.json"); File.Copy(path!, copy);
            var store = new LocalJournalStore(copy);
            var baseline = await store.ReadAsync(token);
            OrderRecord? original = null;
            foreach (var summary in baseline)
            {
                if (summary.ThicknessMillimeters is null || summary.CutLengthMeters is null || summary.Coefficient is null) continue;
                var candidate = (await store.ReadRecordAsync(summary.Id, token))!;
                try
                {
                    var change = new OrderEditChange(candidate, candidate with { Customer = "Проверка редактирования" });
                    change.Validate(); await new CuttingPriceService(store).CalculateAsync(change.PriceRequest, token);
                    original = candidate; break;
                }
                catch (PriceCalculationException) { }
            }
            Assert.NotNull(original);
            var saved = await store.UpdateAsync(new(original, original with { Customer = "Проверка редактирования" }), token);
            Assert.Equal(original with { Customer = saved.Customer, Price = saved.Price }, saved);
            var service = new OrderTrashService(store, new("local", copy), new JsonTrashStore(Path.Combine(root, "Trash")));
            var archived = await service.DeleteAsync(saved, token);
            Assert.Null(await store.ReadRecordAsync(saved.Id, token));
            Assert.Equal(saved, await service.RestoreAsync(archived.OperationId, token));
            var after = await new LocalJournalStore(copy).ReadAsync(token);
            Assert.Equal(baseline.Count, after.Count);
            Assert.Equal(baseline.Where(row => row.Id != original.Id), after.Where(row => row.Id != original.Id));
            await store.DeleteAsync(saved, token); await store.RestoreAsync(original, token);
            Assert.Equal(baseline, await new LocalJournalStore(copy).ReadAsync(token));
            Assert.Equal(beforeHash, System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path!, token)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SnapshotRoundTripPreservesAllTypesAndNullVersusEmpty()
    {
        var record = Record() with { Thickness = double.BitIncrement(2.5), Price = 922337203685477.5807m };
        var copy = JsonSerializer.Deserialize<OrderRecord>(JsonSerializer.Serialize(record))!;
        Assert.Equal(record, copy); Assert.Null(copy.Manager); Assert.Equal("", copy.DeliveryNumber);
        Assert.Equal(BitConverter.DoubleToInt64Bits(record.Thickness!.Value), BitConverter.DoubleToInt64Bits(copy.Thickness!.Value));
        Assert.Equal(new TimeSpan(8, 31, 42), copy.Date!.Value.TimeOfDay);
        Assert.Equal(20, copy.ToSummary().PartCount); Assert.Equal(340, copy.ToSummary().PunchCount);
    }

    [Fact]
    public async Task DurableArchivePrecedesDeleteAndRestoreReturnsTheOriginalPriceAndId()
    {
        using var fixture = new ArchiveFixture();
        var tracking = new FaultStore(fixture.Store);
        fixture.Backend.BeforeDelete = () => Assert.Equal(TrashState.DeletePending, tracking.LastSaved!.State);
        var service = fixture.Service(tracking);
        var entry = await service.DeleteAsync(fixture.Original, TestContext.Current.CancellationToken);
        Assert.Null(fixture.Backend.Current); Assert.Equal(TrashState.Deleted, entry.State);
        Assert.Equal(fixture.Original, Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken)).Record);
        var restored = await fixture.Service().RestoreAsync(entry.OperationId, TestContext.Current.CancellationToken);
        Assert.Equal(fixture.Original, restored); Assert.Equal(1234.5678m, restored.Price);
        Assert.Empty(await fixture.Service().RefreshAsync(true, TestContext.Current.CancellationToken)); Assert.Equal(1, fixture.Backend.Restores);
        if (OperatingSystem.IsLinux())
        {
            var file = Directory.GetFiles(fixture.Root, "*.journal-trash.json", SearchOption.AllDirectories).Single();
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [Fact]
    public async Task ArchiveWriteFailureNeverCallsTheDatabaseDelete()
    {
        using var fixture = new ArchiveFixture();
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service(new FaultStore(fixture.Store) { FailSave = 1 }).DeleteAsync(fixture.Original, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, error.Failure); Assert.Equal(0, fixture.Backend.Deletes);
        Assert.Equal(fixture.Original, fixture.Backend.Current);
    }

    [Fact]
    public async Task FailureAfterCommittedDeleteKeepsPendingCopyAndReconcilesAfterRestart()
    {
        using var fixture = new ArchiveFixture();
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service(new FaultStore(fixture.Store) { FailSave = 2 }).DeleteAsync(fixture.Original, TestContext.Current.CancellationToken));
        Assert.True(error.RequiresReload); Assert.Null(fixture.Backend.Current);
        var pending = Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken));
        Assert.Equal(TrashState.DeletePending, pending.State);
        fixture.Clock.Now += TimeSpan.FromDays(45);
        var checkedEntry = Assert.Single(await fixture.Service().RefreshAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(TrashState.Deleted, checkedEntry.State); Assert.Equal(fixture.Original, checkedEntry.Record);
        Assert.Equal(fixture.Clock.Now, checkedEntry.ConfirmedAt); // новое окно восстановления после неопределённости.
    }

    [Fact]
    public async Task UnknownRestoreCommitIsReconciledWithoutASecondInsert()
    {
        using var fixture = new ArchiveFixture();
        var entry = await fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken);
        fixture.Backend.UnknownRestore = true;
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().RestoreAsync(entry.OperationId, TestContext.Current.CancellationToken));
        Assert.True(error.RequiresReload); Assert.Equal(fixture.Original, fixture.Backend.Current);
        Assert.Equal(TrashState.RestorePending, Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken)).State);
        Assert.Empty(await fixture.Service().RefreshAsync(true, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().RestoreAsync(entry.OperationId, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Backend.Restores);
    }

    [Fact]
    public async Task UnknownDeleteWithAnOccupiedDifferentRowCannotBePurgedOrRestoredAfterThirtyDays()
    {
        using var fixture = new ArchiveFixture(); fixture.Backend.UnknownDelete = true;
        await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken));
        fixture.Backend.Current = fixture.Original with { Customer = "Другой клиент" };
        fixture.Clock.Now += TimeSpan.FromDays(90);
        var entry = Assert.Single(await fixture.Service().RefreshAsync(true, TestContext.Current.CancellationToken));
        Assert.Equal(TrashState.DeletePending, entry.State);
        await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().PurgeAsync(entry.OperationId, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().RestoreAsync(entry.OperationId, TestContext.Current.CancellationToken));
        Assert.Equal("Другой клиент", fixture.Backend.Current.Customer);
    }

    [Fact]
    public async Task ConfirmedDeletionExpiresButDoesNotChangeTheDatabaseDuringCleanup()
    {
        using var fixture = new ArchiveFixture();
        await fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken);
        fixture.Clock.Now += TimeSpan.FromDays(29); Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken));
        fixture.Clock.Now += TimeSpan.FromDays(1); Assert.Empty(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Backend.Deletes); Assert.Equal(0, fixture.Backend.Restores);
    }

    [Fact]
    public async Task BinsAreSeparatedBySourceAndCannotRestoreIntoAnotherDatabase()
    {
        using var fixture = new ArchiveFixture();
        var entry = await fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken);
        var other = new OrderTrashService(fixture.Backend, new("mdb", Path.Combine(fixture.Root, "other.mdb")), fixture.Store);
        Assert.Empty(await other.RefreshAsync(false, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderWriteException>(() => other.RestoreAsync(entry.OperationId, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Backend.Restores);
    }

    [Fact]
    public async Task ExistingIdAndConflictingDeleteNeverOverwriteRecords()
    {
        using var fixture = new ArchiveFixture();
        fixture.Backend.Current = fixture.Original with { DeliveryNumber = "Изменено" };
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, error.Failure); Assert.Empty(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken));
        fixture.Backend.Current = fixture.Original;
        var entry = await fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken);
        fixture.Backend.Current = fixture.Original with { Price = 999 };
        error = await Assert.ThrowsAsync<OrderWriteException>(() => fixture.Service().RestoreAsync(entry.OperationId, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, error.Failure); Assert.Equal(999, fixture.Backend.Current.Price);
        Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptedArchiveStopsCleanupAndFurtherDeletion()
    {
        using var fixture = new ArchiveFixture();
        await fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken);
        var path = Directory.GetFiles(fixture.Root, "*.journal-trash.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(path, "{ damaged archive", TestContext.Current.CancellationToken);
        fixture.Backend.Current = fixture.Original;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service().DeleteAsync(fixture.Original, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.Backend.Deletes); Assert.Equal(fixture.Original, fixture.Backend.Current);
    }

    [Fact]
    public async Task LocalCopySupportsEditingRestoringNullsAndMonotonicIdsAcrossReopen()
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-maintenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "OrderDB.journal-test.json");
            var prices = PriceTables.All.Select(name => new PriceTable(name, true, [new(2.5m, "2,5", 17)])).ToArray();
            await LocalJournalStore.InitializeAsync(path, [], prices, "synthetic.mdb", new string('a', 64), TestContext.Current.CancellationToken);
            var store = new LocalJournalStore(path); var original = Record();
            await store.RestoreAsync(original, TestContext.Current.CancellationToken);
            Assert.Equal(original, await new LocalJournalStore(path).ReadRecordAsync(original.Id, TestContext.Current.CancellationToken));
            var updated = await store.UpdateAsync(new(original, original with { Customer = "Исправлено 'А'" }), TestContext.Current.CancellationToken);
            Assert.Equal(original with { Customer = "Исправлено 'А'" }, updated);
            await store.SetColorAsync([updated.ToSummary()], OrderColors.Ready, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<OrderWriteException>(() => store.DeleteAsync(updated, TestContext.Current.CancellationToken));
            updated = (await store.ReadRecordAsync(original.Id, TestContext.Current.CancellationToken))!;
            await store.DeleteAsync(updated, TestContext.Current.CancellationToken);
            Assert.Empty(await store.ReadAsync(TestContext.Current.CancellationToken));
            await new LocalJournalStore(path).RestoreAsync(updated, TestContext.Current.CancellationToken);
            Assert.Equal(updated, await store.ReadRecordAsync(updated.Id, TestContext.Current.CancellationToken));
            var draft = new ManualOrderDraft(new(2026, 1, 2), "42", "Имя", "Клиент", "Лазер", "Амг3", 2.5m,
                "1500x3000", "1", 25, "12", 1, "", "");
            Assert.True((await store.CreateAsync(draft, TestContext.Current.CancellationToken)).Id > updated.Id);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task BatchStopsWhenTheNextArchiveCannotBeSavedAndKeepsTheCompletedCopy()
    {
        using var fixture = new ArchiveFixture();
        var records = new[] { fixture.Original, fixture.Original with { Id = 43 }, fixture.Original with { Id = 44 } };
        var service = fixture.Service(new FaultStore(fixture.Store) { FailSave = 3 });
        var result = await service.DeleteManyAsync(records, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsComplete);
        Assert.Equal(new[] { TrashDeleteOutcome.Deleted, TrashDeleteOutcome.Failed, TrashDeleteOutcome.NotAttempted }, result.Items.Select(item => item.Outcome));
        Assert.Equal(OrderWriteFailure.NotSaved, Assert.IsType<OrderWriteException>(result.Error).Failure);
        Assert.Equal(1, fixture.Backend.Deletes);
        Assert.Equal(records[0], Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken)).Record);
        await fixture.Service().RestoreAsync(Assert.Single(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken)).OperationId, TestContext.Current.CancellationToken);
        Assert.Equal(records[0], fixture.Backend.Current);
    }

    [Fact]
    public async Task BatchRejectsDuplicateIdsBeforeSavingAnArchiveOrDeletingAnything()
    {
        using var fixture = new ArchiveFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service().DeleteManyAsync([fixture.Original, fixture.Original], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(await fixture.Service().RefreshAsync(false, TestContext.Current.CancellationToken)); Assert.Equal(0, fixture.Backend.Deletes);
    }

    public sealed class Backend : IOrderMaintenance
    {
        public OrderRecord? Current = Record(); public int Deletes; public int Restores; public int Updates;
        public bool UnknownDelete; public bool UnknownRestore; public Action? BeforeDelete;
        public int Rate = 17;
        public Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Current?.Id == id ? Current : null);
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(new PriceTable(name, true, [new(2.5m, "2,5", Rate)]));
        public async Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken cancellationToken = default)
        {
            if (Current != change.Expected) throw new OrderWriteException(OrderWriteFailure.Conflict, "Конфликт");
            Current = change.Calculate(await ReadAsync(CuttingPriceCalculator.SelectTable(change.PriceRequest), cancellationToken)); Updates++; return Current;
        }
        public Task DeleteAsync(OrderRecord expected, CancellationToken cancellationToken = default)
        {
            BeforeDelete?.Invoke(); Deletes++;
            if (Current != expected) throw new OrderWriteException(OrderWriteFailure.Conflict, "Конфликт");
            Current = null;
            if (UnknownDelete) throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Обрыв после DELETE");
            return Task.CompletedTask;
        }
        public Task<OrderRecord> RestoreAsync(OrderRecord record, CancellationToken cancellationToken = default)
        {
            if (Current is not null) throw new OrderWriteException(OrderWriteFailure.Conflict, "Код занят");
            Current = record; Restores++;
            if (UnknownRestore) throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Обрыв после INSERT");
            return Task.FromResult(record);
        }
    }

    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class ArchiveFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "journal-trash-" + Guid.NewGuid().ToString("N"));
        public OrderRecord Original { get; } = Record(); public Backend Backend { get; } = new(); public Clock Clock { get; } = new();
        public ITrashStore Store { get; }
        public ArchiveFixture() { Directory.CreateDirectory(Root); Store = new JsonTrashStore(Root); }
        public OrderTrashService Service(ITrashStore? store = null) => new(Backend, new("mdb", Path.Combine(Root, "OrderDB.mdb")), store ?? Store, Clock);
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class FaultStore(ITrashStore inner) : ITrashStore
    {
        public int FailSave; public TrashEntry? LastSaved; private int _saves;
        public async Task<ITrashLease> OpenAsync(TrashSource source, CancellationToken cancellationToken = default) => new Lease(this, await inner.OpenAsync(source, cancellationToken));
        private sealed class Lease(FaultStore owner, ITrashLease innerLease) : ITrashLease
        {
            public Task<IReadOnlyList<TrashEntry>> ReadAsync() => innerLease.ReadAsync();
            public async Task SaveAsync(TrashEntry entry)
            { if (++owner._saves == owner.FailSave) throw new IOException("Диск недоступен"); await innerLease.SaveAsync(entry); owner.LastSaved = entry; }
            public Task RemoveAsync(Guid id) => innerLease.RemoveAsync(id);
            public void Dispose() => innerLease.Dispose();
        }
    }
}
