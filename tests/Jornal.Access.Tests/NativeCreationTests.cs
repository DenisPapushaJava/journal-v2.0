using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Theory]
    [InlineData("Лазер", "", "2.5", 977)]
    [InlineData("Плазма", "", "2", 5750)]
    [InlineData("Плазма", "", "3", 0)]
    [InlineData("Газо-кислородная", "", "20", 1150)]
    [InlineData("HansLaser", "1", "2.5", 977)]
    [InlineData("HansLaser", "2", "2.5", 977)]
    [InlineData("HansLaser", "01", "2.5", 977)]
    public async Task CreatesTypedOrderWithDatabaseIdentityWithoutChangingHistoryOrSchema(string kind, string materialId, string thickness, int price)
    {
        using var database = Database.Create();
        var original = await database.Read(); var schema = database.Schema();
        var creator = new OleDbManualOrderCreator(database.Path);
        var draft = CreationDraft() with { CuttingType = kind, MaterialId = materialId,
            ThicknessMillimeters = decimal.Parse(thickness, System.Globalization.CultureInfo.InvariantCulture), InitialColor = -16711936 };
        var saved = await creator.CreateAsync(draft, TestContext.Current.CancellationToken);
        Assert.True(saved.Id > original.Max(row => row.Id));
        Assert.Equal(draft.ToOrder(saved.Id, price), saved);
        Assert.Null(saved.DeliveryNumber);
        var reopened = await database.Read();
        Assert.Equal(saved, reopened.Single(row => row.Id == saved.Id));
        Assert.Equal(original, reopened.Where(row => row.Id != saved.Id));
        Assert.Equal(schema, database.Schema());
        Assert.Equal(17, database.Scalar("SELECT [2,5] FROM [PriceLL500]"));
    }

    [Fact]
    public async Task PriceChangeDoesNotInsertUntilTheNewQuoteIsReviewed()
    {
        using var database = Database.Create(); var before = await database.Read();
        var creator = new OleDbManualOrderCreator(database.Path);
        var draft = CreationDraft();
        var quote = await new CuttingPriceService(creator).CalculateAsync(draft.PriceRequest, TestContext.Current.CancellationToken);
        database.Execute("UPDATE [PriceLL500] SET [2,5]=1");
        var changed = await Assert.ThrowsAsync<OrderPriceChangedException>(() => creator.CreateAsync(draft with { ExpectedQuote = quote }, TestContext.Current.CancellationToken));
        Assert.Equal(57m, changed.Quote.StoredPrice); // Прежний Double/ToEven, а не decimal-результат 58.
        Assert.Equal(before, await database.Read());
        var saved = await creator.CreateAsync(draft with { ExpectedQuote = changed.Quote }, TestContext.Current.CancellationToken);
        Assert.Equal(57m, saved.StoredCuttingPrice);
        Assert.Equal(before.Count + 1, (await database.Read()).Count);
    }

    [Fact]
    public async Task MissingNullAndEmptyPricesNeverCreateAnOrder()
    {
        using var database = Database.Create(); var before = await database.Read();
        var creator = new OleDbManualOrderCreator(database.Path);
        await Assert.ThrowsAsync<PriceCalculationException>(() => creator.CreateAsync(CreationDraft() with { CuttingType = "Плазма" }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<PriceCalculationException>(() => creator.CreateAsync(CreationDraft() with { ThicknessMillimeters = 4m }, TestContext.Current.CancellationToken));
        database.Execute("DELETE FROM [PriceLL500]");
        await Assert.ThrowsAsync<PriceCalculationException>(() => creator.CreateAsync(CreationDraft(), TestContext.Current.CancellationToken));
        database.Execute("DROP TABLE [PriceLL500]");
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => creator.CreateAsync(CreationDraft(), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(before, await database.Read());
    }

    [Fact]
    public async Task ConstraintFailureRollsBackAndRetryCreatesOnlyOneOrder()
    {
        using var database = Database.Create(); var before = await database.Read();
        database.Execute("CREATE UNIQUE INDEX [UniqueSpec] ON [Orders] ([Спецификация])");
        var creator = new OleDbManualOrderCreator(database.Path);
        var failed = await Assert.ThrowsAsync<OrderWriteException>(() => creator.CreateAsync(CreationDraft() with { SpecificationNumber = before[0].SpecificationNumber }, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failed.Failure);
        Assert.Equal(before, await database.Read());
        var saved = await creator.CreateAsync(CreationDraft(), TestContext.Current.CancellationToken);
        Assert.Equal(before.Count + 1, (await database.Read()).Count);
        Assert.Equal(saved, (await database.Read()).Single(row => row.Id == saved.Id));
    }

    [Fact]
    public async Task ValidationAndCancellationHappenBeforeOpeningTheFileForCreation()
    {
        using var database = Database.Create();
        var bytes = SHA256.HashData(File.ReadAllBytes(database.Path));
        var creator = new OleDbManualOrderCreator(database.Path);
        foreach (var draft in new[] { CreationDraft() with { CustomerName = new string('А', 256) },
                     CreationDraft() with { OrderDate = new(1, 1, 1) }, CreationDraft() with { MaterialId = "x|2" } })
            await Assert.ThrowsAsync<OrderWriteException>(() => creator.CreateAsync(draft, TestContext.Current.CancellationToken));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creator.CreateAsync(CreationDraft(), cancel.Token));
        Assert.Equal(bytes, SHA256.HashData(File.ReadAllBytes(database.Path)));
    }

    [Fact]
    public async Task ConcurrentCreatorsReturnOnlyTheirOwnAutonumberRows()
    {
        using var database = Database.Create(); var before = await database.Read();
        async Task<OrderSummary?> Add(string customer)
        {
            try { return await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with { CustomerName = customer }, TestContext.Current.CancellationToken); }
            catch (OrderWriteException error) { Assert.Equal(OrderWriteFailure.NotSaved, error.Failure); return null; }
        }
        var results = await Task.WhenAll(Add("Первый клиент"), Add("Второй клиент"));
        var saved = results.OfType<OrderSummary>().ToArray();
        Assert.NotEmpty(saved); Assert.Equal(saved.Length, saved.Select(row => row.Id).Distinct().Count());
        var after = await database.Read();
        Assert.Equal(before.Count + saved.Length, after.Count);
        Assert.All(saved, row => Assert.Equal(row, after.Single(actual => actual.Id == row.Id)));
        Assert.Equal(before, after.Where(row => before.Any(old => old.Id == row.Id)));
    }

    private static ManualOrderDraft CreationDraft() => new(new(2026, 1, 2), "0102--44'; не SQL", "Менеджер 'А'", "Клиент [] %_*?",
        "Лазер", "5083H111", 2.5m, "1500x6000", "02 / комплект", 25, "0012,350", 2.3m, "", "");
}
