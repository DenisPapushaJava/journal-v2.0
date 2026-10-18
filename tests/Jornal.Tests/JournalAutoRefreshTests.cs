using Jornal.Desktop.DataSources;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalAutoRefreshTests
{
    internal sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
    private sealed class Subscription : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
    private sealed class Fixture : IDisposable
    {
        public readonly Clock Clock = new();
        public readonly Queue<Action> Posted = new();
        public readonly List<Subscription> Subscriptions = [];
        public readonly List<(Action Changed, Action Error)> Signals = [];
        public readonly JournalAutoRefresh Monitor;
        public int Reads;
        public JournalRefreshOutcome Outcome = JournalRefreshOutcome.Refreshed;
        public Func<CancellationToken, Task<JournalRefreshOutcome>>? Read;
        public Fixture()
        {
            Monitor = new(async token => { Reads++; return Read is null ? Outcome : await Read(token); }, Posted.Enqueue, Clock,
                (_, changed, error) =>
                {
                    Signals.Add((changed, error));
                    var subscription = new Subscription(); Subscriptions.Add(subscription);
                    return Task.FromResult<IDisposable>(subscription);
                });
            Monitor.Configure("/test/OrderDB.mdb", true);
        }
        public async Task Tick(bool visible = true, bool available = true)
        {
            while (Posted.TryDequeue(out var action)) action();
            await Monitor.TickAsync(visible, available);
        }
        public async Task Settle()
        {
            await Tick(); Clock.Advance(3); await Tick(); Assert.Equal(1, Reads);
        }
        public void Dispose() => Monitor.Dispose();
    }

    [Fact]
    public async Task DuplicateEventsAreCoalescedAndActiveWritersCannotStarveOrFloodReads()
    {
        using var f = new Fixture(); await f.Settle();
        for (var second = 0; second < 60; second++)
        {
            for (var duplicate = 0; duplicate < 100; duplicate++) f.Signals[0].Changed();
            Assert.Single(f.Posted);
            f.Clock.Advance(1); await f.Tick();
        }
        Assert.InRange(f.Reads, 4, 5);
    }

    [Fact]
    public async Task QuietJournalIsVerifiedWithoutRelyingOnTimestampOrFileSize()
    {
        using var f = new Fixture(); await f.Settle();
        f.Clock.Advance(299); await f.Tick(); Assert.Equal(1, f.Reads);
        f.Clock.Advance(1); await f.Tick(); Assert.Equal(2, f.Reads);
    }

    [Fact]
    public async Task MinimizedOrBusyWindowDefersChangesAndRefreshesOnReturn()
    {
        using var f = new Fixture(); await f.Settle();
        f.Signals[0].Changed(); f.Clock.Advance(600);
        await f.Tick(visible: false); Assert.Equal(1, f.Reads);
        await f.Tick(available: false); Assert.Equal(1, f.Reads);
        await f.Tick(); Assert.Equal(2, f.Reads);
    }

    [Fact]
    public async Task PendingReadIsUniqueAndEventsArrivingDuringItAreNotLost()
    {
        using var f = new Fixture(); await f.Settle();
        var completion = new TaskCompletionSource<JournalRefreshOutcome>();
        f.Read = _ => completion.Task;
        f.Clock.Advance(300);
        var pending = f.Tick(); Assert.Equal(2, f.Reads);
        f.Signals[0].Changed(); await f.Tick(); Assert.Equal(2, f.Reads);
        completion.SetResult(JournalRefreshOutcome.Refreshed); await pending;
        f.Read = null; f.Clock.Advance(15); await f.Tick(); Assert.Equal(3, f.Reads);
    }

    [Fact]
    public async Task RestoringWindowStartsReconciliationBeforeFiveMinuteDeadlineWithoutAFileSignal()
    {
        using var f = new Fixture(); await f.Settle();
        f.Clock.Advance(20); await f.Tick(visible: false); Assert.Equal(1, f.Reads);
        f.Clock.Advance(40); await f.Tick(available: false); Assert.Equal(1, f.Reads);
        await f.Tick(); Assert.Equal(2, f.Reads);
        await f.Tick(); Assert.Equal(2, f.Reads);
    }

    [Fact]
    public async Task FailuresUseBackoffAndEventsDoNotCauseARetryStorm()
    {
        using var f = new Fixture(); await f.Settle(); f.Outcome = JournalRefreshOutcome.Failed;
        f.Clock.Advance(300); await f.Tick(); Assert.Equal(2, f.Reads);
        for (var second = 0; second < 14; second++)
        { f.Signals[0].Changed(); f.Clock.Advance(1); await f.Tick(); }
        Assert.Equal(2, f.Reads);
        f.Clock.Advance(1); await f.Tick(); Assert.Equal(3, f.Reads);
        f.Clock.Advance(29); await f.Tick(); Assert.Equal(3, f.Reads);
        f.Outcome = JournalRefreshOutcome.Refreshed; f.Clock.Advance(1); await f.Tick(); Assert.Equal(4, f.Reads);
    }

    [Fact]
    public async Task DisablingOrChangingSourceCancelsReadAndIgnoresOldNotifications()
    {
        using var f = new Fixture(); await f.Settle();
        var completion = new TaskCompletionSource<JournalRefreshOutcome>(); CancellationToken request = default;
        f.Read = token => { request = token; return completion.Task; };
        f.Clock.Advance(300); var pending = f.Tick();
        f.Monitor.Configure("/test/other.mdb", false); Assert.True(request.IsCancellationRequested);
        Assert.True(f.Subscriptions[0].Disposed);
        completion.SetResult(JournalRefreshOutcome.Refreshed); await pending;
        f.Read = null; f.Signals[0].Changed(); f.Clock.Advance(600); await f.Tick(); Assert.Equal(2, f.Reads);
        f.Monitor.Configure("/test/other.mdb", true); await f.Tick();
        f.Clock.Advance(15); await f.Tick(); Assert.Equal(3, f.Reads);
        f.Clock.Advance(15); f.Signals[0].Changed(); await f.Tick(); f.Clock.Advance(3); await f.Tick();
        Assert.Equal(3, f.Reads);
    }

    [Fact]
    public async Task NetworkWatcherFailureReattachesAndReconcilesAfterReconnect()
    {
        using var f = new Fixture(); await f.Settle();
        f.Signals[0].Error(); await f.Tick(); Assert.True(f.Subscriptions[0].Disposed);
        f.Clock.Advance(29); await f.Tick(); Assert.Single(f.Subscriptions);
        f.Clock.Advance(1); await f.Tick(); Assert.Equal(2, f.Subscriptions.Count);
        f.Clock.Advance(15); await f.Tick(); Assert.Equal(3, f.Reads);
    }

    [Fact]
    public async Task UnsupportedWatcherStillUsesReadVerificationAndUnexpectedErrorsAreContained()
    {
        var clock = new Clock(); var reads = 0;
        using var monitor = new JournalAutoRefresh(_ => { reads++; throw new IOException(); }, action => action(), clock,
            (_, _, _) => Task.FromException<IDisposable>(new IOException()));
        monitor.Configure("/test/OrderDB.mdb", true);
        await monitor.TickAsync(true, true); Assert.Equal(1, reads);
        clock.Advance(15); await monitor.TickAsync(true, true); Assert.Equal(2, reads);
        monitor.Dispose(); clock.Advance(600); await monitor.TickAsync(true, true); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task DemoHasNoWatcherOrBackgroundReads()
    {
        using var f = new Fixture(); f.Monitor.Configure(null, true); f.Clock.Advance(600);
        await f.Tick(); Assert.Empty(f.Subscriptions); Assert.Equal(0, f.Reads);
    }

    [Fact]
    public async Task ManualRefreshConsumesEarlierSignalsAndPostponesVerification()
    {
        using var f = new Fixture(); await f.Settle();
        f.Signals[0].Changed(); await f.Tick(available: false);
        f.Monitor.ManualReadStarted(7); f.Clock.Advance(300);
        await f.Tick(); Assert.Equal(1, f.Reads);
        f.Monitor.ManualReadCompleted(7, true);
        await f.Tick(); f.Clock.Advance(299); await f.Tick(); Assert.Equal(1, f.Reads);
        f.Clock.Advance(1); await f.Tick(); Assert.Equal(2, f.Reads);
    }

    [Fact]
    public async Task SignalsDuringManualReadSurviveAndOldCompletionsCannotAcknowledgeThem()
    {
        using var f = new Fixture(); await f.Settle();
        f.Monitor.ManualReadStarted(7);
        f.Monitor.ManualReadStarted(8);
        f.Signals[0].Changed(); await f.Tick();
        f.Monitor.ManualReadCompleted(7, true);
        f.Clock.Advance(30); await f.Tick(); Assert.Equal(1, f.Reads);
        f.Monitor.ManualReadCompleted(8, true);
        await f.Tick(); Assert.Equal(1, f.Reads);
        f.Clock.Advance(14); await f.Tick(); Assert.Equal(1, f.Reads);
        f.Clock.Advance(1); await f.Tick(); Assert.Equal(2, f.Reads);
    }

    [Fact]
    public async Task F5ReplacingBackgroundReadDoesNotTriggerAnotherImmediateRead()
    {
        using var f = new Fixture(); await f.Settle();
        var completion = new TaskCompletionSource<JournalRefreshOutcome>();
        f.Read = _ => completion.Task;
        f.Clock.Advance(300); var automatic = f.Tick(); Assert.Equal(2, f.Reads);
        f.Monitor.ManualReadStarted(7); f.Monitor.ManualReadCompleted(7, true);
        completion.SetResult(JournalRefreshOutcome.Deferred); await automatic; f.Read = null;
        await f.Tick(); f.Clock.Advance(15); await f.Tick(); Assert.Equal(2, f.Reads);
        f.Clock.Advance(285); await f.Tick(); Assert.Equal(3, f.Reads);
    }

    [Fact]
    public async Task FailedManualReadPreservesDirtyStateAndUsesBackoff()
    {
        using var f = new Fixture(); await f.Settle();
        f.Signals[0].Changed(); await f.Tick(available: false);
        f.Monitor.ManualReadStarted(7); f.Monitor.ManualReadCompleted(7, false);
        f.Clock.Advance(14); await f.Tick(); Assert.Equal(1, f.Reads);
        f.Clock.Advance(1); await f.Tick(); Assert.Equal(2, f.Reads);
    }
}
