using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;
namespace Jornal.Access.Tests;
public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task BatchCreationPreservesLegacySchemaValuesAndHistory()
    {
        using var database = Database.Create(); var before = await database.Read(); var schema = database.Schema();
        var drafts = new[] { CreationDraft() with { PartCount = 14, PunchCount = 31 },
            CreationDraft() with { CuttingType = "Плазма", ThicknessMillimeters = 2m, SheetCount = "1,3" } };
        var saved = await new OleDbManualOrderCreator(database.Path).CreateBatchAsync(drafts, TestContext.Current.CancellationToken);
        Assert.Equal(2, saved.Count); Assert.NotEqual(saved[0].Id, saved[1].Id);
        Assert.Equal(drafts[0].ToOrder(saved[0].Id, 977m), saved[0]);
        Assert.Equal(drafts[1].ToOrder(saved[1].Id, 5750m), saved[1]);
        var reopened = await database.Read();
        Assert.All(saved, row => Assert.Contains(row, reopened));
        Assert.Equal(before, reopened.Where(row => !saved.Any(created => created.Id == row.Id)));
        Assert.Equal(schema, database.Schema());
    }
    [Fact]
    public async Task LaterInsertConstraintFailureRollsBackEveryEarlierInsert()
    {
        using var database = Database.Create(); var before = await database.Read();
        database.Execute("CREATE UNIQUE INDEX [UniqueSpec] ON [Orders] ([Спецификация])");
        var creator = new OleDbManualOrderCreator(database.Path);
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => creator.CreateBatchAsync([CreationDraft(), CreationDraft() with { SpecificationNumber = before[0].SpecificationNumber }], TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(before, await database.Read());
    }
    [Fact]
    public async Task BatchStaleQuoteAndMissingTariffDoNotSaveEarlierRows()
    {
        using var database = Database.Create(); var before = await database.Read();
        var creator = new OleDbManualOrderCreator(database.Path); var draft = CreationDraft();
        var quote = await new CuttingPriceService(creator).CalculateAsync(draft.PriceRequest, TestContext.Current.CancellationToken);
        database.Execute("UPDATE [PriceLL500] SET [2,5]=1");
        var failure = await Assert.ThrowsAsync<OrderBatchPriceChangedException>(() => creator.CreateBatchAsync([draft, draft with { ExpectedQuote = quote }], TestContext.Current.CancellationToken));
        Assert.Equal(1, failure.Index); Assert.Equal(57m, failure.Quote.StoredPrice);
        await Assert.ThrowsAsync<PriceCalculationException>(() => creator.CreateBatchAsync([draft, draft with { ThicknessMillimeters = 4m }], TestContext.Current.CancellationToken));
        Assert.Equal(before, await database.Read());
    }
    [Fact]
    public async Task BatchDurationRowsAndOrdersRollBackTogether()
    {
        using var database = Database.Create(); var before = await database.Read(); var schema = database.Schema();
        await new OleDbOrderExtras(database.Path).PrepareCuttingTimeAsync(TestContext.Current.CancellationToken);
        database.Execute("CREATE UNIQUE INDEX [UniqueSpec] ON [Orders] ([Спецификация])");
        var creator = new OleDbManualOrderCreator(database.Path);
        var draft = CreationDraft() with { CuttingDurationMinutes = 20571 };
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => creator.CreateBatchAsync([draft, draft with { SpecificationNumber = before[0].SpecificationNumber }], TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(before, await database.Read());
        Assert.Equal(0, Convert.ToInt32(database.Scalar("SELECT Count(*) FROM [JournalOrderExtras]")));
        var saved = await creator.CreateBatchAsync([draft, draft with { SpecificationNumber = "batch-2", CuttingDurationMinutes = 2207 }], TestContext.Current.CancellationToken);
        Assert.Equal(new long?[] { 20571, 2207 }, saved.Select(row => row.CuttingDurationMinutes));
        Assert.Equal(2, Convert.ToInt32(database.Scalar("SELECT Count(*) FROM [JournalOrderExtras]")));
        Assert.Equal(schema, database.Schema());
    }

}
