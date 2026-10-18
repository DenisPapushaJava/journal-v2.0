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
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TechnologistColorWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void GeneralSettingsPaletteCodePreviewAndRestartPreserveColorOutsideLayout(bool dark)
    {
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = 24 } });
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store);
        var owner = new MainWindow(preferences) { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            owner.Show(); Pump(); Click(owner.FindControl<Control>("SettingsButton")!);
            var settings = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>()); settings.Width = 800; settings.Height = 580; Pump();
            var enabled = settings.FindDescendant<CheckBox>("UseTechnologistColorCheckBox")!;
            enabled.BringIntoView(); Pump();
            Assert.False(enabled.IsChecked);
            var button = settings.FindDescendant<Button>("TechnologistColorButton")!;
            Assert.False(button.IsEffectivelyEnabled); Assert.Equal(OrderColors.None, model.EffectiveNewOrderColor);
            Click(enabled); button.BringIntoView(); Pump(); Click(button);
            var flyout = Assert.IsType<Flyout>(button.Flyout); Assert.True(flyout.IsOpen);
            var palette = Assert.IsAssignableFrom<Control>(flyout.Content);
            var swatches = palette.GetVisualDescendants().OfType<Button>().Where(control => control.DataContext is TechnologistColorOption).ToArray();
            Assert.Equal(29, swatches.Length); Assert.Equal(5, model.FrequentTechnologistColors.Count);
            Assert.All(model.FrequentTechnologistColors.Concat(model.OtherTechnologistColors), item => Assert.True(NewOrderColorSettings.IsAllowed(item.Argb)));
            Screenshot(TopLevel.GetTopLevel(palette)!, "technologist-palette-" + dark);
            Click(swatches.Single(control => control.DataContext is TechnologistColorOption { Argb: -8323200 })); Assert.False(flyout.IsOpen);
            Assert.Equal(-8323200, model.EffectiveNewOrderColor);
            var input = settings.FindDescendant<TextBox>("TechnologistColorCodeInput")!;
            input.BringIntoView(); Pump(); input.Text = "-25089"; Pump();
            Assert.Equal(-25089, model.EffectiveNewOrderColor); Assert.Equal("#FF9DFF", model.TechnologistColorHex);
            input.Text = null; Pump(); Assert.False(model.HasTechnologistColorError); Assert.Equal(-25089, model.EffectiveNewOrderColor);
            input.Text = "-1"; Pump(); Assert.True(model.HasTechnologistColorError); Assert.Equal(-25089, model.EffectiveNewOrderColor);
            model.CommitTechnologistColorCode(); Pump(); Assert.Equal("-25089", input.Text);
            var preview = settings.FindDescendant<Border>("TechnologistColorPreview")!;
            preview.BringIntoView(); Pump(); owner.MouseMove(new Point(1, 1)); settings.MouseMove(new Point(1, 1)); Pump();
            Assert.Equal(Color.Parse("#FF9DFF"), Assert.IsAssignableFrom<ISolidColorBrush>(preview.Background).Color);
            Assert.Equal(-25089, model.EffectiveNewOrderColor);
            var bounds = new Rect(preview.Bounds.Size).TransformToAABB(preview.TransformToVisual(settings)!.Value);
            Assert.True(bounds.Left >= 0 && bounds.Right <= 800 && bounds.Top >= 0 && bounds.Bottom <= 580);
            Screenshot(settings, "technologist-settings-" + dark);
            Click(settings.FindControl<Control>("CloseSettingsButton")!);
            Click(owner.FindControl<Control>("PersonalizeButton")!);
            Assert.True(model.IsCustomizing); model.SelectTechnologistColor(-12402177);
            owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(model.IsCustomizing); Assert.Equal(24, owner.FindControl<DataGrid>("OrdersGrid")!.RowHeight);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        Assert.Equal(new NewOrderColorSettings { Enabled = true, ColorArgb = -12402177 }, preferences.Value!.NewOrderColor);
        var reopenedModel = new MainWindowViewModel(store, store); var reopened = new MainWindow(preferences) { DataContext = reopenedModel };
        try
        {
            reopened.Show(); Pump(); Assert.Equal(-12402177, reopenedModel.EffectiveNewOrderColor); Assert.Equal(dark, reopenedModel.IsDarkTheme);
            Assert.Equal(-12402177, reopenedModel.CreateOrderForm()!.InitialColor);
        }
        finally { reopened.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task CreatedRowsRenderTheSavedColorWithoutAStatusLabel(bool dark)
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark, UseTechnologistColor = true };
        var form = model.CreateOrderForm()!; TechnologistColorTests.Fill(form, store); await form.SaveCommand.ExecuteAsync(null);
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); await window.LoadInitialOrdersAsync(); Pump();
            var item = model.Orders.Single(row => row.Id == form.CreatedOrder!.Id);
            var grid = window.FindControl<DataGrid>("OrdersGrid")!; grid.ScrollIntoView(item, grid.Columns[1]); Pump(); window.MouseMove(new Point(1, 1)); Pump();
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(control => control.DataContext == item);
            Assert.Equal(-25089, form.CreatedOrder!.Color); Assert.Equal(-25089, item.Color);
            Assert.Equal(Color.Parse("#FF9DFF"), Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);
            Assert.False(item.HasStandardStatus); Assert.Empty(item.StatusText); Screenshot(window, "technologist-row-" + dark);
        }
        finally { window.Close(); }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Screenshot(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory); using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1));
        bitmap.Render(root); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
