using Avalonia;

namespace Jornal.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var diagnostics = new Diagnostics.DiagnosticRuntime();
        using var installerGuard = OperatingSystem.IsWindows()
            ? new Mutex(initiallyOwned: false, @"Local\DenisPapusha.Journal.Running") : null;
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception exception)
        {
            Jornal.Infrastructure.Diagnostics.AppDiagnostics.Capture(Jornal.Infrastructure.Diagnostics.DiagnosticOperation.Unexpected, exception, fatal: true);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
