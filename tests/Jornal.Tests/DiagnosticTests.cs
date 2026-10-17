using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void OptionalBuildAndStageMetadataPreservesOlderReportsAndGrouping()
    {
        var report = Report() with { BuildCommit = new string('a', 40), BuildDirty = true, Stage = DiagnosticStage.ReadOrders };
        report.Validate();
        var restored = JsonSerializer.Deserialize<DiagnosticReport>(JsonSerializer.Serialize(report, DiagnosticReport.Json), DiagnosticReport.Json)!;
        Assert.Equal(report, restored with { Exceptions = report.Exceptions, Recent = report.Recent });
        var older = report with { BuildCommit = null, BuildDirty = null, Stage = DiagnosticStage.Unknown };
        var json = JsonSerializer.Serialize(older, DiagnosticReport.Json);
        Assert.DoesNotContain("buildCommit", json);
        Assert.DoesNotContain("buildDirty", json);
        Assert.DoesNotContain("stage", json);
        JsonSerializer.Deserialize<DiagnosticReport>(json, DiagnosticReport.Json)!.Validate();
        Assert.Equal(report.Fingerprint, older.Fingerprint);
        Assert.Equal(report.Fingerprint, (report with { BuildCommit = new string('b', 40), Stage = DiagnosticStage.OpenDatabase }).Fingerprint);
        Assert.Throws<InvalidDataException>(() => (report with { BuildCommit = "SECRET-PATH" }).Validate());
        Assert.Throws<InvalidDataException>(() => (report with { BuildCommit = null }).Validate());
        Assert.Throws<InvalidDataException>(() => (report with { Stage = (DiagnosticStage)999 }).Validate());
    }

    [Fact]
    public async Task NestedAndConcurrentStagesStayWithTheirExceptionAcrossWrapping()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception> Fail(DiagnosticStage stage)
        {
            try
            {
                await DiagnosticStages.RunAsync(DiagnosticStage.ReadAccessResponse, async () =>
                {
                    await DiagnosticStages.RunAsync(stage, async () => { await gate.Task; throw new IOException("SECRET"); });
                });
            }
            catch (Exception exception) { return new OrderReadException("SECRET", exception); }
            throw new InvalidOperationException();
        }
        var first = Fail(DiagnosticStage.OpenDatabase);
        var second = Fail(DiagnosticStage.CommitTransaction);
        gate.SetResult();
        Assert.Equal(DiagnosticStage.OpenDatabase, DiagnosticStages.Of(await first));
        Assert.Equal(DiagnosticStage.CommitTransaction, DiagnosticStages.Of(await second));
        var wrapped = await second;
        var report = DiagnosticReport.Create(wrapped, DiagnosticOperation.OrderEdit, Guid.NewGuid().ToString("N"), "2026.1.0");
        Assert.Equal(DiagnosticStage.CommitTransaction, report.Stage);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(report, DiagnosticReport.Json));
        Assert.Equal(DiagnosticStage.Unknown, DiagnosticStages.Of(new IOException()));
        var hostError = AppDiagnostics.Attach(new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "SECRET"),
            report with { Component = DiagnosticComponent.AccessHost, WriteFailure = OrderWriteFailure.OutcomeUnknown });
        Assert.Equal(DiagnosticStage.CommitTransaction, DiagnosticStages.Of(hostError));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, hostError.Failure);
    }

    private static DiagnosticReport Report() => DiagnosticReport.Create(
        new IOException("Заказчик SECRET-CUSTOMER, C:\\shared\\OrderDB.mdb, цена 12345"),
        DiagnosticOperation.OrderRead, Guid.NewGuid().ToString("N"), "2026.1.0-dev");

    [Fact]
    public void ExceptionMessagesDataAndFilePathsNeverEnterTheReport()
    {
        Exception failure;
        try { throw new OrderReadException("SECRET-CUSTOMER C:\\shared\\OrderDB.mdb", new IOException("SECRET-DOCUMENT")); }
        catch (Exception exception) { failure = exception; }
        failure.Data["request"] = "SECRET-MAIL SECRET-PRICE";
        var report = DiagnosticReport.Create(failure, DiagnosticOperation.OrderRead, Guid.NewGuid().ToString("N"), "2026.1.0");
        report.Validate();
        var json = JsonSerializer.Serialize(report, DiagnosticReport.Json);
        Assert.DoesNotContain("SECRET", json);
        Assert.DoesNotContain("OrderDB", json);
        Assert.DoesNotContain(".cs:", json);
        Assert.Equal(2, report.Exceptions.Length);
        Assert.NotEmpty(report.Exceptions[0].Frames);
        Assert.Matches("^JR-[A-F0-9]{8}$", report.Code);
        Assert.Equal(report.Fingerprint, (report with { EventId = Guid.NewGuid().ToString("N"), Version = "2026.2.0", SessionId = Guid.NewGuid().ToString("N") }).Fingerprint);
        Assert.NotEqual(report.Fingerprint, (report with { Operation = DiagnosticOperation.OrderCreate }).Fingerprint);
    }

    [Fact]
    public void ExtraFieldsCannotBeReadOrExported()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path);
        var report = Report();
        Assert.True(store.Save(report, true));
        var extra = JsonSerializer.Serialize(report, DiagnosticReport.Json).TrimEnd('}') + ",\"message\":\"SECRET\"}";
        File.WriteAllText(System.IO.Path.Combine(root.Path, "events", "invalid.json"), extra);
        File.WriteAllText(System.IO.Path.Combine(root.Path, "outbox", "invalid.json"), extra);
        File.WriteAllText(System.IO.Path.Combine(root.Path, "OrderDB.mdb"), "SECRET");
        using var buffer = new MemoryStream();
        store.Export(buffer); buffer.Position = 0;
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries);
        Assert.Equal("events/" + report.EventId + ".json", entry.FullName);
        using var reader = new StreamReader(entry.Open());
        Assert.DoesNotContain("SECRET", reader.ReadToEnd());
        Assert.Single(store.Due(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SettingsDefaultOnAndPersistOffButCorruptSettingsFailClosed()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path);
        Assert.True(store.LoadAutomatic());
        Assert.True(store.SaveAutomatic(false));
        Assert.False(new DiagnosticStore(root.Path).LoadAutomatic());
        File.WriteAllText(System.IO.Path.Combine(root.Path, "settings.json"), "{}");
        Assert.False(store.LoadAutomatic());
        Assert.True(store.SettingsUnreadable);
    }

    [Fact]
    public async Task OfflineReportsSurviveRestartAndBackOffWithoutThrowing()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path);
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("SECRET-NETWORK")));
        using (var service = new DiagnosticService(store, http, new("https://example.test/v1/reports"), "2026.1.0"))
        {
            service.Capture(DiagnosticOperation.OrderRead, new IOException("SECRET"));
            await service.FlushLocalAsync(TestContext.Current.CancellationToken);
            await service.SendDueAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, service.PendingCount);
            Assert.Empty(store.Due(DateTimeOffset.UtcNow));
            var pending = Assert.Single(store.Due(DateTimeOffset.UtcNow.AddMinutes(2)));
            Assert.Equal(1, pending.Attempts);
        }
        using var restarted = new DiagnosticService(new(root.Path), http, new("https://example.test/v1/reports"), "2026.1.0");
        Assert.True(restarted.SendAutomatically);
        Assert.Equal(1, restarted.PendingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyMatchingReceiptAcknowledgesDelivery(bool matching)
    {
        using var root = new Folder();
        var report = Report();
        var store = new DiagnosticStore(root.Path);
        store.Save(report, true);
        using var http = new HttpClient(new Handler(async (request, token) =>
        {
            var sent = await request.Content!.ReadFromJsonAsync<DiagnosticReport>(DiagnosticReport.Json, token);
            Assert.Equal(report.EventId, sent!.EventId);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DiagnosticReceipt(1, matching ? sent.EventId : Guid.NewGuid().ToString("N"), true), options: DiagnosticReport.Json) };
        }));
        using var service = new DiagnosticService(store, http, new("https://example.test/v1/reports"), "2026.1.0");
        await service.SendDueAsync(TestContext.Current.CancellationToken);
        Assert.Equal(matching ? 0 : 1, service.PendingCount);
        Assert.Equal(matching, service.LastAcceptedUtc is not null);
    }

    [Fact]
    public async Task AReceiverThatNeverRespondsTimesOutWithoutBlockingTheApplication()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path); store.Save(Report(), true);
        using var http = new HttpClient(new Handler(async (_, token) =>
        { await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); }));
        using var service = new DiagnosticService(store, http, new("https://example.test/v1/reports"), "2026.1.0");
        await service.SendDueAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal(1, service.PendingCount);
        Assert.Empty(store.Due(DateTimeOffset.UtcNow));
        Assert.Null(service.LastAcceptedUtc);
    }

    [Fact]
    public async Task RetainedOfflineReportIsDeliveredWhenConnectivityReturns()
    {
        using var root = new Folder(); var store = new DiagnosticStore(root.Path);
        store.Save(Report(), true);
        using var offline = new HttpClient(new Handler((_, _) => throw new HttpRequestException()));
        using (var service = new DiagnosticService(store, offline, new("https://example.test/v1/reports"), "2026.1.0"))
            await service.SendDueAsync(TestContext.Current.CancellationToken);
        var item = Assert.Single(store.Due(DateTimeOffset.UtcNow.AddMinutes(2)));
        // Сдвигаем только часы планировщика в тестовом хранилище: реальный повтор также ждёт свою паузу.
        store.Retry(item, DateTimeOffset.UtcNow.AddMinutes(-5), TimeSpan.FromMinutes(1));
        using var online = new HttpClient(new Handler(async (request, token) =>
        {
            var sent = await request.Content!.ReadFromJsonAsync<DiagnosticReport>(DiagnosticReport.Json, token);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new DiagnosticReceipt(1, sent!.EventId, true), options: DiagnosticReport.Json) };
        }));
        using var restarted = new DiagnosticService(new(root.Path), online, new("https://example.test/v1/reports"), "2026.1.0");
        await restarted.SendDueAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, restarted.PendingCount); Assert.NotNull(restarted.LastAcceptedUtc);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(500)]
    public async Task InvalidOrLargeResponseDoesNotLoseTheReport(int status)
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path); store.Save(Report(), true);
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(new string('x', 5000)) })));
        using var service = new DiagnosticService(store, http, new("https://example.test/v1/reports"), "2026.1.0");
        await service.SendDueAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, service.PendingCount);
        Assert.Null(service.LastAcceptedUtc);
    }

    [Fact]
    public async Task DisableCancelsNetworkAndRemovesPendingButKeepsLocalReports()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path); store.Save(Report(), true);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { cancelled.SetResult(); throw; }
            return new(HttpStatusCode.OK);
        }));
        using var service = new DiagnosticService(store, http, new("https://example.test/v1/reports"), "2026.1.0");
        var sending = service.SendDueAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        service.SetAutomatic(false);
        await sending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(cancelled.Task.IsCompletedSuccessfully);
        Assert.Equal(0, service.PendingCount);
        Assert.False(new DiagnosticStore(root.Path).LoadAutomatic());
        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(root.Path, "events"), "*.json"));
    }

    [Fact]
    public async Task OffThenOnDoesNotResurrectRecordsQueuedUnderTheOldChoice()
    {
        using var root = new Folder();
        using var http = new HttpClient();
        using var service = new DiagnosticService(new(root.Path), http, null, "2026.1.0");
        for (var i = 0; i < 50; i++) service.Capture(DiagnosticOperation.Unexpected, new IOException("SECRET"));
        service.SetAutomatic(false);
        service.SetAutomatic(true);
        await service.FlushLocalAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, service.PendingCount);
        service.Capture(DiagnosticOperation.Unexpected, new IOException());
        await service.FlushLocalAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, service.PendingCount);
    }

    [Fact]
    public async Task CancelledSendAndUnavailableProfileDoNotEscapeIntoApplication()
    {
        using var root = new Folder();
        var occupied = System.IO.Path.Combine(root.Path, "occupied"); File.WriteAllText(occupied, "file");
        using var http = new HttpClient();
        using var service = new DiagnosticService(new(occupied), http, new("https://example.test/v1/reports"), "2026.1.0");
        service.Capture(DiagnosticOperation.Preferences, new IOException());
        await service.FlushLocalAsync(TestContext.Current.CancellationToken);
        Assert.False(service.StorageAvailable);
        service.SetAutomatic(false);
        Assert.False(service.PreferencesSaved);
        await service.SendDueAsync(new CancellationToken(true));
    }

    [Fact]
    public void DiskQueueAndRetentionAreBounded()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path);
        for (var i = 0; i < 210; i++) Assert.True(store.Save(Report(), true));
        Assert.Equal(DiagnosticStore.MaximumPending, store.PendingCount);
        Assert.Equal(DiagnosticStore.MaximumReports, Directory.GetFiles(System.IO.Path.Combine(root.Path, "events"), "*.json").Length);
        var old = Report() with { CreatedUtc = DateTimeOffset.UtcNow.AddDays(-15) };
        Assert.True(store.Save(old, true));
        Assert.DoesNotContain(store.Due(DateTimeOffset.UtcNow), item => item.Report.EventId == old.EventId);
        var due = Assert.Single(store.Due(DateTimeOffset.UtcNow).Take(1));
        store.Retry(due with { Attempts = 11 }, DateTimeOffset.UtcNow);
        Assert.False(File.Exists(System.IO.Path.Combine(root.Path, "outbox", due.Report.EventId + ".json")));
    }

    [Fact]
    public void RetryAfterIsRespectedAndClamped()
    {
        using var root = new Folder();
        var store = new DiagnosticStore(root.Path); store.Save(Report(), true);
        var now = DateTimeOffset.UtcNow;
        var item = Assert.Single(store.Due(now));
        store.Retry(item, now, TimeSpan.FromHours(24));
        var retry = Assert.Single(store.Due(now.AddHours(7)));
        Assert.Equal(now.AddHours(6), retry.NextAttemptUtc);
        Assert.Empty(store.Due(now.AddHours(5)));
    }

    [Fact]
    public void FatalIsStoredSynchronouslyAndNoticeCanBeAcknowledgedAcrossRestarts()
    {
        using var root = new Folder();
        using var http = new HttpClient();
        using (var service = new DiagnosticService(new(root.Path), http, null, "2026.1.0"))
            service.Capture(DiagnosticOperation.Unexpected, new IOException(), fatal: true);
        using var restarted = new DiagnosticService(new(root.Path), http, null, "2026.1.0");
        Assert.True(restarted.HasPreviousFatal);
        restarted.AcknowledgePreviousFatal();
        using var third = new DiagnosticService(new(root.Path), http, null, "2026.1.0");
        Assert.False(third.HasPreviousFatal);
        Assert.Single(Directory.GetFiles(System.IO.Path.Combine(root.Path, "events"), "*.json"));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    private sealed class Folder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "journal-diagnostics-test-" + Guid.NewGuid().ToString("N"));
        public Folder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
