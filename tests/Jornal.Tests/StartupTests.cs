using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Desktop.Startup;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class StartupTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplashShowsCompanyLogoVersionAndSmallEmbeddedArtworkInBothThemes(bool dark)
    {
        var app = Application.Current!;
        var previousTheme = app.RequestedThemeVariant;
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var window = new SplashWindow();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.Equal(new Size(640, 360), window.ClientSize);
            Assert.False(window.Topmost);
            var artwork = Assert.IsType<Bitmap>(window.FindControl<Image>("ArtworkImage")!.Source);
            Assert.Equal(new PixelSize(1280, 720), artwork.PixelSize);
            Assert.Equal(new PixelSize(136, 82), Assert.IsType<Bitmap>(window.FindControl<Image>("CompanyLogoImage")!.Source).PixelSize);
            Assert.StartsWith("Версия ", window.FindControl<TextBlock>("VersionLabel")!.Text);
            Assert.DoesNotContain('+', window.FindControl<TextBlock>("VersionLabel")!.Text!);
            using var stream = AssetLoader.Open(StartupArtwork.Current.Background);
            Assert.True(stream.Length <= 160 * 1024, $"Слишком большой фон: {stream.Length} байт");
            foreach (var name in new[] { "CompanyLogoImage", "VersionLabel", "LaunchStatus", "CloseSplashButton" })
            {
                var control = window.FindControl<Control>(name)!;
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(control.IsEffectivelyVisible);
                Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 641 && bounds.Bottom <= 361, $"{name}: {bounds}");
            }
            SavePreview(window, $"splash-{(dark ? "dark" : "light")}");
        }
        finally { window.Close(); app.RequestedThemeVariant = previousTheme; }
    }

    [AvaloniaFact]
    public void MissingBackgroundKeepsBrandingAndDoesNotPreventOpeningTheWindow()
    {
        var window = new SplashWindow(new(new Uri("avares://Jornal.Desktop/Assets/Startup/missing.jpg")));
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(window.FindControl<Image>("ArtworkImage")!.Source);
            Assert.NotNull(window.FindControl<Image>("CompanyLogoImage")!.Source);
            Assert.NotEmpty(window.FindControl<TextBlock>("VersionLabel")!.Text!);
            SavePreview(window, "splash-no-artwork");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void StartupDefersMainConstructionAndReplacesLifetimeWindowBeforeClosingSplash()
    {
        Window? lifetimeWindow = null;
        var constructions = 0;
        var main = new Window();
        var startup = new DesktopStartup(() => { constructions++; return main; }, window => lifetimeWindow = window);
        lifetimeWindow = startup.Splash;
        var mainWasVisibleAtSplashClose = false;
        startup.Splash.Closed += (_, _) => mainWasVisibleAtSplashClose = main.IsVisible && ReferenceEquals(lifetimeWindow, main);
        try
        {
            startup.Splash.Show();
            Assert.Equal(0, constructions);
            Assert.Same(startup.Splash, lifetimeWindow);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, constructions);
            Assert.Same(main, lifetimeWindow);
            Assert.True(mainWasVisibleAtSplashClose);
            Assert.False(startup.Splash.IsVisible);
            Assert.Null(startup.Splash.FindControl<Image>("ArtworkImage")!.Source);
        }
        finally { main.Close(); startup.Splash.Close(); }
    }

    [AvaloniaFact]
    public void ClosingSplashBeforeQueuedStartupCancelsLaunch()
    {
        var constructions = 0;
        var startup = new DesktopStartup(() => { constructions++; return new Window(); }, _ => { });
        startup.Splash.Show();
        startup.Splash.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(startup.Splash.IsVisible);
        Assert.Equal(0, constructions);
    }

    [AvaloniaFact]
    public void EscapeClosesSplash()
    {
        var window = new SplashWindow();
        window.Show();
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void InitializationErrorStopsLoadingKeepsBrandingAndOffersClose()
    {
        Window? lifetimeWindow = null;
        var startup = new DesktopStartup(() => throw new IOException("Synthetic startup failure"), window => lifetimeWindow = window);
        try
        {
            startup.Splash.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(startup.Splash, lifetimeWindow);
            Assert.True(startup.Splash.IsVisible);
            Assert.True(startup.Splash.FindControl<Control>("StartupErrorPanel")!.IsVisible);
            Assert.False(startup.Splash.FindControl<Control>("LaunchStatus")!.IsVisible);
            Assert.NotNull(startup.Splash.FindControl<Image>("CompanyLogoImage")!.Source);
            Assert.True(startup.Splash.FindControl<TextBlock>("VersionLabel")!.IsEffectivelyVisible);
            SavePreview(startup.Splash, "splash-startup-error");
            var button = startup.Splash.FindControl<Button>("CloseSplashButton")!;
            var point = button.TranslatePoint(new Point(16, 16), startup.Splash)!.Value;
            startup.Splash.MouseDown(point, MouseButton.Left);
            startup.Splash.MouseUp(point, MouseButton.Left);
            Assert.False(startup.Splash.IsVisible);
        }
        finally { startup.Splash.Close(); }
    }

    [AvaloniaFact]
    public async Task SlowOrUnavailableDatabaseDoesNotLeaveSplashCoveringTheJournal()
    {
        var reader = new PendingReader();
        var model = new MainWindowViewModel(reader);
        var main = new MainWindow { DataContext = model };
        var startup = new DesktopStartup(() => main, _ => { });
        try
        {
            startup.Splash.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(startup.Splash.IsVisible);
            Assert.True(main.IsVisible);
            Assert.True(model.IsLoadingOrders);
            reader.Completion.SetException(new OrderReadException("Сетевая база недоступна"));
            await model.ReloadCommand.ExecutionTask!;
            Assert.True(main.IsVisible);
            Assert.Contains("Сетевая база недоступна", model.ErrorMessage);
            Assert.False(model.IsLoadingOrders);
        }
        finally { main.Close(); startup.Splash.Close(); }
    }

    private static void SavePreview(SplashWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 720), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }

    private sealed class PendingReader : IOrderReader
    {
        public TaskCompletionSource<IReadOnlyList<OrderSummary>> Completion { get; } = new();
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Completion.Task.WaitAsync(cancellationToken);
    }
}
