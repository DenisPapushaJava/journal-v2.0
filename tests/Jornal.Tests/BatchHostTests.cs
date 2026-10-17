using System.Diagnostics;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;
namespace Jornal.Tests;
public sealed class BatchHostTests
{
    public static bool IsUnix => !OperatingSystem.IsWindows();
    private static ManualOrderDraft Draft => LocalJournalFixture.Draft;
    [Theory(Skip = "Искусственный однократный хост использует /bin/sh; настоящий x86 проверяется отдельно на Windows.", SkipUnless = nameof(IsUnix))]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public async Task UnconfirmedOrMalformedBatchResponseNeverAllowsBlindRetry(int variant)
    {
        var rows = new[] { Draft.ToOrder(100, 977), Draft.ToOrder(101, 977) };
        var response = variant switch
        {
            0 => new AccessHostResponse(1, [rows[0]]),
            1 => new AccessHostResponse(1, [rows[0], rows[0]]),
            2 => new AccessHostResponse(1, [rows[0], rows[1] with { CustomerName = "Другой" }]),
            3 => new AccessHostResponse(1, [rows[0], rows[1] with { StoredCuttingPrice = null }]),
            4 => new AccessHostResponse(1, Error: "Прайс изменился", Failure: OrderWriteFailure.NotSaved, ChangedDraftIndex: 1),
            5 => new AccessHostResponse(1, Error: "Прайс изменился", Failure: OrderWriteFailure.NotSaved, ChangedQuote: Quote, ChangedDraftIndex: 2),
            _ => new AccessHostResponse(1, [rows[0], rows[1] with { Id = 0 }])
        };
        using var host = new ReplyHost(response);
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => host.Client.CreateBatchAsync([Draft, Draft], TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, failure.Failure); Assert.True(failure.RequiresReload);
        Assert.Equal(1, host.Calls);
    }
    [Fact(Skip = "Искусственный хост использует /bin/sh.", SkipUnless = nameof(IsUnix))]
    public async Task BatchReplyConfirmsAllRowsOrProvidesAnIndexedReviewedQuote()
    {
        var rows = new[] { Draft.ToOrder(100, 977), Draft.ToOrder(101, 977) };
        using var host = new ReplyHost(new(1, rows));
        Assert.Equal(rows, await host.Client.CreateBatchAsync([Draft, Draft], TestContext.Current.CancellationToken));
        using var changed = new ReplyHost(new(1, Error: "Прайс изменился", Failure: OrderWriteFailure.NotSaved, ChangedQuote: Quote, ChangedDraftIndex: 1));
        var failure = await Assert.ThrowsAsync<OrderBatchPriceChangedException>(() => changed.Client.CreateBatchAsync([Draft, Draft], TestContext.Current.CancellationToken));
        Assert.Equal(1, failure.Index); Assert.Equal(Quote, failure.Quote);
        Assert.Equal(1, changed.Calls);
    }
    private static CuttingPriceQuote Quote => new(PriceTables.HansLaserId2, "2,5", 17, 25, 2.3m, 977m);
    private sealed class ReplyHost : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "journal-host-" + Guid.NewGuid().ToString("N") + ".json");
        public AccessHostClient Client { get; }
        public int Calls { get; private set; }
        public ReplyHost(AccessHostResponse response)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(response));
            Client = new("unused.mdb", "/bin/sh", Start);
        }
        private void Start(Process process)
        {
            Calls++;
            process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add("cat >/dev/null; cat -- \"$1\"");
            process.StartInfo.ArgumentList.Add("reply"); process.StartInfo.ArgumentList.Add(_path); process.Start();
        }
        public void Dispose() => File.Delete(_path);
    }
}
