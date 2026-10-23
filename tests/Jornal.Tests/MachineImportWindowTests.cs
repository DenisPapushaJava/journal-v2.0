using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Pricing;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineImportWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditFromPricesKeepsTheWorkingKeyAndSupportsCancelAndSave(bool dark)
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine));
        var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "Демо", PriceStorageKind.Session);
        var owner = new PriceEditorWindow { DataContext = model, Width = 840, Height = 560, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            owner.Show(); Pump(); Assert.False(model.IsCustomMachine);
            Assert.NotNull(model.CreateMachineImportForm());
            await model.ShowCreatedMachineAsync(machine); Pump();
            var open = owner.FindControl<JournalButton>("EditMachineImportButton")!;
            Assert.True(open.IsEffectivelyVisible); Assert.True(open.IsEffectivelyEnabled);
            Click(owner, open);
            var window = Assert.Single(owner.OwnedWindows.OfType<MachineImportWindow>());
            var form = (MachineImportViewModel)window.DataContext!;
            Assert.Equal(machine.Name, form.Name);
            var name = window.FindControl<TextBox>("MachineDisplayNameInput")!;
            Assert.Equal(machine.Name, name.Text); name.Text = "Гидроабразив № 2"; Pump();
            var combo = window.FindControl<ComboBox>("MachineImportProfileInput")!;
            Assert.Equal(4, combo.ItemCount);
            Assert.True(form.CanSave);
            combo.SelectedIndex = 1; Pump(); Assert.True(form.CanSave);
            window.Close(); Pump(); Assert.True(window.IsVisible); Assert.True(form.ConfirmDiscard);
            var keep = window.GetVisualDescendants().OfType<JournalButton>().Single(button => button.Label == "Продолжить");
            Click(window, keep); Assert.False(form.ConfirmDiscard);
            foreach (var control in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
            {
                var point = control.TranslatePoint(default, window)!.Value;
                Assert.InRange(point.X, 0, window.ClientSize.Width - control.Bounds.Width + 0.1);
                Assert.InRange(point.Y, 0, window.ClientSize.Height - control.Bounds.Height + 0.1);
            }
            if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"machine-import-{dark}.png"), new PngBitmapEncoderOptions());
            }
            Click(window, window.FindControl<JournalButton>("SaveMachineImportButton")!);
            Assert.False(window.IsVisible); Assert.False(form.HasError, form.Error);
            var saved = Assert.Single(await store.ReadMachinesAsync());
            Assert.Equal(machine.Name, saved.Name); Assert.Equal(machine.TableName, saved.TableName);
            Assert.Equal(CuttingMachine.VintechProfile, saved.SpecificationProfileId);
            var reopened = model.CreateMachineImportForm()!;
            Assert.Equal("Гидроабразив № 2", reopened.Name);
            Assert.Equal(CuttingMachine.VintechProfile, reopened.Profile.Id); Assert.False(reopened.CanSave);
        }
        finally
        {
            foreach (var window in owner.OwnedWindows.OfType<MachineImportWindow>().ToArray())
            {
                if (window.DataContext is MachineImportViewModel form && form.HasChanges)
                {
                    window.Close(); Pump();
                    var discard = window.GetVisualDescendants().OfType<JournalButton>().Single(button => button.Label == "Не сохранять");
                    Click(window, discard);
                }
                else window.Close();
            }
            owner.Close();
        }
    }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
