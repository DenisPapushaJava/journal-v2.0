using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class ManualMdbWindowTests
{
    [AvaloniaTheory]
    [InlineData(1060, 600, false)]
    [InlineData(1060, 600, true)]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    public async Task DatabaseFormShowsPriceAndKeepsActionsVisibleWhenTheTariffChanges(int width, int height, bool dark)
    {
        var creator = new ManualMdbCreationTests.PricedCreator();
        var model = ManualMdbCreationTests.Filled(creator);
        var window = new NewOrderWindow { DataContext = model, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var calculate = window.FindControl<JournalButton>("CalculatePriceButton")!;
            Assert.True(calculate.IsVisible); Click(window, calculate);
            await model.CalculatePriceCommand.ExecutionTask!; Dispatcher.UIThread.RunJobs();
            Assert.Equal("977 ₽", model.PriceText); Assert.Contains("MDB", window.Title);
            creator.Rate = 1;
            Click(window, window.FindControl<JournalButton>("SaveButton")!);
            await model.SaveCommand.ExecutionTask!; Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible); Assert.Equal("57 ₽", model.PriceText); Assert.Equal(0, creator.Created);
            foreach (var name in new[] { "SaveButton", "CancelButton", "CalculatePriceButton" })
            {
                var control = window.FindControl<Control>(name)!;
                var rect = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(rect.Top >= 0 && rect.Bottom <= height + 1 && rect.Right <= width + 1, $"{name}: {rect}");
            }
            if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
                bitmap.Render(window); bitmap.Save(Path.Combine(dir, $"mdb-create-{width}-{(dark ? "dark" : "light")}.png"), new PngBitmapEncoderOptions());
            }
            Click(window, window.FindControl<JournalButton>("SaveButton")!);
            await model.SaveCommand.ExecutionTask!; Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsSaved); Assert.False(window.IsVisible); Assert.Equal(1, creator.Created);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
}
