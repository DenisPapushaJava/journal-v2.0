using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task DurationPreparationBacksUpOnceAndPreservesLegacyColumnsPricesAndRows()
    {
        using var database = Database.Create();
        var rows = await database.Read(); var schema = database.Schema();
        var before = SHA256.HashData(File.ReadAllBytes(database.Path));
        var initializer = new OleDbOrderExtras(database.Path);
        await initializer.PrepareCuttingTimeAsync(TestContext.Current.CancellationToken);
        await initializer.PrepareCuttingTimeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(schema, database.Schema()); Assert.Equal(rows, await database.Read());
        Assert.Equal(0, Convert.ToInt32(database.Scalar("SELECT Count(*) FROM [JournalOrderExtras]")));
        Assert.Equal(17, database.Scalar("SELECT [2,5] FROM [PriceLL500]"));
        var backup = Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-journal-extras-*.bak"));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(backup)));
    }

    [Fact]
    public async Task TimeRoundTripsWithoutExtendingCoefficientAndLegacyWritesKeepWorking()
    {
        using var database = Database.Create(); var schema = database.Schema();
        var draft = CreationDraft() with { CuttingDurationMinutes = 342 * 60 + 51 };
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(draft, TestContext.Current.CancellationToken);
        Assert.Equal(20571L, created.CuttingDurationMinutes);
        Assert.Equal(draft.StoredCoefficient, created.StoredCoefficient);
        Assert.Equal(created, (await database.Read()).Single(row => row.Id == created.Id));
        database.Execute($"UPDATE [Orders] SET [Менеджер]='Legacy' WHERE [Код]={created.Id}");
        var renamed = (await database.Read()).Single(row => row.Id == created.Id);
        Assert.Equal(created.CuttingDurationMinutes, renamed.CuttingDurationMinutes);
        database.Execute($"UPDATE [Orders] SET [Ход]=[Ход]+1 WHERE [Код]={created.Id}");
        var changed = (await database.Read()).Single(row => row.Id == created.Id);
        Assert.True(changed.CuttingTimeNeedsReview); Assert.Null(changed.CuttingDurationMinutes);
        // Никаких FK, блокирующих DELETE старой программы.
        database.Execute($"DELETE FROM [Orders] WHERE [Код]={created.Id}");
        Assert.DoesNotContain(await database.Read(), row => row.Id == created.Id);
        Assert.Equal(schema, database.Schema());
    }

    [Fact]
    public async Task EditingDeletingAndRestoringTimeAreAtomicAndCheckMetadataConflicts()
    {
        using var database = Database.Create();
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { CuttingDurationMinutes = 90 }, TestContext.Current.CancellationToken);
        var store = new OleDbOrderMaintenance(database.Path);
        var before = (await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
        var values = before with { CuttingTime = OrderCuttingTime.For(before.ToSummary(), 91) };
        var saved = await store.UpdateAsync(new(before, values), TestContext.Current.CancellationToken);
        Assert.Equal(91L, saved.ToSummary().CuttingDurationMinutes);
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(new(before, values), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Equal(saved, await store.ReadRecordAsync(saved.Id, TestContext.Current.CancellationToken));
        await store.DeleteAsync(saved, TestContext.Current.CancellationToken);
        Assert.Equal(0, Convert.ToInt32(database.Scalar($"SELECT Count(*) FROM [JournalOrderExtras] WHERE [OrderId]={saved.Id}")));
        Assert.Equal(saved, await store.RestoreAsync(saved, TestContext.Current.CancellationToken));
        Assert.Equal(91L, (await database.Read()).Single(row => row.Id == saved.Id).CuttingDurationMinutes);
    }

    [Fact]
    public async Task ExistingUnknownExtrasTableIsNeverReplaced()
    {
        using var database = Database.Create(); var rows = await database.Read();
        database.Execute("CREATE TABLE [JournalOrderExtras] ([OtherData] TEXT(100))");
        database.Execute("INSERT INTO [JournalOrderExtras] ([OtherData]) VALUES ('keep')");
        await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderExtras(database.Path).PrepareCuttingTimeAsync(TestContext.Current.CancellationToken));
        Assert.Equal("keep", database.Scalar("SELECT [OtherData] FROM [JournalOrderExtras]"));
        Assert.Equal(rows.Count, Convert.ToInt32(database.Scalar("SELECT Count(*) FROM [Orders]")));
    }

    [Fact]
    public async Task DurationFailureRollsBackInsertedOrderAndDoesNotOverwriteOrphanMetadata()
    {
        using var database = Database.Create();
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { CuttingDurationMinutes = 20 }, TestContext.Current.CancellationToken);
        var store = new OleDbOrderMaintenance(database.Path);
        var archived = (await store.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
        database.Execute($"DELETE FROM [Orders] WHERE [Код]={created.Id}");
        await Assert.ThrowsAsync<OrderWriteException>(() => store.RestoreAsync(archived, TestContext.Current.CancellationToken));
        Assert.Equal(0, Convert.ToInt32(database.Scalar($"SELECT Count(*) FROM [Orders] WHERE [Код]={created.Id}")));
        Assert.Equal(1, Convert.ToInt32(database.Scalar($"SELECT Count(*) FROM [JournalOrderExtras] WHERE [OrderId]={created.Id}")));
    }
}
