using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultFeedbackTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task SuccessExpiresAfterThreeSecondsWithoutMovingTheEditorOrChangingItsText(bool dark, bool clear)
    {
        using var fixture = new Fixture(dark);
        var input = fixture.Window.FindControl<TextBox>("ResultTextBox")!;
        var feedback = fixture.Window.FindControl<TextBlock>("ResultFeedback")!;
        var actions = fixture.Window.FindControl<StackPanel>("ResultActions")!;
        var editorBounds = input.Bounds;
        var actionsBounds = actions.Bounds;
        Assert.False(feedback.IsVisible);
        if (clear) fixture.Model.ClearResultCommand.Execute(null);
        else await fixture.Model.CopyResultAsync(new Clipboard());
        Pump();
        var text = fixture.Model.ResultText;
        var message = fixture.Model.ResultMessage;
        Assert.True(feedback.IsVisible);
        Assert.Equal(1, fixture.Clock.ActiveTimers);
        Assert.Equal(editorBounds, input.Bounds);
        Assert.Equal(actionsBounds, actions.Bounds);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(2999)); Pump();
        Assert.Equal(message, fixture.Model.ResultMessage);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1)); Pump();
        Assert.Equal(string.Empty, fixture.Model.ResultMessage);
        Assert.False(feedback.IsVisible);
        Assert.Equal(0, fixture.Clock.ActiveTimers);
        Assert.Equal(text, fixture.Model.ResultText);
        Assert.Equal(editorBounds, input.Bounds);
        Assert.Equal(actionsBounds, actions.Bounds);
    }

    [AvaloniaFact]
    public async Task RepeatedCopyKeepsOneTimerAndAnAlreadyQueuedExpiryCannotHideANewConfirmation()
    {
        using var fixture = new Fixture();
        for (var index = 0; index < 20; index++)
        {
            await fixture.Model.CopyResultAsync(new Clipboard());
            Assert.Equal(1, fixture.Clock.ActiveTimers);
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        // Предыдущее срабатывание уже в очереди UI, но ещё не выполнено.
        await fixture.Model.CopyResultAsync(new Clipboard()); Pump();
        Assert.Equal("Скопировано", fixture.Model.ResultMessage);
        Assert.Equal(1, fixture.Clock.ActiveTimers);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2)); Pump();
        Assert.Equal("Скопировано", fixture.Model.ResultMessage);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1)); Pump();
        Assert.Empty(fixture.Model.ResultMessage);
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task AnErrorCancelsExpiryEvenIfTheCallbackIsAlreadyQueuedAndOtherMessagesRemain(bool queued)
    {
        using var fixture = new Fixture();
        await fixture.Model.CopyResultAsync(new Clipboard());
        if (queued) fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        fixture.Model.HasResultError = true;
        fixture.Model.ResultMessage = "Заявки из результата изменились. Проверьте подготовленный текст.";
        Pump(); fixture.Clock.Advance(TimeSpan.FromSeconds(10)); Pump();
        Assert.True(fixture.Model.HasResultError);
        Assert.Contains("изменились", fixture.Model.ResultMessage);
        Assert.Equal(0, fixture.Clock.ActiveTimers);
        fixture.Model.HasResultError = false;
        foreach (var message in new[] { "Копирование…", "Сформировано заявок: 3", "Выберите заявки" })
        {
            fixture.Model.ResultMessage = message;
            fixture.Clock.Advance(TimeSpan.FromSeconds(10)); Pump();
            Assert.Equal(message, fixture.Model.ResultMessage);
            Assert.Equal(0, fixture.Clock.ActiveTimers);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task ClosingOrChangingTheModelReleasesTheTimerAndIgnoresAnOldQueuedCallback(bool close, bool queued)
    {
        using var fixture = new Fixture();
        await fixture.Model.CopyResultAsync(new Clipboard());
        if (queued) fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        if (close)
        {
            fixture.Window.Close();
            Assert.Equal(0, fixture.Clock.ActiveTimers);
            fixture.Clock.Advance(TimeSpan.FromSeconds(10)); Pump();
            Assert.Equal("Скопировано", fixture.Model.ResultMessage);
            fixture.Model.ClearResultCommand.Execute(null); Pump();
            Assert.Equal(0, fixture.Clock.ActiveTimers);
        }
        else
        {
            var replacement = new MainWindowViewModel(new DemoOrderStore()) { ResultText = "Новый текст" };
            fixture.Window.DataContext = replacement; Pump();
            Assert.Equal(0, fixture.Clock.ActiveTimers);
            Assert.Equal("Скопировано", fixture.Model.ResultMessage);
            await replacement.CopyResultAsync(new Clipboard());
            fixture.Model.ClearResultCommand.Execute(null); Pump();
            Assert.Equal(1, fixture.Clock.ActiveTimers);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(2999)); Pump();
            Assert.Equal("Скопировано", replacement.ResultMessage);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1)); Pump();
            Assert.Empty(replacement.ResultMessage);
            Assert.Equal("Новый текст", replacement.ResultText);
            Assert.Equal(0, fixture.Clock.ActiveTimers);
        }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class Fixture : IDisposable
    {
        public Clock Clock { get; } = new();
        public MainWindowViewModel Model { get; } = new(new DemoOrderStore());
        public MainWindow Window { get; }
        public Fixture(bool dark = false)
        {
            Model.IsDarkTheme = dark;
            Window = new(Clock, (_, _, _) => Task.FromResult<IDisposable>(new Subscription()))
                { DataContext = Model, Width = 1280, Height = 720 };
            Window.Show(); Pump();
            Model.ResultText = "Ручной комментарий\nТекст для письма"; Pump();
        }
        public void Dispose() { Window.Close(); Pump(); Assert.Equal(0, Clock.ActiveTimers); }
    }
    private sealed class Subscription : IDisposable { public void Dispose() { } }
    private sealed class Clipboard : IResultClipboard { public Task WriteAsync(ResultClipboardContent content) => Task.CompletedTask; }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        private readonly HashSet<ManualTimer> _timers = [];
        public int ActiveTimers => _timers.Count;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            _ticks += elapsed.Ticks;
            foreach (var timer in _timers.Where(timer => timer.Deadline <= _ticks).ToArray()) timer.Fire();
        }
        private sealed class ManualTimer(Clock owner, TimerCallback callback, object? state) : ITimer
        {
            private TimerCallback? _callback = callback;
            private object? _state = state;
            public long Deadline { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                if (_callback is null) return false;
                owner._timers.Remove(this);
                Deadline = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._ticks + dueTime.Ticks;
                if (dueTime != Timeout.InfiniteTimeSpan) owner._timers.Add(this);
                return true;
            }
            public void Fire() { Deadline = long.MaxValue; _callback?.Invoke(_state); }
            public void Dispose() { owner._timers.Remove(this); _callback = null; _state = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
