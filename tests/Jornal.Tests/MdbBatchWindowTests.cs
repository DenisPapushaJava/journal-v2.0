using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class MdbBatchWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WritableSourceHasAccurateLabelsAndPersistsThroughTheEditor(bool dark)
    {
        var store = new DemoOrderStore();
        var manager = new OrderSourceManager(new OrderSourceTests.MemorySettings
            { Loaded = new(new(DatabasePath: OrderSourceTests.DatabasePath)) }, _ => store, _ => store);
        var model = OrderSourceTests.Model(manager); model.IsDarkTheme = dark;
        var owner = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            owner.Show(); Pump();
            var table = owner.FindControl<DataGrid>("OrdersGrid")!; table.SelectAll(); Pump();
            Assert.Contains("статусы и БЛЦБ", model.SourceDescription);
            Assert.False(model.CanCreateOrder); Assert.True(model.CanUpdateSelectedOrders);
            Click(owner, "SetReadyButton");
            Assert.Contains("сохранено в MDB", model.UpdatedText); Assert.DoesNotContain("до закрытия", model.UpdatedText);
            Assert.All(model.Orders, row => Assert.Equal(OrderColors.Ready, row.Color));
            Click(owner, "DeliveryNumberButton");
            var dialog = Assert.Single(owner.OwnedWindows.OfType<DeliveryNumberWindow>());
            Assert.DoesNotContain("Демо", dialog.Title);
            var form = (DeliveryNumberViewModel)dialog.DataContext!;
            Assert.Contains("сохраняются в MDB", form.StorageDescription);
            dialog.FindControl<TextBox>("DeliveryNumberInput")!.Text = "БЛЦБ-новый"; Pump();
            SaveScreenshot(dialog, $"mdb-delivery-{(dark ? "dark" : "light")}");
            Click(dialog, "SaveDeliveryButton");
            Assert.False(dialog.IsVisible); Assert.False(model.HasError, model.ErrorMessage);
            Assert.All(model.Orders, row => Assert.Equal("БЛЦБ-новый", row.DeliveryNumber));
            SaveScreenshot(owner, $"mdb-batch-{(dark ? "dark" : "light")}");
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleDeliveryFormShowsTheConflictWithoutDiscardingInput(bool dark)
    {
        var store = new DemoOrderStore(); var model = await BatchOrderTests.Model(store); model.IsDarkTheme = dark;
        model.UpdateSelection(model.Orders, model.Orders[0]);
        var form = model.CreateDeliveryNumberForm()!;
        var before = await store.ReadAsync(TestContext.Current.CancellationToken);
        await store.SetDeliveryNumberAsync([before[2]], "Другой клиент", TestContext.Current.CancellationToken);
        var window = new DeliveryNumberWindow { DataContext = form, Width = 520, Height = 420,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            window.FindControl<TextBox>("DeliveryNumberInput")!.Text = "Мой номер"; Pump();
            Click(window, "SaveDeliveryButton");
            Assert.True(model.RequiresReload); Assert.True(form.HasError); Assert.Equal("Мой номер", form.Number);
            Assert.False(window.FindControl<JournalButton>("SaveDeliveryButton")!.IsEffectivelyEnabled);
            foreach (var name in new[] { "DeliveryNumberInput", "DeliveryNumberError", "CancelDeliveryButton" })
            {
                var control = window.FindControl<Control>(name)!;
                var rect = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(rect.Top >= 0 && rect.Bottom <= window.Height + 1, $"{name}: {rect}");
            }
            SaveScreenshot(window, $"mdb-conflict-{(dark ? "dark" : "light")}");
            Click(window, "CancelDeliveryButton"); Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, string name)
    {
        if (TableStatusTestActions.TryExecute(window, name)) return;
        var control = window.FindControl<Control>(name)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void SaveScreenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
