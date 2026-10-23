using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task OperationsPreparationAllowsSequentialClientsWithoutChangingLegacyAnnotationsOrOrders()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        await new OleDbOrderAnnotations(database.Path).PrepareAnnotationsAsync(token);
        var rows = await database.Read(); var schema = database.Schema();
        var bytes = SHA256.HashData(File.ReadAllBytes(database.Path));
        await new OleDbOrderOperations(database.Path).PrepareOperationsAsync(token);
        await new OleDbOrderOperations(database.Path).PrepareOperationsAsync(token);
        Assert.Equal(rows, await database.Read()); Assert.Equal(schema, database.Schema());
        Assert.Equal(17, database.Scalar("SELECT [2,5] FROM [PriceLL500]"));
        using (var connection = database.Connection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM [JournalOrderAnnotations] WHERE 1=0";
            using var reader = command.ExecuteReader()!; Assert.Equal(5, reader.FieldCount);
        }
        var backup = Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-journal-operations-*.bak"));
        Assert.Equal(bytes, SHA256.HashData(File.ReadAllBytes(backup)));
    }

    [Fact]
    public async Task ConcurrentOperationsPreparationAndTransientFileLockAreSafe()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var holder = new FileStream(database.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var first = new OleDbOrderOperations(database.Path).PrepareOperationsAsync(token);
            var second = new OleDbOrderOperations(database.Path).PrepareOperationsAsync(token);
            await Task.Delay(150, token); holder.Dispose();
            await Task.WhenAll(first, second);
            Assert.All(await database.Read(), row => Assert.False(row.OperationsUnavailable));
            Assert.Equal(0, database.Scalar("SELECT Count(*) FROM [JournalOrderOperations]"));
        }
        finally { holder.Dispose(); }
    }

    [Fact]
    public async Task ChamferWritesAreAtomicConflictProtectedAndKeepTheLegacySchema()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken; var schema = database.Schema();
        var draft = CreationDraft() with { HasChamfer = true, HasBending = true, Comment = "Сохранить" };
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(draft, token);
        Assert.True(created.HasChamfer); Assert.True(created.HasBending); Assert.Equal(draft.Comment, created.Comment);
        var store = new OleDbOrderMaintenance(database.Path); var original = (await store.ReadRecordAsync(created.Id, token))!;
        var saved = await store.UpdateAsync(new(original, original with { Operations = null }), token);
        Assert.False(saved.ToSummary().HasChamfer); Assert.Equal(original.Annotations, saved.Annotations); Assert.Equal(original.Price, saved.Price);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(original, original with { Customer = "Устаревший снимок" }), token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        var restoredFlag = await store.UpdateAsync(new(saved, saved with { Operations = original.Operations }), token);
        await store.DeleteAsync(restoredFlag, token);
        Assert.Equal(0, database.Scalar($"SELECT Count(*) FROM [JournalOrderOperations] WHERE [OrderId]={created.Id}"));
        Assert.Equal(restoredFlag, await store.RestoreAsync(restoredFlag, token));
        // SQL прежних приложений не требует нового столбца в Orders/Annotations.
        database.Execute($"UPDATE [Orders] SET [Менеджер]='Legacy' WHERE [Код]={created.Id}");
        Assert.True((await database.Read()).Single(row => row.Id == created.Id).HasChamfer);
        database.Execute($"UPDATE [Orders] SET [Спецификация]='Legacy-new' WHERE [Код]={created.Id}");
        Assert.True((await database.Read()).Single(row => row.Id == created.Id).OperationsNeedReview);
        Assert.Equal(schema, database.Schema());
    }

    [Fact]
    public async Task UnknownOperationsSchemaIsNeverReplacedAndDoesNotHideTheJournal()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var count = (await database.Read()).Count;
        database.Execute("CREATE TABLE [JournalOrderOperations] ([OtherData] TEXT(100))");
        database.Execute("INSERT INTO [JournalOrderOperations] ([OtherData]) VALUES ('keep')");
        await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderOperations(database.Path).PrepareOperationsAsync(token));
        Assert.Equal("keep", database.Scalar("SELECT [OtherData] FROM [JournalOrderOperations]"));
        var rows = await database.Read(); Assert.Equal(count, rows.Count); Assert.All(rows, row => Assert.True(row.OperationsUnavailable));
        await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { HasChamfer = true }, token));
        Assert.Equal(count, database.Scalar("SELECT Count(*) FROM [Orders]"));
    }

    [Fact]
    public async Task LegacyDeleteLeavesOperationsWithoutAllowingRestoreToOverwriteTheOrphan()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { HasChamfer = true }, token);
        var store = new OleDbOrderMaintenance(database.Path); var archived = (await store.ReadRecordAsync(created.Id, token))!;
        database.Execute($"DELETE FROM [Orders] WHERE [Код]={created.Id}");
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.RestoreAsync(archived, token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure); Assert.Null(await store.ReadRecordAsync(created.Id, token));
        Assert.Equal(1, database.Scalar($"SELECT Count(*) FROM [JournalOrderOperations] WHERE [OrderId]={created.Id}"));
    }

    [Fact]
    public async Task BatchOperationsAreCheckedPerRowAndRollbackWithTheWholeBatch()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var creator = new OleDbManualOrderCreator(database.Path);
        var first = CreationDraft() with { SpecificationNumber = "operations-batch-1", HasChamfer = true };
        var second = CreationDraft() with { SpecificationNumber = "operations-batch-2", HasChamfer = false };
        var created = await creator.CreateBatchAsync([first, second], token);
        Assert.True(created[0].HasChamfer); Assert.False(created[1].HasChamfer);
        var before = await database.Read();
        Assert.Equal(1, database.Scalar("SELECT Count(*) FROM [JournalOrderOperations]"));
        database.Execute("CREATE UNIQUE INDEX [UX_operations_batch_spec] ON [Orders] ([Спецификация])");
        var failed = await Assert.ThrowsAsync<OrderWriteException>(() => creator.CreateBatchAsync(
            [first with { SpecificationNumber = "operations-must-rollback" }, second with { SpecificationNumber = first.SpecificationNumber }], token));
        Assert.Equal(OrderWriteFailure.NotSaved, failed.Failure);
        Assert.Equal(before, await database.Read());
        Assert.Equal(1, database.Scalar("SELECT Count(*) FROM [JournalOrderOperations]"));
    }

    [Fact]
    public async Task ANewOperationRevalidatesTheSchemaInsteadOfReusingAnEarlierConnection()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { HasChamfer = true }, token);
        var store = new OleDbOrderMaintenance(database.Path);
        var original = (await store.ReadRecordAsync(created.Id, token))!;
        database.Execute("ALTER TABLE [JournalOrderOperations] ADD COLUMN [UnknownData] TEXT(20)");
        await Assert.ThrowsAsync<OrderReadException>(() => store.ReadRecordAsync(created.Id, token));
        await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(original, original with { Customer = "Не сохранять" }), token));
        Assert.Equal(original.Customer, database.Scalar($"SELECT [Заказчик] FROM [Orders] WHERE [Код]={created.Id}"));
        Assert.Equal(1, database.Scalar("SELECT Count(*) FROM [JournalOrderOperations]"));
    }
}
