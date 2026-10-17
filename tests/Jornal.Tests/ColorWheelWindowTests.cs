using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ColorWheelWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task WheelPopupFitsAndPointerKeyboardAndCodeStaySynchronizedUntilExplicitConfirmation(bool dark, bool nearScreenEdge)
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store);
        var selected = model.Orders[0]; model.UpdateSelection([selected], selected); model.ResultText = "Письмо";
        var form = model.CreateRowColorForm()!;
        form.Code = "#FF9DFF"; var original = await store.ReadAsync(TestContext.Current.CancellationToken);
        var window = new RowColorWindow { DataContext = form, Width = 500, Height = 440, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); Assert.Equal(-25089, form.Argb);
            if (nearScreenEdge && window.Screens.Primary is { } screen)
                window.Position = new(screen.WorkingArea.Right - 504, screen.WorkingArea.Bottom - 444);
            var more = window.FindControl<Button>("MoreRowColors")!; Click(more);
            var flyout = Assert.IsType<Flyout>(more.Flyout); Assert.True(flyout.IsOpen);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var wheel = content.GetVisualDescendants().OfType<ColorWheel>().Single();
            var root = TopLevel.GetTopLevel(wheel)!;
            Assert.True(wheel.Bounds.Width >= 240 && wheel.Bounds.Height >= 240); InBounds(root, wheel);
            InBounds(root, content);
            Assert.Equal(new Size(500, 440), window.ClientSize);
            InBounds(window, window.FindControl<Border>("RowColorPreview")!);
            InBounds(window, window.FindControl<JournalButton>("ConfirmRowColorButton")!);
            Assert.Equal(-25089, form.Argb); Assert.Equal(form.SpectrumColor, wheel.HsvColor);
            // Клик справа на кольце: красный оттенок; центр треугольника: S=1/2, V=2/3.
            ClickAt(wheel, new(wheel.Bounds.Width * .94, wheel.Bounds.Height / 2));
            Assert.InRange(form.SpectrumColor.H, 0, .01);
            ClickAt(wheel, new(wheel.Bounds.Width / 2, wheel.Bounds.Height / 2));
            Assert.InRange(form.SpectrumColor.S, .49, .51); Assert.InRange(form.SpectrumColor.V, .66, .68);
            var hue = form.SpectrumColor.H; wheel.Focus();
            root.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null); Pump();
            Assert.True(form.SpectrumColor.H > hue);
            var saturation = form.SpectrumColor.S;
            root.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.ArrowLeft, null); Pump();
            Assert.True(form.SpectrumColor.S < saturation);
            var value = form.SpectrumColor.V;
            root.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Pump();
            Assert.True(form.SpectrumColor.V < value);
            form.Code = "#123456"; Pump(); Assert.Equal(Color.Parse("#123456"), wheel.HsvColor.ToRgb());
            form.SpectrumColor = new(1, 30, .65, .8); Pump();
            Assert.Equal(original, await store.ReadAsync(TestContext.Current.CancellationToken));
            Save(root, $"row-wheel-{dark}-{nearScreenEdge}"); Save(window, $"row-wheel-dialog-{dark}-{nearScreenEdge}");
            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(flyout.IsOpen); Assert.True(window.IsVisible);
            var expected = form.Argb;
            await form.ConfirmCommand.ExecuteAsync(null); Pump(); Assert.False(window.IsVisible);
            var after = await store.ReadAsync(TestContext.Current.CancellationToken);
            foreach (var row in after) Assert.Equal(original.Single(old => old.Id == row.Id) with { Color = row.Id == selected.Id ? expected : row.Color }, row);
            Assert.Equal("Письмо", model.ResultText);
        }
        finally { window.FindControl<Button>("MoreRowColors")?.Flyout?.Hide(); window.Close(); }
    }
    [AvaloniaFact]
    public void DragOutsideTheTriangleClampsAndEmptySpaceDoesNotChangeTheColor()
    {
        var wheel = new ColorWheel { Width = 244, Height = 244, HsvColor = new(1, 0, .5, .6) };
        var window = new Window { Content = wheel, Width = 300, Height = 300 };
        try
        {
            window.Show(); Pump(); var before = wheel.HsvColor;
            ClickAt(wheel, new(2, 2)); Assert.Equal(before, wheel.HsvColor);
            var root = TopLevel.GetTopLevel(wheel)!; var center = wheel.TranslatePoint(new(122, 122), root)!.Value;
            root.MouseDown(center, MouseButton.Left); root.MouseMove(center + new Vector(500, 500)); root.MouseUp(center + new Vector(500, 500), MouseButton.Left); Pump();
            Assert.InRange(wheel.HsvColor.S, 0, 1); Assert.InRange(wheel.HsvColor.V, 0, 1); Assert.Equal(1, wheel.HsvColor.A);
            var stopped = wheel.HsvColor; root.MouseMove(center); Pump(); Assert.Equal(stopped, wheel.HsvColor);
        }
        finally { window.Close(); }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Control control) => ClickAt(control, new(control.Bounds.Width / 2, control.Bounds.Height / 2));
    private static void ClickAt(Control control, Point local)
    {
        var root = TopLevel.GetTopLevel(control)!; var point = control.TranslatePoint(local, root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void InBounds(TopLevel root, Control control)
    {
        var rect = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(root)!.Value);
        Assert.True(rect.Left >= 0 && rect.Top >= 0 && rect.Right <= root.ClientSize.Width + 1 && rect.Bottom <= root.ClientSize.Height + 1, $"{control.Name}: {rect}, {root.ClientSize}");
    }
    private static void Save(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_COLOR_WHEEL_PROBE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); using var image = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1)); image.Render(root);
        image.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
