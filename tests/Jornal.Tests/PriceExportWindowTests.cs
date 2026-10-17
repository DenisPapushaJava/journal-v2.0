using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

public sealed class PriceExportWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, 520, 430)]
    [InlineData(true, 520, 430)]
    [InlineData(false, 440, 340)]
    [InlineData(true, 440, 340)]
    public async Task ExportFromPricesShowsUsedMachinesAndKeepsSelectionAndCommandsReadable(bool dark, int width, int height)
    {
        var store = new DemoOrderStore();
        var machine = MachineCatalogTests.Machine("Гидроабразивная резка — " + new string('Д', 100));
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), TestContext.Current.CancellationToken);
        var prices = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "Демо");
        var owner = new PriceEditorWindow { DataContext = prices, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            owner.Show(); Pump();
            Assert.True(prices.CanExport);
            var open = owner.FindControl<JournalButton>("ExportPricesButton")!;
            InBounds(owner, open); Assert.True(open.IsEffectivelyEnabled);
            Click(owner, open); Pump();
            var window = Assert.Single(owner.OwnedWindows.OfType<PriceExportWindow>());
            var form = (PriceExportViewModel)window.DataContext!;
            window.Width = width; window.Height = height; Pump();
            Assert.Equal(5, form.Items.Count); Assert.All(form.Items, item => Assert.True(item.IsSelected));
            Assert.DoesNotContain(form.Items, item => item.Name == "Прочие прайсы");
            Assert.Equal(5, window.GetVisualDescendants().OfType<CheckBox>().Count());
            Assert.True(prices.IsExportDialogOpen); Assert.False(prices.CanExport); Assert.False(prices.CanClose);
            owner.Close(); Assert.True(owner.IsVisible); Assert.True(window.IsVisible);
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible)) InBounds(window, button);
            Save(window, $"price-export-{width}-{dark}");
            foreach (var checkbox in window.GetVisualDescendants().OfType<CheckBox>()) checkbox.IsChecked = false;
            Pump(); Assert.False(form.CanExport); Assert.False(window.FindControl<JournalButton>("SaveExportButton")!.IsEffectivelyEnabled);
            form.Items[0].IsSelected = true; Pump(); Assert.True(form.CanExport);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(window.IsVisible); Assert.False(prices.IsExportDialogOpen); Assert.True(prices.CanExport);
            prices.BeginEditCommand.Execute(null); Pump(); Assert.False(prices.CanExport); Assert.Null(prices.CreateExportForm());
            prices.CancelEditCommand.Execute(null); Assert.True(prices.CanExport);
        }
        finally
        { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); prices.CancelEditCommand.Execute(null); owner.Close(); }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, Control button)
    {
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void InBounds(Window window, Control control)
    {
        var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ClientSize.Width + 1 && bounds.Bottom <= window.ClientSize.Height + 1,
            $"{control.Name}: {bounds}");
    }
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_PRICE_EXPORT_PROBE") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); using var image = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); image.Render(window);
        image.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
