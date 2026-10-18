using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Xunit;

namespace Jornal.Tests;

public sealed class HeaderClockTests
{
    [AvaloniaFact]
    public async Task ClockUsesLocalTimeAndRussianInflectionAcrossClockChangesAndStopsWhenDetached()
    {
        var previous = CultureInfo.CurrentCulture;
        var source = new Clock { Utc = new(2026, 10, 18, 11, 38, 59, 950, TimeSpan.Zero) };
        var clock = new HeaderClock(source);
        var window = new Window { Content = clock, Width = 300, Height = 100 };
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("14:38   Вс 18 октября", Text());
            source.Utc = new(2026, 12, 31, 20, 59, 59, 950, TimeSpan.Zero);
            await UntilText("23:59   Чт 31 декабря");
            source.Utc = new(2026, 12, 31, 21, 0, 59, 950, TimeSpan.Zero);
            await UntilText("00:00   Пт 1 января");

            window.Content = null; Dispatcher.UIThread.RunJobs();
            var reads = source.Reads;
            await Task.Delay(200, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs();
            Assert.Equal(reads, source.Reads);
            source.Utc = new(2027, 3, 8, 11, 38, 0, TimeSpan.Zero);
            window.Content = clock; Dispatcher.UIThread.RunJobs();
            Assert.Equal("14:38   Пн 8 марта", Text());
        }
        finally { window.Close(); CultureInfo.CurrentCulture = previous; }

        string Text() => string.Concat(clock.Inlines!.OfType<Run>().Select(run => run.Text));

        async Task UntilText(string expected)
        {
            var timer = Stopwatch.StartNew();
            while (Text() != expected && timer.Elapsed < TimeSpan.FromSeconds(3))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(expected, Text());
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; }
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() { Reads++; return Utc; }
        public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone("Journal.Clock.Tests", TimeSpan.FromHours(3), "Test", "Test");
    }
}
