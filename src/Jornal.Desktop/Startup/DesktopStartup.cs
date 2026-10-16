using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Jornal.Desktop.Startup;

public sealed class DesktopStartup
{
    private readonly Func<Window> _createMainWindow;
    private readonly Action<Window> _setMainWindow;
    private bool _closed;
    private bool _started;

    public SplashWindow Splash { get; } = new();

    public DesktopStartup(Func<Window> createMainWindow, Action<Window> setMainWindow)
    {
        _createMainWindow = createMainWindow;
        _setMainWindow = setMainWindow;
        Splash.Closed += (_, _) => _closed = true;
        // Сначала даём Avalonia обработать отрисовку заставки, затем строим рабочее окно.
        Splash.Opened += (_, _) => Dispatcher.UIThread.Post(OpenMainWindow, DispatcherPriority.Background);
    }

    private void OpenMainWindow()
    {
        if (_closed || _started) return;
        _started = true;
        Window? main = null;
        try
        {
            main = _createMainWindow();
            // Подменяем MainWindow до закрытия заставки, в том числе для OnMainWindowClose.
            _setMainWindow(main);
            main.Show();
            Splash.Close();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Journal startup failed: {0}", exception);
            _setMainWindow(Splash);
            main?.Close();
            Splash.ShowStartupError();
        }
    }
}
