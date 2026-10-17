using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Local;
using Jornal.Infrastructure.Demo;
using Xunit;
namespace Jornal.Tests;
public sealed class BatchCreationTests
{
    [Fact]
    public async Task LocalBatchSavesAllRowsWithPricesTimesAndOriginalHistory()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var drafts = new[] { LocalJournalFixture.Draft with { CuttingDurationMinutes = 1786, PartCount = 14, PunchCount = 31 },
            LocalJournalFixture.Draft with { OrderDate = new(2026, 3, 27), SpecificationNumber = "0327-1003", SheetCount = "1,3" } };
        var saved = await store.CreateBatchAsync(drafts, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 43, 44 }, saved.Select(row => row.Id));
        for (var i = 0; i < drafts.Length; i++) Assert.Equal(drafts[i].ToOrder(saved[i].Id, 977m), saved[i]);
        var reopened = await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken);
        Assert.All(saved, row => Assert.Contains(row, reopened));
        Assert.All(LocalJournalFixture.Orders, row => Assert.Contains(row, reopened));
    }
    [Fact]
    public async Task BadLaterDraftOrQuoteNeverSavesAnyPartOfLocalBatch()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path); var draft = LocalJournalFixture.Draft;
        var bytes = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateBatchAsync([draft, draft with { CustomerName = "" }], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<PriceCalculationException>(() => store.CreateBatchAsync([draft, draft with { ThicknessMillimeters = 6m }], TestContext.Current.CancellationToken));
        var quote = await new CuttingPriceService(store).CalculateAsync(draft.PriceRequest, TestContext.Current.CancellationToken);
        var changed = await Assert.ThrowsAsync<OrderBatchPriceChangedException>(() => store.CreateBatchAsync([draft, draft with { ExpectedQuote = quote with { RublesPerMeter = 1 } }], TestContext.Current.CancellationToken));
        Assert.Equal(1, changed.Index); Assert.Equal(quote, changed.Quote);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateBatchAsync([draft, draft], cancel.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task ConcurrentLocalBatchesKeepAllIdsUnique()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var result = await Task.WhenAll(new LocalJournalStore(fixture.Path).CreateBatchAsync([LocalJournalFixture.Draft, LocalJournalFixture.Draft], TestContext.Current.CancellationToken),
            new LocalJournalStore(fixture.Path).CreateBatchAsync([LocalJournalFixture.Draft, LocalJournalFixture.Draft], TestContext.Current.CancellationToken));
        Assert.Equal(4, result.SelectMany(rows => rows).Select(row => row.Id).Distinct().Count());
        Assert.Equal(6, (await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken)).Count);
    }
    [Fact]
    public async Task DemoBatchValidationAndLimitsAreAtomic()
    {
        var store = new DemoOrderStore(); var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateBatchAsync([LocalJournalFixture.Draft, LocalJournalFixture.Draft with { SheetCount = "2+1" }], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateBatchAsync([], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateBatchAsync(Enumerable.Repeat(LocalJournalFixture.Draft, 101).ToArray(), TestContext.Current.CancellationToken));
        Assert.Equal(before, await store.ReadAsync(TestContext.Current.CancellationToken));
        var saved = await store.CreateBatchAsync([LocalJournalFixture.Draft, LocalJournalFixture.Draft], TestContext.Current.CancellationToken);
        Assert.Equal(2, saved.Count); Assert.NotEqual(saved[0].Id, saved[1].Id);
    }
}
