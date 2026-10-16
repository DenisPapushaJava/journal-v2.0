using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task EditingPreservesEveryUnchangedRawFieldAndTheLegacySchema()
    {
        using var database = Database.Create();
        var store = new OleDbOrderMaintenance(database.Path); var schema = database.Schema();
        var raw = (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
        var other = await store.ReadRecordAsync(43, TestContext.Current.CancellationToken);
        Assert.Null(other!.Date); Assert.Null(other.Manager); Assert.Null(other.Price); Assert.Null(other.Coefficient);
        Assert.Equal(" 1,25 | 20 | 340 | 2 | N2 ", raw.Coefficient);
        var saved = await store.UpdateAsync(new(raw, raw with { Customer = "Новый 'А', %_*? []" }), TestContext.Current.CancellationToken);
        Assert.Equal(raw with { Customer = "Новый 'А', %_*? []", Price = 659m }, saved);
        Assert.Equal(saved, await new OleDbOrderMaintenance(database.Path).ReadRecordAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal(other, await store.ReadRecordAsync(43, TestContext.Current.CancellationToken));
        Assert.Equal(schema, database.Schema());
        Assert.Equal(saved.ToSummary(), (await database.Read()).Single(row => row.Id == 42));
        Assert.Equal(17, database.Scalar("SELECT [2,5] FROM [PriceLL500]"));
    }

    [Fact]
    public async Task DeleteAndRestorePreserveNullEmptyCurrencyTimeAndAutonumberContinuation()
    {
        using var database = Database.Create(); var schema = database.Schema();
        var store = new OleDbOrderMaintenance(database.Path);
        var originals = new[] { (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!, (await store.ReadRecordAsync(43, TestContext.Current.CancellationToken))! };
        foreach (var original in originals)
        {
            await store.DeleteAsync(original, TestContext.Current.CancellationToken);
            Assert.Null(await store.ReadRecordAsync(original.Id, TestContext.Current.CancellationToken));
            Assert.Equal(original, await store.RestoreAsync(original, TestContext.Current.CancellationToken));
            Assert.Equal(original, await new OleDbOrderMaintenance(database.Path).ReadRecordAsync(original.Id, TestContext.Current.CancellationToken));
        }
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft(), TestContext.Current.CancellationToken);
        Assert.True(created.Id > originals.Max(row => row.Id));
        Assert.Equal(originals[0], await store.ReadRecordAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal(schema, database.Schema());
    }

    [Fact]
    public async Task FullSnapshotChecksCaseTrailingSpacesNullAndConcurrentStatusChanges()
    {
        using var database = Database.Create(); var store = new OleDbOrderMaintenance(database.Path);
        var original = (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
        database.Execute("UPDATE [Orders] SET [Менеджер]='менеджер' WHERE [Код]=42");
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.DeleteAsync(original, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(original, original with { Customer = "Правка" }), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Equal("менеджер", (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!.Manager);
        original = (await store.ReadRecordAsync(43, TestContext.Current.CancellationToken))!;
        database.Execute("UPDATE [Orders] SET [Color]=0 WHERE [Код]=43");
        conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.DeleteAsync(original, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.NotNull(await store.ReadRecordAsync(43, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChangedPriceAndUniqueConstraintRollbackWithoutReplacingHistoricalValues()
    {
        using var database = Database.Create(); var store = new OleDbOrderMaintenance(database.Path);
        var raw = (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
        var change = new OrderEditChange(raw, raw with { Customer = "Изменено" });
        var quote = await new CuttingPriceService(store).CalculateAsync(change.PriceRequest, TestContext.Current.CancellationToken);
        database.Execute("UPDATE [PriceLL500] SET [2,5]=1");
        var changed = await Assert.ThrowsAsync<OrderEditPriceChangedException>(() => store.UpdateAsync(change with { ExpectedQuote = quote }, TestContext.Current.CancellationToken));
        Assert.Equal(39, changed.Quote.StoredPrice);
        Assert.Equal(raw, await store.ReadRecordAsync(42, TestContext.Current.CancellationToken));
        database.Execute("CREATE UNIQUE INDEX [UniqueSpec] ON [Orders] ([Спецификация])");
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(raw, raw with { Specification = "без даты" }), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(raw, await store.ReadRecordAsync(42, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestoreNeverOverwritesAnOccupiedKeyAndUnsupportedSchemaIsNotChanged()
    {
        using var database = Database.Create(); var store = new OleDbOrderMaintenance(database.Path);
        var raw = (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => store.RestoreAsync(raw with { Customer = "Другое" }, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, failure.Failure);
        Assert.Equal(raw, await store.ReadRecordAsync(42, TestContext.Current.CancellationToken));
        database.Execute("ALTER TABLE [Orders] ADD COLUMN [Extra] TEXT(10)");
        var schema = database.Schema();
        failure = await Assert.ThrowsAsync<OrderWriteException>(() => store.DeleteAsync(raw, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(schema, database.Schema()); Assert.Equal(2, database.Scalar("SELECT COUNT(*) FROM [Orders]"));
    }

    [Fact]
    public async Task CascadingRelationsAreRejectedWithoutDeletingDependentRows()
    {
        using var database = Database.Create(); var store = new OleDbOrderMaintenance(database.Path);
        var row = (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
        database.Execute("CREATE TABLE [Child] ([OrderId] LONG, CONSTRAINT [ChildOrder] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Код]))");
        database.Execute("INSERT INTO [Child] ([OrderId]) VALUES (42)");
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => store.DeleteAsync(row, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(row, await store.ReadRecordAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal(1, database.Scalar("SELECT COUNT(*) FROM [Child]"));
    }

    [Fact]
    public async Task CancelledMaintenanceNeverStartsAWrite()
    {
        using var database = Database.Create(); var store = new OleDbOrderMaintenance(database.Path);
        var row = (await store.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
        var bytes = SHA256.HashData(File.ReadAllBytes(database.Path));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DeleteAsync(row, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RestoreAsync(row, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateAsync(new(row, row with { Customer = "Новая" }), cancellation.Token));
        Assert.Equal(bytes, SHA256.HashData(File.ReadAllBytes(database.Path)));
    }
}
