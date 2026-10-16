using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class LocalCopyWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalRealLocalCopyLoadsInTheWindowWithEditableActions(bool dark)
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_TEST_LOCAL_COPY");
        Assert.SkipUnless(OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(path), "Локальная тестовая копия Linux не задана.");
        using var fixture = new LocalJournalFixture();
        Directory.CreateDirectory(fixture.Directory); File.Copy(path!, fixture.Path);
        var expected = await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken);
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: fixture.Path)) };
        var model = OrderSourceTests.Model(new OrderSourceManager(settings)); model.IsDarkTheme = dark;
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump(); await model.ReloadCommand.ExecutionTask!; Pump();
            Assert.False(model.HasError, model.ErrorMessage); Assert.Equal(expected.Count, model.Orders.Count);
            Assert.True(window.FindControl<JournalButton>("NewOrderButton")!.IsEffectivelyEnabled);
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.SelectedItems.Add(model.Orders.First(row => row.StoredCuttingPrice is not null)); Pump();
            Assert.True(window.FindControl<JournalButton>("DeliveryNumberButton")!.IsEffectivelyEnabled);
            Click(window, "AddResultButton"); Assert.False(model.HasResultError, model.ResultMessage);
            Screenshot(window, "private-local-real-" + (dark ? "dark" : "light"));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinuxCopyEnablesCreationPricingAndBlcbAndKeepsChangesAfterRestart(bool dark)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Локальная копия в приложении предназначена для Linux.");
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: fixture.Path)) };
        var model = OrderSourceTests.Model(new OrderSourceManager(settings)); model.IsDarkTheme = dark;
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 720 };
        try
        {
            window.Show(); Pump(); await model.ReloadCommand.ExecutionTask!; Pump();
            Assert.False(model.HasError, model.ErrorMessage);
            Assert.Contains("Локальная", model.SourceDescription);
            Assert.True(window.FindControl<JournalButton>("NewOrderButton")!.IsEffectivelyEnabled);
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.SelectedItems.Add(model.Orders[0]); Pump();
            Assert.True(window.FindControl<JournalButton>("DeliveryNumberButton")!.IsEffectivelyEnabled);
            Click(window, "DeliveryNumberButton");
            var delivery = Assert.Single(window.OwnedWindows.OfType<DeliveryNumberWindow>());
            var blcb = (DeliveryNumberViewModel)delivery.DataContext!;
            Assert.Contains("локальной", blcb.StorageDescription);
            blcb.Number = "БЛЦБ-тест-42";
            await blcb.SaveCommand.ExecuteAsync(null); Pump();
            Assert.True(blcb.IsSaved, blcb.Error); Assert.False(delivery.IsVisible);
            Assert.Contains("локальной копии", model.UpdatedText);
            Click(window, "NewOrderButton");
            var dialog = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            var form = (NewOrderViewModel)dialog.DataContext!;
            Fill(form); Pump();
            Assert.Contains("Локальная", dialog.Title);
            Click(dialog, "CalculatePriceButton"); await form.CalculatePriceCommand.ExecutionTask!; Pump();
            Assert.False(form.HasPriceError, form.PriceError); Assert.Equal("977 ₽", form.PriceText);
            Screenshot(dialog, "local-new-" + (dark ? "dark" : "light"));
            form.Coefficient.Value = "2"; Pump(); Assert.Equal("Не рассчитана", form.PriceText);
            form.Coefficient.Value = "2,3";
            Click(dialog, "SaveButton"); await form.SaveCommand.ExecutionTask!; Pump();
            Assert.True(form.IsSaved, form.SaveError); Assert.False(dialog.IsVisible);
            Assert.Equal(3, model.Orders.Count); Assert.Equal(43, ((OrderRowViewModel)table.SelectedItem!).Id);
            Assert.Equal(977m, model.Orders.Single(row => row.Id == 43).StoredCuttingPrice);
            Screenshot(window, "local-main-" + (dark ? "dark" : "light"));
        }
        finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
        var restarted = OrderSourceTests.Model(new OrderSourceManager(settings));
        await restarted.ReloadCommand.ExecuteAsync(null);
        Assert.Equal(3, restarted.Orders.Count);
        Assert.Equal("БЛЦБ-тест-42", restarted.Orders.Single(row => row.Id == 42).DeliveryNumber);
        Assert.Equal(1234.5678m, restarted.Orders.Single(row => row.Id == 42).StoredCuttingPrice);
        Assert.Equal(977m, restarted.Orders.Single(row => row.Id == 43).StoredCuttingPrice);
        Assert.True(restarted.CanCreateOrder);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalSourceSelectionKeepsActionsVisibleAndPersistsPath(bool dark)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Выбор локального источника доступен на Linux.");
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var settings = new OrderSourceTests.MemorySettings();
        var model = OrderSourceTests.Model(new OrderSourceManager(settings));
        var form = model.CreateDatabaseSettingsForm()!;
        var window = new DatabaseSettingsWindow { DataContext = form, Width = 600, Height = 460,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            var radio = window.FindControl<RadioButton>("LocalCopyModeButton")!;
            radio.BringIntoView(); Pump(); Click(window, radio.Name!);
            Assert.True(form.UseLocalCopy); Assert.False(form.UseDemo); Assert.False(form.UseDatabase);
            form.LocalCopyPath = "некорректный путь";
            Click(window, "ConnectDatabaseButton"); Pump(); Assert.True(form.HasError);
            foreach (var name in new[] { "ConnectDatabaseButton", "CancelDatabaseButton", "DatabaseConnectionError" })
            {
                var control = window.FindControl<Control>(name)!;
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= window.Height && bounds.Right <= window.Width, name);
            }
            form.LocalCopyPath = fixture.Path; Pump();
            Screenshot(window, "local-source-" + (dark ? "dark" : "light"));
            Click(window, "ConnectDatabaseButton"); await form.ConnectCommand.ExecutionTask!; Pump();
            Assert.True(form.IsConnected, form.Error); Assert.False(window.IsVisible);
            Assert.Equal(fixture.Path, settings.Saved!.DatabasePath);
            Assert.True(model.CanCreateOrder); Assert.Equal(2, model.Orders.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task WindowsKeepsMdbAdapterAndRejectsLinuxCopyAsAnOperationalSource()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var profile = Path.Combine(fixture.Directory, "data-source.json");
        var settings = new JsonOrderSourceSettingsStore(profile);
        settings.Save(new(DatabasePath: fixture.Path));
        var form = new DatabaseSettingsViewModel(null, (_, _) => Task.FromResult<string?>(null));
        Assert.Equal(OperatingSystem.IsLinux(), form.CanUseLocalCopy);
        if (OperatingSystem.IsLinux()) Assert.Equal(fixture.Path, settings.Load().Settings!.DatabasePath);
        else
        {
            Assert.NotNull(settings.Load().Error);
            var local = new OrderSourceManager(new OrderSourceTests.MemorySettings()).Create(fixture.Path);
            Assert.Null(local.Creator); Assert.Null(local.Editor);
            await Assert.ThrowsAsync<OrderReadException>(() => local.Reader.ReadAsync(TestContext.Current.CancellationToken));
        }
        var mdb = new OrderSourceManager(new OrderSourceTests.MemorySettings()).Create(OrderSourceTests.DatabasePath);
        Assert.False(mdb.IsLocalCopy);
        Assert.Equal(OperatingSystem.IsWindows(), mdb.Creator is not null);
        Assert.False(mdb.Creator is LocalJournalStore);
        Assert.Equal(OperatingSystem.IsWindows(), mdb.Editor is not null);
    }

    private static void Fill(NewOrderViewModel form)
    {
        form.OrderDate.Value = "28.03.2026"; form.SpecificationNumber.Value = "0328-1002";
        form.Manager.Value = "Иван"; form.Customer.Value = "Тест В"; form.CuttingType.Value = "HansLaser";
        form.Material.Value = "Амг3"; form.Thickness.Value = "2,5"; form.SheetSize.Value = "1500x3000";
        form.SheetCount.Value = "2"; form.Weight.Value = "38,5"; form.CutLength.Value = "25";
        form.Coefficient.Value = "2,3"; form.MaterialId.Value = "2"; form.Gas.Value = "Азот";
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Screenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
