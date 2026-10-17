using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class MissingTariffWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTariffCardAndFooterRemainReadableAndAllowRecalculation(bool dark)
    {
        using var fixture = new LocalJournalFixture();
        var (_, _, form) = await MissingTariffTests.BatchAsync(fixture);
        var window = new NewOrderWindow { DataContext = form, Width = 1060, Height = 660,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            await form.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<JournalButton>("SaveButton")!;
            var calculate = window.FindControl<JournalButton>("CalculatePriceButton")!;
            Assert.False(save.IsEffectivelyEnabled); Assert.True(calculate.IsEffectivelyEnabled);
            var error = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == form.SaveError);
            var point = error.TranslatePoint(default, window)!.Value;
            Assert.True(point.X >= 0 && point.Y >= 0);
            Assert.True(point.X + error.Bounds.Width <= window.ClientSize.Width + 1);
            Assert.True(point.Y + error.Bounds.Height <= window.ClientSize.Height + 1);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(control => ReferenceEquals(control.ItemsSource, form.BatchItems));
            list.ScrollIntoView(form.BatchItems[1]); Dispatcher.UIThread.RunJobs();
            var reason = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.IsEffectivelyVisible && block.Text == form.BatchItems[1].Form.PriceError);
            var reasonPoint = reason.TranslatePoint(default, window)!.Value;
            Assert.True(reasonPoint.Y >= 0 && reasonPoint.Y + reason.Bounds.Height < point.Y);
            var directory = Environment.GetEnvironmentVariable("JOURNAL_WINDOW_SMOKE_OUTPUT");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), new Vector(96, 96));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory, $"missing-tariff-{dark}.png"), new PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); }
    }
}
