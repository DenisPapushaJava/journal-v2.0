using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Jornal.Desktop.Controls;

public sealed class HeaderClock : TextBlock
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");
    private readonly TimeProvider _timeProvider;
    private readonly DispatcherTimer _timer = new();

    public HeaderClock() : this(TimeProvider.System) { }

    public HeaderClock(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _timer.Tick += (_, _) => Refresh();
    }

    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        var now = _timeProvider.GetLocalNow();
        var weekday = Russian.TextInfo.ToTitleCase(now.ToString("ddd", Russian));
        Text = now.ToString("HH:mm", Russian) + ", " + weekday + ", " + now.ToString("d MMMM", Russian);
        // Следующая граница минуты: без секунд на экране и без постоянного секундного опроса.
        var remaining = TimeSpan.TicksPerMinute - now.TimeOfDay.Ticks % TimeSpan.TicksPerMinute;
        _timer.Interval = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMillisecond * 100, remaining));
    }
}
