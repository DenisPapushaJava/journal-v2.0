using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TechnologistColorWheelTests
{
    [Fact]
    public void PreciseSelectionRetainsHsvPrecisionAndSynchronizesLegacyCodeAndSwatches()
    {
        var model = new MainWindowViewModel(new DemoOrderStore()) { UseTechnologistColor = true };
        var hsv = new HsvColor(1, 197.12345, .54321, .87654);
        model.TechnologistSpectrumColor = hsv;
        Assert.Equal(hsv, model.TechnologistSpectrumColor);
        var rgb = hsv.ToRgb(); var argb = unchecked((int)rgb.ToUInt32());
        Assert.Equal(argb, model.EffectiveNewOrderColor);
        Assert.Equal(argb.ToString(CultureInfo.InvariantCulture), model.TechnologistColorCode);
        model.SelectTechnologistColor(-8323200);
        Assert.Equal(Color.Parse("#80FF80"), model.TechnologistSpectrumColor.ToRgb());
        Assert.Equal(-8323200, Assert.Single(model.FrequentTechnologistColors.Concat(model.OtherTechnologistColors), option => option.IsSelected).Argb);
        model.TechnologistColorCode = unchecked((int)Color.Parse("#123456").ToUInt32()).ToString(CultureInfo.InvariantCulture);
        Assert.Equal(Color.Parse("#123456"), model.TechnologistSpectrumColor.ToRgb());
        model.TechnologistSpectrumColor = new(.2, 220, .5, .7);
        Assert.Equal(1, model.TechnologistSpectrumColor.A);
        model.TechnologistColorCode = unchecked((int)Color.Parse("#808080").ToUInt32()).ToString(CultureInfo.InvariantCulture);
        Assert.Equal(220, model.TechnologistSpectrumColor.H);
    }

    [Theory]
    [InlineData(OrderColors.None)] [InlineData(OrderColors.Work)]
    [InlineData(OrderColors.Ready)] [InlineData(OrderColors.Shipped)]
    public void AReservedWheelColorNeverReplacesTheSavedCandidate(int reserved)
    {
        var model = new MainWindowViewModel(new DemoOrderStore()) { UseTechnologistColor = true };
        model.SelectTechnologistColor(-8323200);
        model.TechnologistSpectrumColor = new(Color.FromUInt32(unchecked((uint)reserved)));
        Assert.True(model.HasTechnologistColorError);
        Assert.Equal(-8323200, model.CaptureNewOrderColor().ColorArgb);
        model.CommitTechnologistColorCode();
        Assert.False(model.HasTechnologistColorError);
        Assert.Equal(Color.Parse("#80FF80"), model.TechnologistSpectrumColor.ToRgb());
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task SettingsWheelFitsClosesSafelyAndPersistsOnlyThePersonalColor(bool dark, bool nearScreenEdge)
    {
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark });
        var store = new DemoOrderStore(); var history = await store.ReadAsync(TestContext.Current.CancellationToken);
        var model = new MainWindowViewModel(store, store); var owner = new MainWindow(preferences) { DataContext = model, Width = 1366, Height = 768 };
        Flyout? flyout = null;
        try
        {
            owner.Show(); Pump(); Click(owner.FindControl<Control>("SettingsButton")!);
            var settings = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            settings.Width = 800; settings.Height = 580; Pump();
            if (nearScreenEdge && settings.Screens.Primary is { } screen)
                settings.Position = new(screen.WorkingArea.Right - 804, screen.WorkingArea.Bottom - 584);
            var enabled = settings.FindDescendant<CheckBox>("UseTechnologistColorCheckBox")!;
            enabled.BringIntoView(); Pump();
            var button = settings.FindDescendant<Button>("MoreTechnologistColors")!;
            Assert.False(button.IsEffectivelyEnabled);
            Click(enabled); button.BringIntoView(); Pump(); InBounds(settings, button); Click(button);
            flyout = Assert.IsType<Flyout>(button.Flyout); Assert.True(flyout.IsOpen);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var wheel = content.GetVisualDescendants().OfType<ColorWheel>().Single();
            var root = TopLevel.GetTopLevel(wheel)!;
            InBounds(root, content); InBounds(root, wheel);
            var screenBounds = settings.Screens.Primary!.WorkingArea;
            var origin = root.PointToScreen(default);
            Assert.True(origin.X >= screenBounds.X && origin.Y >= screenBounds.Y);
            Assert.True(origin.X + root.ClientSize.Width * root.RenderScaling <= screenBounds.Right + 1);
            Assert.True(origin.Y + root.ClientSize.Height * root.RenderScaling <= screenBounds.Bottom + 1);
            Assert.True(wheel.IsFocused);
            model.TechnologistColorCode = "-8323200"; Pump(); Assert.Equal(Color.Parse("#80FF80"), wheel.HsvColor.ToRgb());
            var center = wheel.TranslatePoint(new(122, 122), root)!.Value;
            root.MouseDown(center, MouseButton.Left); root.MouseUp(center, MouseButton.Left); Pump();
            Assert.InRange(model.TechnologistSpectrumColor.S, .49, .51);
            var hue = model.TechnologistSpectrumColor.H;
            root.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); Pump();
            Assert.Equal(hue + 1, model.TechnologistSpectrumColor.H);
            var expected = model.TechnologistColorArgb;
            Assert.Equal(expected, model.CreateOrderForm()!.InitialColor);
            Assert.Equal(history, await store.ReadAsync(TestContext.Current.CancellationToken));
            Save(root, $"personal-wheel-{dark}-{nearScreenEdge}");
            model.TechnologistSpectrumColor = new(1, hue + 1, 0, 1); Pump();
            Assert.True(model.HasTechnologistColorError);
            Assert.Equal(expected, model.CaptureNewOrderColor().ColorArgb);
            InBounds(root, content);
            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(flyout.IsOpen); Assert.True(settings.IsVisible); Assert.True(button.IsFocused);
            Assert.False(model.HasTechnologistColorError);
            var preview = settings.FindDescendant<Border>("TechnologistColorPreview")!;
            preview.BringIntoView(); Pump(); settings.MouseMove(new(1, 1)); Pump();
            Assert.Equal(Color.FromUInt32(unchecked((uint)expected)), Assert.IsAssignableFrom<ISolidColorBrush>(preview.Background).Color);
            Save(settings, $"personal-settings-{dark}-{nearScreenEdge}");
            button.BringIntoView(); Pump(); Click(button); Assert.True(flyout.IsOpen);
            settings.Close(); Pump(); Assert.False(flyout.IsOpen);
            owner.Close(); Pump();
            Assert.Equal(expected, preferences.Value!.NewOrderColor.ColorArgb);
            Assert.True(preferences.Value.NewOrderColor.Enabled);
            var reopened = new MainWindowViewModel(store, store); var window = new MainWindow(preferences) { DataContext = reopened };
            try { window.Show(); Pump(); Assert.Equal(expected, reopened.CreateOrderForm()!.InitialColor); Assert.Equal(dark, reopened.IsDarkTheme); }
            finally { window.Close(); }
        }
        finally { flyout?.Hide(); foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); Pump(); }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void InBounds(TopLevel root, Control control)
    {
        var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(root)!.Value);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= root.ClientSize.Width + 1 && bounds.Bottom <= root.ClientSize.Height + 1);
    }
    private static void Save(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_COLOR_WHEEL_PROBE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); using var image = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1)); image.Render(root);
        image.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
