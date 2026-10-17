using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task AnnotationPreparationIsIdempotentAndPreservesOrdersPricesAndFourColumnTimeTable()
    {
        using var database = Database.Create();
        await new OleDbOrderExtras(database.Path).PrepareCuttingTimeAsync(TestContext.Current.CancellationToken);
        var rows = await database.Read(); var schema = database.Schema();
        var before = SHA256.HashData(File.ReadAllBytes(database.Path));
        var initializer = new OleDbOrderAnnotations(database.Path);
        await initializer.PrepareAnnotationsAsync(TestContext.Current.CancellationToken);
        await initializer.PrepareAnnotationsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(rows, await database.Read()); Assert.Equal(schema, database.Schema());
        Assert.Equal(17, database.Scalar("SELECT [2,5] FROM [PriceLL500]"));
        using var connection = database.Connection();
        using var query = connection.CreateCommand(); query.CommandText = "SELECT * FROM [JournalOrderExtras] WHERE 1=0";
        using var reader = query.ExecuteReader()!; Assert.Equal(4, reader.FieldCount);
        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(database.Path)!, "*.before-journal-annotations-*.bak"));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(backup)));
    }

    [Fact]
    public async Task BusyAnnotationPreparationKeepsListAvailableAndRetriesWithoutChangingOrders()
    {
        using var database = Database.Create(); var schema = database.Schema();
        var rows = await database.Read();
        using (var occupied = database.Connection())
        {
            var error = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderAnnotations(database.Path).PrepareAnnotationsAsync(TestContext.Current.CancellationToken));
            Assert.Equal(OrderWriteFailure.NotSaved, error.Failure);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(database.Path)!, "*.before-journal-annotations-*.bak"));
            Assert.Equal(rows, await database.Read()); Assert.Equal(schema, database.Schema());
        }
        await new OleDbOrderAnnotations(database.Path).PrepareAnnotationsAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "NULL")]
    [InlineData(true, "Гнуть 'А'\r\nЧертёж №2")]
    public async Task AnnotationsRoundTripInTheSameTransactionAndLegacySqlStillWorks(bool bending, string comment)
    {
        using var database = Database.Create(); var schema = database.Schema();
        var draft = CreationDraft() with { HasBending = bending, Comment = comment, CuttingDurationMinutes = 1786 };
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(draft, TestContext.Current.CancellationToken);
        Assert.Equal(bending, created.HasBending); Assert.Equal(comment, created.Comment);
        Assert.Equal(draft.StoredCoefficient, created.StoredCoefficient);
        Assert.Equal(created, (await database.Read()).Single(row => row.Id == created.Id));
        var store = new OleDbOrderMaintenance(database.Path);
        var original = (await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
        var edited = original with { Annotations = OrderAnnotations.For(original.ToSummary(), !bending, new string('я', 255)) };
        var saved = await store.UpdateAsync(new(original, edited), TestContext.Current.CancellationToken);
        Assert.Equal(edited.Annotations, saved.Annotations);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(original, original with { Customer = "Old snapshot" }), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Equal(saved, await store.ReadRecordAsync(saved.Id, TestContext.Current.CancellationToken));
        await store.DeleteAsync(saved, TestContext.Current.CancellationToken);
        Assert.Equal(0, Convert.ToInt32(database.Scalar($"SELECT Count(*) FROM [JournalOrderAnnotations] WHERE [OrderId]={saved.Id}")));
        Assert.Equal(saved, await store.RestoreAsync(saved, TestContext.Current.CancellationToken));
        // Запросы старого EXE не требуют новых полей и не ограничены FK.
        database.Execute($"UPDATE [Orders] SET [Менеджер]='Legacy', [Ход]=[Ход]+1 WHERE [Код]={saved.Id}");
        var legacy = (await database.Read()).Single(row => row.Id == saved.Id);
        Assert.Equal(saved.Annotations, legacy.Annotations); Assert.Equal(saved.ToSummary().HasBending, legacy.HasBending);
        database.Execute($"UPDATE [Orders] SET [Спецификация]='другой' WHERE [Код]={saved.Id}");
        Assert.True((await database.Read()).Single(row => row.Id == saved.Id).AnnotationsNeedReview);
        database.Execute($"DELETE FROM [Orders] WHERE [Код]={saved.Id}");
        Assert.DoesNotContain(await database.Read(), row => row.Id == saved.Id);
        Assert.Equal(schema, database.Schema());
    }

    [Fact]
    public async Task ClearingTheFlagAndCommentRemovesOnlyAnnotationsAndKeepsLegacyFieldsAndTime()
    {
        using var database = Database.Create();
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { HasBending = true, CuttingDurationMinutes = 20 }, TestContext.Current.CancellationToken);
        var store = new OleDbOrderMaintenance(database.Path);
        var original = (await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
        var saved = await store.UpdateAsync(new(original, original with { Annotations = null }), TestContext.Current.CancellationToken);
        Assert.Equal(original with { Annotations = null }, saved);
        Assert.Equal(0, Convert.ToInt32(database.Scalar($"SELECT Count(*) FROM [JournalOrderAnnotations] WHERE [OrderId]={saved.Id}")));
        Assert.Equal(20L, saved.ToSummary().CuttingDurationMinutes);
    }

    [Fact]
    public async Task UnknownAnnotationsSchemaIsNotReplacedAndOriginalListRemainsReadable()
    {
        using var database = Database.Create(); var count = (await database.Read()).Count;
        database.Execute("CREATE TABLE [JournalOrderAnnotations] ([OtherData] TEXT(100))");
        database.Execute("INSERT INTO [JournalOrderAnnotations] ([OtherData]) VALUES ('keep')");
        await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderAnnotations(database.Path).PrepareAnnotationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("keep", database.Scalar("SELECT [OtherData] FROM [JournalOrderAnnotations]"));
        var rows = await database.Read(); Assert.Equal(count, rows.Count);
        Assert.All(rows, row => Assert.True(row.AnnotationsUnavailable));
        var before = Convert.ToInt32(database.Scalar("SELECT Count(*) FROM [Orders]"));
        await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { HasBending = true }, TestContext.Current.CancellationToken));
        Assert.Equal(before, Convert.ToInt32(database.Scalar("SELECT Count(*) FROM [Orders]")));
    }

    [Fact]
    public async Task OrphanAnnotationConflictRollsBackRestoreAndNeverOverwritesManualData()
    {
        using var database = Database.Create();
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { HasBending = true, Comment = "Сохранить" }, TestContext.Current.CancellationToken);
        var store = new OleDbOrderMaintenance(database.Path);
        var record = (await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
        database.Execute($"DELETE FROM [Orders] WHERE [Код]={created.Id}");
        await Assert.ThrowsAsync<OrderWriteException>(() => store.RestoreAsync(record, TestContext.Current.CancellationToken));
        Assert.Equal(0, Convert.ToInt32(database.Scalar($"SELECT Count(*) FROM [Orders] WHERE [Код]={created.Id}")));
        Assert.Equal("Сохранить", database.Scalar($"SELECT [Comment] FROM [JournalOrderAnnotations] WHERE [OrderId]={created.Id}"));
    }
}
