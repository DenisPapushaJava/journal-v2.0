using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalPointerRefreshTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticReadBetweenPressAndReleaseCannotCancelTheResultClick(bool dark)
    {
        var (window, model, reader, monitor) = await Open(dark);
        try
        {
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.SelectAll(); Pump();
            var button = window.FindControl<JournalButton>("AddResultButton")!;
            var point = Center(window, button);
            window.MouseMove(point); await Reset(monitor);
            var before = reader.FullCalls;
            // Тик выполняется после штатного Button.OnPointerPressed, до MouseUp.
            EventHandler<PointerPressedEventArgs> tick = (_, _) => Tick(window);
            window.AddHandler(InputElement.PointerPressedEvent, tick, RoutingStrategies.Bubble, true);
            try { window.MouseDown(point, MouseButton.Left); }
            finally { window.RemoveHandler(InputElement.PointerPressedEvent, tick); }
            Assert.True(button.IsPressed);
            Assert.Equal(before, reader.FullCalls);
            Assert.True(model.CanAddResult);
            window.MouseUp(point, MouseButton.Left); Pump();
            Assert.NotEmpty(model.ResultText);
            var result = model.ResultText;
            Tick(window); Pump();
            Assert.Equal(before + 1, reader.FullCalls);
            Assert.Equal(result, model.ResultText);
            Assert.Single(table.SelectedItems);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ResizeDefersReadingUntilReleaseAndCancelledCaptureCanRecoverOnPointerMove()
    {
        var (window, _, reader, monitor) = await Open(false);
        try
        {
            var splitter = window.FindControl<GridSplitter>("FirstPanelSplitter")!;
            var point = Center(window, splitter);
            window.MouseMove(point); await Reset(monitor);
            var before = reader.FullCalls;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(0, 8), RawInputModifiers.LeftMouseButton);
            Tick(window); Assert.Equal(before, reader.FullCalls);
            window.MouseUp(point + new Vector(0, 8), MouseButton.Left);
            Tick(window); Assert.Equal(before + 1, reader.FullCalls);

            var button = window.FindControl<JournalButton>("AddResultButton")!;
            window.FindControl<DataGrid>("OrdersGrid")!.SelectAll(); Pump();
            point = Center(window, button); window.MouseMove(point); await Reset(monitor);
            IPointer? pointer = null;
            EventHandler<PointerPressedEventArgs> capture = (_, e) => pointer = e.Pointer;
            window.AddHandler(InputElement.PointerPressedEvent, capture, RoutingStrategies.Tunnel, true);
            try { window.MouseDown(point, MouseButton.Left); }
            finally { window.RemoveHandler(InputElement.PointerPressedEvent, capture); }
            Tick(window); Assert.Equal(before + 1, reader.FullCalls);
            Assert.NotNull(pointer); pointer.Capture(null);
            // Например, отпускание произошло вне захвата. Следующее движение
            // с отпущенными кнопками не должно оставить вечную паузу обновления.
            window.MouseMove(point, RawInputModifiers.None);
            Tick(window); Assert.Equal(before + 2, reader.FullCalls);
            window.MouseUp(point, MouseButton.Left);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DeactivationReleasesThePointerGuardForVisibleInactiveWindow()
    {
        var (window, _, reader, monitor) = await Open(false);
        try
        {
            var splitter = window.FindControl<GridSplitter>("FirstPanelSplitter")!;
            var point = Center(window, splitter);
            window.Activate(); window.MouseMove(point); await Reset(monitor);
            var before = reader.FullCalls;
            window.MouseDown(point, MouseButton.Left);
            Tick(window); Assert.Equal(before, reader.FullCalls);
            // То же уведомление, которое оконная система посылает при Alt+Tab.
            typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); Pump();
            Assert.False(window.IsActive); Assert.True(window.IsVisible);
            Tick(window); Assert.Equal(before + 1, reader.FullCalls);
            window.MouseUp(point, MouseButton.Left);
        }
        finally { window.Close(); }
    }

    private const string Path = "synthetic-pointer-refresh.mdb";
    private static readonly FieldInfo MonitorField = typeof(MainWindow).GetField("_journalAutoRefresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo TimerTick = typeof(MainWindow).GetMethod("OnJournalRefreshTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static async Task<(MainWindow Window, MainWindowViewModel Model, JournalPeriodTests.Reader Reader, JournalAutoRefresh Monitor)> Open(bool dark)
    {
        var reader = new JournalPeriodTests.Reader { Rows = [JournalPeriodTests.Row(42, DateOnly.FromDateTime(DateTime.Today))] };
        var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        model.InitializeSource(new(Path, reader));
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
        ((JournalAutoRefresh)MonitorField.GetValue(window)!).Dispose();
        var monitor = new JournalAutoRefresh(model.RefreshAutomaticallyAsync, action => action(), new JournalAutoRefreshTests.Clock(),
            (_, _, _) => Task.FromResult<IDisposable>(new Subscription()));
        MonitorField.SetValue(window, monitor); await Reset(monitor);
        return (window, model, reader, monitor);
    }
    private sealed class Subscription : IDisposable { public void Dispose() { } }
    private static Task Reset(JournalAutoRefresh monitor)
    {
        monitor.Configure(null, false); monitor.Configure(Path, true);
        // Подписка уже готова до жеста: её служебный сигнал не должен добавлять
        // искусственные три секунды debounce к следующей проверке.
        return monitor.TickAsync(visible: false, canRefresh: false);
    }
    private static void Tick(MainWindow window) => TimerTick.Invoke(window, [null, EventArgs.Empty]);
    private static Point Center(Window window, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
