using System.Threading.Channels;
using Avalonia.Headless.XUnit;
using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class AutomaticUpdateChecksTests
{
    [AvaloniaFact]
    public async Task StartupChecksOnceAndOnlyNotifiesUntilTheUserDecides()
    {
        await using var fixture = new Fixture();
        var model = fixture.Model;
        await fixture.StartAsync();
        await model.RunAutomaticChecksAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, fixture.Updater.Checks);
        Assert.False(fixture.Updater.Preview);
        Assert.True(model.HasUpdate);
        Assert.Contains("2026.10.0", model.NotificationText);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(TimeSpan.FromHours(6), await fixture.Clock.NextDelayAsync());
        Assert.Equal(1, fixture.Updater.Checks);
        Assert.Equal(0, fixture.Updater.Downloads);
        Assert.Equal(0, fixture.Updater.Restarts);
    }

    [AvaloniaFact]
    public async Task AutomaticChecksCanBeDisabledWithoutStartingATimer()
    {
        var clock = new ManualTimeProvider();
        var updater = new AppUpdatesTests.FakeUpdater();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var checking = new AppUpdatesViewModel(updater, clock).RunAutomaticChecksAsync(lifetime.Token);
        try { Assert.Equal(0, updater.Checks); Assert.Equal(0, clock.ActiveTimers); }
        finally { lifetime.Cancel(); await checking; }
    }

    [AvaloniaFact]
    public async Task TurningAutomaticChecksOffStopsTheTimerButKeepsManualChecksAndCanResume()
    {
        await using var fixture = new Fixture();
        fixture.Updater.Update = null;
        await fixture.StartAsync();
        fixture.Model.CheckAutomatically = false;
        await Task.Yield();
        Assert.Equal(0, fixture.Clock.ActiveTimers);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, fixture.Updater.Checks);
        await fixture.Model.CheckCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.Updater.Checks);
        fixture.Model.CheckAutomatically = true;
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(3, fixture.Updater.Checks);
        Assert.Equal(0, fixture.Updater.Downloads);
    }

    [AvaloniaFact]
    public async Task TurningAutomaticChecksOffCancelsAnInFlightQuietRequest()
    {
        await using var fixture = new Fixture();
        fixture.Updater.CheckHandler = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; };
        fixture.Run();
        Assert.True(fixture.Model.IsBusy);
        fixture.Model.CheckAutomatically = false;
        await Task.Yield();
        Assert.False(fixture.Model.IsBusy);
        Assert.False(fixture.Model.HasError);
        Assert.False(fixture.Model.HasUpdate);
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineAndTimeoutAreSilentAndRetryAfterSixHours(bool timeout)
    {
        await using var fixture = new Fixture();
        fixture.Updater.CheckError = timeout ? new TimeoutException() : new HttpRequestException("Offline");
        var initialStatus = fixture.Model.Status;
        await fixture.StartAsync();
        Assert.False(fixture.Model.HasError);
        Assert.Equal(initialStatus, fixture.Model.Status);
        Assert.False(fixture.Model.IsBusy);
        fixture.Clock.Advance(TimeSpan.FromHours(6) - TimeSpan.FromSeconds(1));
        Assert.Equal(1, fixture.Updater.Checks);
        fixture.Updater.CheckError = null;
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(2, fixture.Updater.Checks);
        Assert.True(fixture.Model.HasUpdate);
        Assert.Equal(0, fixture.Updater.Downloads);
        Assert.Equal(0, fixture.Updater.Restarts);
    }

    [AvaloniaFact]
    public async Task QuicklyReenablingDuringCancellationChecksAgainWithoutWaitingSixHours()
    {
        await using var fixture = new Fixture();
        fixture.Updater.CheckHandler = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; };
        fixture.Run();
        fixture.Model.CheckAutomatically = false;
        fixture.Model.CheckAutomatically = true;
        fixture.Updater.CheckHandler = null;
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(2, fixture.Updater.Checks);
        Assert.True(fixture.Model.HasUpdate);
        Assert.False(fixture.Model.HasError);
    }

    [AvaloniaFact]
    public async Task BackgroundFailurePreservesTheVisibleManualErrorAndCanRecover()
    {
        await using var fixture = new Fixture();
        fixture.Updater.CheckError = new HttpRequestException("Offline");
        await fixture.StartAsync();
        await fixture.Model.CheckCommand.ExecuteAsync(null);
        var error = fixture.Model.Error;
        var status = fixture.Model.Status;
        Assert.NotNull(error);
        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(error, fixture.Model.Error);
        Assert.Equal(status, fixture.Model.Status);
        fixture.Updater.CheckError = null;
        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.Clock.NextDelayAsync();
        Assert.True(fixture.Model.HasUpdate);
        Assert.False(fixture.Model.HasError);
    }

    [AvaloniaFact]
    public async Task ManualCheckPostponesTheNextAutomaticRequest()
    {
        await using var fixture = new Fixture();
        fixture.Updater.Update = null;
        await fixture.StartAsync();
        fixture.Clock.Advance(TimeSpan.FromHours(4));
        await fixture.Model.CheckCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.Updater.Checks);
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(TimeSpan.FromHours(4), await fixture.Clock.NextDelayAsync());
        Assert.Equal(2, fixture.Updater.Checks);
        fixture.Clock.Advance(TimeSpan.FromHours(4));
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(3, fixture.Updater.Checks);
    }

    [AvaloniaFact]
    public async Task LongPauseDoesNotAccumulateMissedRequests()
    {
        await using var fixture = new Fixture();
        fixture.Updater.Update = null;
        await fixture.StartAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(TimeSpan.FromHours(6), await fixture.Clock.NextDelayAsync());
        Assert.Equal(2, fixture.Updater.Checks);
        Assert.Equal(1, fixture.Clock.ActiveTimers);
        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(3, fixture.Updater.Checks);
    }

    [AvaloniaFact]
    public async Task TimerAndManualCheckNeverRunRequestsTogether()
    {
        await using var fixture = new Fixture();
        fixture.Updater.Update = null;
        await fixture.StartAsync();
        var completion = new TaskCompletionSource<AppUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Updater.CheckHandler = token => completion.Task.WaitAsync(token);
        var manual = fixture.Model.CheckCommand.ExecuteAsync(null);
        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(2, fixture.Updater.Checks);
        completion.SetResult(null);
        await manual;
        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.Clock.NextDelayAsync();
        Assert.Equal(3, fixture.Updater.Checks);
    }

    [AvaloniaFact]
    public async Task ManualCheckIsUnavailableDuringAnAutomaticRequest()
    {
        await using var fixture = new Fixture();
        var completion = new TaskCompletionSource<AppUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Updater.CheckHandler = token => completion.Task.WaitAsync(token);
        var starting = fixture.StartAsync();
        Assert.False(fixture.Model.CanCheck);
        await fixture.Model.CheckCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Updater.Checks);
        completion.SetResult(fixture.Updater.Update);
        await starting;
    }

    [AvaloniaFact]
    public async Task DownloadedPackageIsPreservedAcrossTimerTicks()
    {
        await using var fixture = new Fixture();
        await fixture.StartAsync();
        await fixture.Model.DownloadCommand.ExecuteAsync(null);
        fixture.Clock.Advance(TimeSpan.FromHours(6));
        await fixture.Clock.NextDelayAsync();
        Assert.True(fixture.Model.CanInstall);
        Assert.Equal(100, fixture.Model.DownloadProgress);
        Assert.Equal(1, fixture.Updater.Checks);
        Assert.Equal(1, fixture.Updater.Downloads);
        Assert.Equal(0, fixture.Updater.Restarts);
    }

    [AvaloniaFact]
    public async Task ClosingCancelsThePendingTimerAndPreventsFutureRequests()
    {
        await using var fixture = new Fixture();
        fixture.Updater.Update = null;
        await fixture.StartAsync();
        await fixture.StopAsync();
        Assert.Equal(0, fixture.Clock.ActiveTimers);
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(1, fixture.Updater.Checks);
    }

    [AvaloniaFact]
    public async Task ClosingDuringARequestCancelsItWithoutAnError()
    {
        await using var fixture = new Fixture();
        fixture.Updater.CheckHandler = async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        };
        fixture.Run();
        Assert.True(fixture.Model.IsBusy);
        await fixture.StopAsync();
        Assert.False(fixture.Model.IsBusy);
        Assert.False(fixture.Model.HasError);
        Assert.False(fixture.Model.HasUpdate);
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    [AvaloniaFact]
    public async Task ResultArrivingAfterClosingCannotSetTheBadge()
    {
        await using var fixture = new Fixture();
        var completion = new TaskCompletionSource<AppUpdate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Updater.CheckHandler = _ => completion.Task;
        fixture.Run();
        var stopping = fixture.StopAsync();
        completion.SetResult(fixture.Updater.Update);
        await stopping;
        Assert.False(fixture.Model.IsBusy);
        Assert.False(fixture.Model.HasError);
        Assert.False(fixture.Model.HasUpdate);
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private Task? _running;
        public AppUpdatesTests.FakeUpdater Updater { get; } = new();
        public ManualTimeProvider Clock { get; } = new();
        public AppUpdatesViewModel Model { get; }
        public Fixture() => Model = new(Updater, Clock) { CheckAutomatically = true };
        public void Run() => _running = Model.RunAutomaticChecksAsync(_lifetime.Token);
        public async Task StartAsync() { Run(); Assert.Equal(TimeSpan.FromHours(6), await Clock.NextDelayAsync()); }
        public async Task StopAsync() { _lifetime.Cancel(); if (_running is not null) await _running.WaitAsync(TimeSpan.FromSeconds(5)); }
        public async ValueTask DisposeAsync() { await StopAsync(); _lifetime.Dispose(); }
    }

    // Управляем только временем одноразовых таймеров Task.Delay, без ожидания реальных часов.
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly Channel<TimeSpan> _created = Channel.CreateUnbounded<TimeSpan>();
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_gate) return _timestamp; }
        public int ActiveTimers { get { lock (_gate) return _timers.Count; } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _created.Writer.TryWrite(dueTime);
            return timer;
        }
        public async Task<TimeSpan> NextDelayAsync() => await _created.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        public void Advance(TimeSpan amount)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _timestamp += amount.Ticks;
                due = _timers.Where(timer => timer.Deadline <= _timestamp).ToArray();
                foreach (var timer in due) _timers.Remove(timer);
            }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public long Deadline { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                lock (owner._gate)
                {
                    if (_disposed) return false;
                    owner._timers.Remove(this);
                    if (dueTime != Timeout.InfiniteTimeSpan)
                    { Deadline = owner._timestamp + dueTime.Ticks; owner._timers.Add(this); }
                    return true;
                }
            }
            public void Fire() { lock (owner._gate) { if (_disposed) return; } callback(state); }
            public void Dispose() { lock (owner._gate) { _disposed = true; owner._timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
