using Avalonia;

namespace Jornal.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var installerGuard = OperatingSystem.IsWindows()
            ? new Mutex(initiallyOwned: false, @"Local\DenisPapusha.Journal.Running") : null;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
