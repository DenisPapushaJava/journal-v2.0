using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jornal.Core.Pricing;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

[CollectionDefinition("Diagnostic read context", DisableParallelization = true)]
public sealed class DiagnosticReadContextCollection { }

[Collection("Diagnostic read context")]
public sealed class DiagnosticReadContextTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(@"\\SECRET-SERVER\SECRET-SHARE\db.mdb", DiagnosticPathKind.UncPath)]
    [InlineData(@"\\?\UNC\SECRET-SERVER\db.mdb", DiagnosticPathKind.UncPath)]
    [InlineData(@"Z:\SECRET\db.mdb", DiagnosticPathKind.DrivePath)]
    [InlineData(@"\\?\C:\SECRET\db.mdb", DiagnosticPathKind.DrivePath)]
    [InlineData("/SECRET/db.mdb", DiagnosticPathKind.AbsolutePath)]
    [InlineData("SECRET/db.mdb", DiagnosticPathKind.RelativePath)]
    [InlineData("", DiagnosticPathKind.Unknown)]
    public void PathClassificationOnlyUsesSyntax(string path, DiagnosticPathKind expected) => Assert.Equal(expected, DiagnosticReads.ClassifyPath(path));

    [Fact]
    public async Task FailedReadKeepsOriginalExceptionAndLastSuccessBelongsToTheSameSource()
    {
        using var fixture = new Fixture();
        const string path = @"\\SECRET-SERVER\SECRET-SHARE\db.mdb";
        Assert.Equal(42, await DiagnosticReads.ObserveAsync(DiagnosticOperation.OrderRead, DiagnosticReadAction.ReadDay, path, () =>
        { fixture.Clock.Advance(125); return Task.FromResult(42); }));
        var success = fixture.Clock.GetUtcNow();
        var failure = new PriceReadException("SECRET-PRICE", new FileNotFoundException("SECRET-CUSTOMER", path));
        var actual = await Assert.ThrowsAsync<PriceReadException>(() => DiagnosticReads.ObserveAsync<int>(DiagnosticOperation.PriceRead,
            DiagnosticReadAction.ReadMachineCatalog, path, () => { fixture.Clock.Advance(37); return Task.FromException<int>(failure); }));
        Assert.Same(failure, actual);
        var report = DiagnosticReport.Create(actual, DiagnosticOperation.PriceRead, fixture.Service.SessionId, "2026.1.0");
        Assert.Equal(new(DiagnosticReadAction.ReadMachineCatalog, DiagnosticPathKind.UncPath, 37, success), report.ReadContext);
        Assert.Equal(2, report.Exceptions[1].NativeCode);
        report.Validate(); Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(report, DiagnosticReport.Json));
        var other = await Assert.ThrowsAsync<IOException>(() => DiagnosticReads.ObserveAsync<int>(DiagnosticOperation.OrderRead,
            DiagnosticReadAction.ReadOrders, "other-SECRET.mdb", () => Task.FromException<int>(new IOException("SECRET"))));
        Assert.Null(DiagnosticReads.ContextOf(other)!.LastSuccessfulReadUtc);
        fixture.Service.Capture(DiagnosticOperation.PriceRead, failure);
        await fixture.Service.FlushLocalAsync(Token);
        var recent = Assert.Single(fixture.Service.Store.Due(DateTimeOffset.UtcNow)).Report.Recent;
        Assert.Equal(new[] { DiagnosticReadOutcome.Succeeded, DiagnosticReadOutcome.Failed, DiagnosticReadOutcome.Failed }, recent.Select(item => item.Outcome));
        Assert.Equal(125, recent[0].DurationMilliseconds); Assert.Equal(37, recent[1].DurationMilliseconds);
        Assert.Null(DiagnosticReads.EnrichHostReport(report with { ReadContext = null }).ReadContext);
    }

    [Fact]
    public async Task ParallelReadsAndCancellationDoNotShareContextOrGrowHistoryWithoutBound()
    {
        using var fixture = new Fixture();
        var firstGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondGate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = DiagnosticReads.ObserveAsync(DiagnosticOperation.OrderRead, DiagnosticReadAction.ReadDay, @"C:\SECRET.mdb", () => firstGate.Task);
        var second = DiagnosticReads.ObserveAsync(DiagnosticOperation.PriceRead, DiagnosticReadAction.ReadPriceTable, @"\\SECRET\share\db.mdb", () => secondGate.Task);
        for (var index = 0; index < 24; index++)
            await DiagnosticReads.ObserveAsync(DiagnosticOperation.OrderRead, DiagnosticReadAction.ReadOrders, "SECRET.mdb", () => Task.FromResult(0));
        fixture.Clock.Advance(20);
        firstGate.SetCanceled(Token); secondGate.SetException(new IOException("SECRET"));
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var failed = await Assert.ThrowsAsync<IOException>(() => second);
        Assert.Equal(DiagnosticReadAction.ReadDay, DiagnosticReads.ContextOf(cancelled)!.Action);
        Assert.Equal(DiagnosticReadAction.ReadPriceTable, DiagnosticReads.ContextOf(failed)!.Action);
        Assert.Equal(DiagnosticPathKind.UncPath, DiagnosticReads.ContextOf(failed)!.PathKind);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiagnosticReads.ObserveAsync<int>(DiagnosticOperation.OrderRead,
            DiagnosticReadAction.ReadOrders, "SECRET.mdb", () => Task.FromCanceled<int>(new CancellationToken(true))));
        fixture.Service.Capture(DiagnosticOperation.PriceRead, failed); await fixture.Service.FlushLocalAsync(Token);
        var report = Assert.Single(fixture.Service.Store.Due(DateTimeOffset.UtcNow)).Report;
        Assert.Equal(16, report.Recent.Length);
        Assert.All(report.Recent.Take(15), item => Assert.Equal(DiagnosticReadOutcome.Succeeded, item.Outcome));
        Assert.Equal(DiagnosticReadOutcome.Cancelled, report.Recent[^1].Outcome);
        // Завершение вытесненных действий не возвращает старые узлы в ограниченную историю.
        Assert.Null(DiagnosticReads.EnrichHostReport(report with { ReadContext = null }).ReadContext);
    }

    [Fact]
    public async Task HostEventIsEnrichedBeforeQueueingWithoutChangingItsIdentityOrWriteOutcome()
    {
        using var fixture = new Fixture();
        var host = DiagnosticReport.Create(new IOException("SECRET"), DiagnosticOperation.OrderRead, fixture.Service.SessionId,
            "2026.1.0", component: DiagnosticComponent.AccessHost) with { Stage = DiagnosticStage.OpenDatabase };
        var failure = await Assert.ThrowsAsync<OrderReadException>(() => DiagnosticReads.ObserveAsync<int>(DiagnosticOperation.OrderRead, DiagnosticReadAction.ReadOrder, "SECRET.mdb", async () =>
        {
            await DiagnosticReads.ObserveAsync(DiagnosticOperation.PriceRead, DiagnosticReadAction.ReadPriceTable, "OTHER-SECRET.mdb", () => Task.FromResult(0));
            fixture.Clock.Advance(22);
            var enriched = DiagnosticReads.EnrichHostReport(host);
            Assert.Equal(host.EventId, enriched.EventId); Assert.Equal(host.Fingerprint, enriched.Fingerprint);
            Assert.Equal(host.Component, enriched.Component); Assert.Equal(host.WriteFailure, enriched.WriteFailure);
            Assert.Equal(host.Stage, enriched.Stage); Assert.Equal(22, enriched.ReadContext!.DurationMilliseconds);
            Assert.Equal(DiagnosticReadAction.ReadOrder, enriched.ReadContext.Action);
            Assert.Null(enriched.ReadContext.LastSuccessfulReadUtc);
            Assert.Equal(DiagnosticReadOutcome.Failed, enriched.Recent[0].Outcome);
            Assert.Equal(DiagnosticReadOutcome.Succeeded, enriched.Recent[1].Outcome);
            var uncertain = host with { Operation = DiagnosticOperation.OrderEdit, WriteFailure = OrderWriteFailure.OutcomeUnknown };
            Assert.Same(uncertain, DiagnosticReads.EnrichHostReport(uncertain));
            fixture.Service.Record(enriched);
            throw AppDiagnostics.Attach(new OrderReadException("SECRET"), enriched);
        }));
        Assert.Equal(host.Code, AppDiagnostics.Capture(DiagnosticOperation.OrderRead, failure));
        await fixture.Service.FlushLocalAsync(Token);
        Assert.Equal(host.EventId, Assert.Single(fixture.Service.Store.Due(DateTimeOffset.UtcNow)).Report.EventId);
    }

    [Fact]
    public async Task NestedFailureKeepsTheMostSpecificReadAndStillCompletesBothHistoryEntries()
    {
        using var fixture = new Fixture();
        var failure = new IOException("SECRET");
        var actual = await Assert.ThrowsAsync<IOException>(() => DiagnosticReads.ObserveAsync(DiagnosticOperation.OrderRead,
            DiagnosticReadAction.ReadOrders, "outer-SECRET.mdb", () => DiagnosticReads.ObserveAsync<int>(DiagnosticOperation.PriceRead,
                DiagnosticReadAction.ReadPriceTable, "inner-SECRET.mdb", () => Task.FromException<int>(failure))));
        Assert.Same(failure, actual);
        Assert.Equal(DiagnosticReadAction.ReadPriceTable, DiagnosticReads.ContextOf(actual)!.Action);
        fixture.Service.Capture(DiagnosticOperation.PriceRead, actual); await fixture.Service.FlushLocalAsync(Token);
        Assert.All(Assert.Single(fixture.Service.Store.Due(DateTimeOffset.UtcNow)).Report.Recent,
            item => Assert.Equal(DiagnosticReadOutcome.Failed, item.Outcome));
        Assert.Null(DiagnosticReads.EnrichHostReport(DiagnosticReport.Create(new IOException(), DiagnosticOperation.OrderRead, fixture.Service.SessionId, "2026.1.0")).ReadContext);
    }

    [Fact]
    public async Task DiagnosticClockFailureCannotChangeTheResultOfAnApplicationRead()
    {
        using var fixture = new Fixture(new BrokenClock());
        Assert.Equal(7, await DiagnosticReads.ObserveAsync(DiagnosticOperation.OrderRead, DiagnosticReadAction.ReadOrders, "SECRET", () => Task.FromResult(7)));
        var exception = new IOException("SECRET");
        var actual = await Assert.ThrowsAsync<IOException>(() => DiagnosticReads.ObserveAsync<int>(DiagnosticOperation.OrderRead,
            DiagnosticReadAction.ReadOrders, "SECRET", () => Task.FromException<int>(exception)));
        Assert.Same(exception, actual);
    }

    [Fact]
    public async Task MissingMdbReportsTheExactActionAndAUsefulMessageWithoutLeakingItsName()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "SECRET-CLIENT.mdb");
        var reader = new AccessPriceReader(path);
        var error = await Assert.ThrowsAsync<PriceReadException>(() => reader.ReadMachinesAsync(Token));
        Assert.StartsWith("База данных «SECRET-CLIENT.mdb» недоступна.", error.Message);
        var report = DiagnosticReport.Create(error, DiagnosticOperation.PriceRead, fixture.Service.SessionId, "2026.1.0");
        Assert.Equal(DiagnosticReadAction.ReadMachineCatalog, report.ReadContext!.Action);
        Assert.IsType<FileNotFoundException>(error.InnerException);
        Assert.Equal(DiagnosticStage.OpenDatabase, report.Stage);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(report, DiagnosticReport.Json));
    }

    [Fact]
    public void OptionalReadDetailsKeepLegacyQueuesAndGroupingButRejectUnapprovedFields()
    {
        var original = DiagnosticReport.Create(new IOException("SECRET"), DiagnosticOperation.OrderRead, Guid.NewGuid().ToString("N"), "2026.1.0");
        var extended = WithDetails(original);
        extended.Validate(); original.Validate();
        Assert.Equal(original.Fingerprint, extended.Fingerprint);
        var restored = JsonSerializer.Deserialize<DiagnosticReport>(JsonSerializer.Serialize(extended, DiagnosticReport.Json), DiagnosticReport.Json)!;
        restored.Validate(); Assert.Equal(extended.ReadContext, restored.ReadContext);
        var old = JsonSerializer.Deserialize<DiagnosticReport>(JsonSerializer.Serialize(original, DiagnosticReport.Json), DiagnosticReport.Json)!;
        old.Validate(); Assert.Null(old.ReadContext);
        Assert.DoesNotContain("readContext", JsonSerializer.Serialize(extended.WithoutReadDetails(), DiagnosticReport.Json));
        Assert.All(extended.WithoutReadDetails().Recent, item => Assert.Equal(DiagnosticReadOutcome.Unknown, item.Outcome));
        var json = JsonNode.Parse(JsonSerializer.Serialize(extended, DiagnosticReport.Json))!;
        json["readContext"]!["sourcePath"] = "SECRET";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DiagnosticReport>(json.ToJsonString(), DiagnosticReport.Json));
        Assert.Throws<InvalidDataException>(() => (extended with { ReadContext = extended.ReadContext! with { DurationMilliseconds = -1 } }).Validate());
        Assert.Throws<InvalidDataException>(() => (extended with { Recent = [new(DiagnosticOperation.OrderRead, DateTimeOffset.UtcNow,
            DiagnosticReadAction.ReadOrders, DiagnosticReadOutcome.Started, 3)] }).Validate());
    }

    [Theory]
    [InlineData(400)] [InlineData(422)]
    public async Task OldReceiverGetsOneLegacyFallbackWithTheSameEventAndTheLocalReportStaysComplete(int rejected)
    {
        using var fixture = new Fixture();
        var report = WithDetails(DiagnosticReport.Create(new IOException("SECRET"), DiagnosticOperation.OrderRead, fixture.Service.SessionId, "2026.1.0"));
        fixture.Service.Store.Save(report, true);
        var sent = new List<DiagnosticReport>();
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            var json = await request.Content!.ReadAsStringAsync(token);
            var value = JsonSerializer.Deserialize<DiagnosticReport>(json, DiagnosticReport.Json)!;
            sent.Add(value);
            if (sent.Count == 1) return new((HttpStatusCode)rejected);
            Assert.Equal(report.EventId, value.EventId); Assert.False(value.HasReadDetails);
            using var document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("readContext", out _));
            Assert.Equal(new[] { "at", "operation" }, document.RootElement.GetProperty("recent")[0].EnumerateObject().Select(property => property.Name).Order().ToArray());
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DiagnosticReceipt(1, value.EventId, true), options: DiagnosticReport.Json) };
        }));
        using var sender = new DiagnosticService(fixture.Service.Store, http, new("https://receiver.test/v1/reports"), "2026.1.0");
        await sender.SendDueAsync(Token);
        Assert.Equal(2, sent.Count); Assert.Equal(0, sender.PendingCount); Assert.NotNull(sender.LastAcceptedUtc);
        var local = File.ReadAllText(Path.Combine(fixture.Root, "events", report.EventId + ".json"));
        Assert.Contains("readContext", local); Assert.DoesNotContain("SECRET", local);
    }

    [Theory]
    [InlineData(200)] [InlineData(400)] [InlineData(500)]
    public async Task ExtendedSendingStillRequiresMatchingReceiptsAndNeverLoopsOnRejection(int status)
    {
        using var fixture = new Fixture();
        var report = WithDetails(DiagnosticReport.Create(new IOException("SECRET"), DiagnosticOperation.OrderRead, fixture.Service.SessionId, "2026.1.0"));
        fixture.Service.Store.Save(report, true); var calls = 0;
        using var http = new HttpClient(new Handler((_, _) =>
        { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") }); }));
        using var sender = new DiagnosticService(fixture.Service.Store, http, new("https://receiver.test/v1/reports"), "2026.1.0");
        await sender.SendDueAsync(Token);
        Assert.Equal(status == 400 ? 2 : 1, calls);
        Assert.Equal(status == 400 ? 0 : 1, sender.PendingCount);
        Assert.Null(sender.LastAcceptedUtc);
    }

    private static DiagnosticReport WithDetails(DiagnosticReport report) => report with
    {
        ReadContext = new(DiagnosticReadAction.ReadMachineCatalog, DiagnosticPathKind.DrivePath, 25),
        Recent = [new(DiagnosticOperation.OrderRead, DateTimeOffset.UtcNow, DiagnosticReadAction.ReadOrders, DiagnosticReadOutcome.Succeeded, 12)]
    };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
    private class Clock : TimeProvider
    {
        private long _milliseconds;
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
        public void Advance(long milliseconds) => Interlocked.Add(ref _milliseconds, milliseconds);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Interlocked.Read(ref _milliseconds);
        public override DateTimeOffset GetUtcNow() => _start.AddMilliseconds(GetTimestamp());
    }
    private sealed class BrokenClock : Clock { public override long GetTimestamp() => throw new IOException("SECRET"); }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "journal-read-context-" + Guid.NewGuid().ToString("N"));
        public Clock Clock { get; }
        private readonly HttpClient _http = new();
        private readonly DiagnosticService? _previous = AppDiagnostics.Current;
        public DiagnosticService Service { get; }
        public Fixture(Clock? clock = null)
        {
            Directory.CreateDirectory(Root);
            Clock = clock ?? new();
            Service = new(new(Root), _http, null, "2026.1.0", Clock);
            AppDiagnostics.Current = Service;
        }
        public void Dispose()
        {
            AppDiagnostics.Current = _previous; Service.Dispose(); _http.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
