using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(Jornal.Tests.TestAppBuilder))]

namespace Jornal.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Desktop.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
