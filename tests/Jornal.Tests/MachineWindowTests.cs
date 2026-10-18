using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class MachineWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAtMinimumSizeAndOpenTheNewTariffAndExistingOrder(bool dark)
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Деможурнал", "Деможурнал", PriceStorageKind.Session);
        var owner = new PriceEditorWindow { DataContext = model, Width = 840, Height = 560, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            owner.Show(); Pump(); Click(owner, owner.FindControl<JournalButton>("NewMachineButton")!);
            var window = Assert.Single(owner.OwnedWindows.OfType<NewMachineWindow>()); window.Width = 620; window.Height = 520; Pump();
            var form = (NewMachineViewModel)window.DataContext!; form.Name = "Гидроабразивная"; form.Profile = form.Profiles[1];
            form.Thickness = "2.5"; form.Rate = "170"; Click(window, window.FindControl<JournalButton>("AddMachineRateButton")!);
            form.Thickness = "70"; form.Rate = "600"; Click(window, window.FindControl<JournalButton>("AddMachineRateButton")!);
            Assert.Empty(await store.ReadMachinesAsync()); Assert.Equal(2, form.Rates.Count);
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible)) InBounds(window, button);
            Save(window, $"new-machine-{dark}");
            Click(window, window.FindControl<JournalButton>("CreateMachineButton")!); Pump();
            Assert.False(window.IsVisible); Assert.False(form.HasError, form.Error);
            var machine = Assert.Single(await store.ReadMachinesAsync()); Assert.Equal(machine.TableName, model.TableName);
            Assert.Same(model.SelectedGroup, owner.FindControl<ListBox>("PriceGroups")!.SelectedItem);
            Assert.Equal(2, model.Rows.Count); Save(owner, $"new-machine-price-{dark}");
            var original = OrderRecord.FromSummary(await store.CreateAsync(MachineCatalogTests.Draft(machine)));
            var edit = new EditOrderViewModel(original, store.Maintenance, "Демо", _ => Task.FromResult<OrderRecord?>(original));
            var editor = new EditOrderWindow { DataContext = edit, RequestedThemeVariant = owner.ActualThemeVariant };
            try
            {
                editor.Show(); Pump(); Assert.Equal(machine.Name, edit.CuttingType.Value);
                Assert.Contains(machine.Name, edit.CuttingType.Choices!); Assert.Equal(machine, edit.BuildChange().Machine);
            }
            finally { editor.Close(); }
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MaterialReviewIsVisibleConfirmableAndRequiredAgainAfterReplacement(bool dark, bool suggestions)
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine(); await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine));
        var form = new NewOrderViewModel(store); await form.LoadMachinesAsync();
        if (suggestions) form.Material.Suggestions = ["Ст-20", "Aisi-304"];
        var window = new NewOrderWindow { DataContext = form, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            var preview = SpecificationImportPreview.Create("Раскрой.pdf", new WaterjetPdfSpecificationParser().Parse(WaterjetPdfParserTests.Document()));
            form.ApplyImport(preview); Pump();
            Assert.True(form.Material.NeedsReview); Assert.False(form.SaveCommand.CanExecute(null));
            var entry = window.GetVisualDescendants().OfType<OrderEntryControl>().Single(control => ReferenceEquals(control.DataContext, form.Material));
            var combo = entry.FindControl<ComboBox>("EditableSuggestionsInput")!;
            Assert.Equal(suggestions, combo.IsVisible);
            var input = entry.GetVisualDescendants().OfType<TextBox>().Single(control => control.IsEffectivelyVisible);
            var border = suggestions
                ? combo.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "Background")
                : input.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "PART_BorderElement");
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(((ISolidColorBrush)Application.Current!.FindResource(window.ActualThemeVariant, "WorkForeground")!).Color, ((ISolidColorBrush)border.BorderBrush!).Color);
            if (suggestions)
            {
                combo.Focus(); combo.IsDropDownOpen = true; Pump();
                Assert.Equal("Сталь 20/09Г2С", form.Material.Value); Assert.True(form.Material.NeedsReview);
                var popup = TopLevel.GetTopLevel(combo.ContainerFromIndex(0)!)!;
                popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                popup.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
                Assert.False(combo.IsDropDownOpen); Assert.True(window.IsVisible); Assert.True(form.Material.NeedsReview);
            }
            var confirm = window.GetVisualDescendants().OfType<JournalButton>().Single(button => button.Label == "Подтвердить материал" && button.IsEffectivelyVisible);
            InBounds(window, confirm); Save(window, $"waterjet-material-review-{dark}-{suggestions}"); Click(window, confirm);
            Assert.False(form.Material.NeedsReview); Assert.Equal("Сталь 20/09Г2С", form.Material.Value);
            Assert.False(form.HasImportDiagnostics);
            form.ApplyImport(preview); Pump(); Assert.True(form.Material.NeedsReview);
            if (suggestions) input.Text = "Ст-20";
            else form.Material.Value = "Ст-20";
            Pump(); Assert.False(form.Material.NeedsReview); Assert.Equal("Ст-20", form.Material.Value);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void InBounds(Window window, Control control)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.X >= -0.1 && point.Y >= -0.1 && point.X + control.Bounds.Width <= window.ClientSize.Width + 0.1 && point.Y + control.Bounds.Height <= window.ClientSize.Height + 0.1,
            $"{control.Name}: {point}, {control.Bounds.Size}, {window.ClientSize}");
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Save(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
