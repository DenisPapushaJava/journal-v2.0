using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class WindowsSecurityHelpTests
{
    private static OrderReadException BlockedRead() => new(new AccessHostBlockedException(4551).Message, new AccessHostBlockedException(4551));
    private static OrderWriteException BlockedWrite() => new(OrderWriteFailure.NotSaved,
        new AccessHostBlockedException(4551).Message + " Этот запрос на запись в MDB не отправлен.", new AccessHostBlockedException(4551));

    [Fact]
    public async Task OpensOnlyTheWindowsSecurityPageAndExplainsFailureWithoutCrashing()
    {
        Uri? opened = null;
        Assert.Null(await WindowsSecurityHelp.OpenSettingsAsync(uri => { opened = uri; return Task.FromResult(true); }));
        Assert.Equal("ms-settings:windowsdefender", opened!.OriginalString);
        Assert.Equal(WindowsSecurityHelp.ManualInstructions, await WindowsSecurityHelp.OpenSettingsAsync(_ => Task.FromResult(false)));
        Assert.Equal(WindowsSecurityHelp.ManualInstructions, await WindowsSecurityHelp.OpenSettingsAsync(_ => throw new InvalidOperationException("No handler")));
    }

    [AvaloniaFact]
    public async Task BlockedConnectionKeepsPreviousSourceRowsAndResult()
    {
        var settings = new OrderSourceTests.MemorySettings();
        var manager = new OrderSourceManager(settings, _ => new BlockedReader());
        var model = OrderSourceTests.Model(manager);
        await model.ReloadCommand.ExecuteAsync(null);
        model.AddOrdersToResult(model.Orders);
        var before = model.Orders.ToArray(); var result = model.ResultText;
        var form = model.CreateDatabaseSettingsForm()!;
        form.UseDatabase = true; form.DatabasePath = OrderSourceTests.DatabasePath;
        await form.ConnectCommand.ExecuteAsync(null);
        Assert.True(form.HasError); Assert.True(form.AccessHostHelp.IsVisible);
        Assert.Contains("4551", form.Error); Assert.False(form.IsConnected); Assert.Null(settings.Saved);
        Assert.Equal(before, model.Orders); Assert.Equal(result, model.ResultText);
        form.DatabasePath += " "; Assert.False(form.AccessHostHelp.IsVisible);
    }

    [AvaloniaFact]
    public async Task BlockedSaveKeepsInputAllowsRetryAndHidesHelpAfterSuccess()
    {
        var creator = new BlockingCreator(); var form = ManualMdbCreationTests.Filled(creator);
        await form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.True(form.HasPriceError); Assert.True(form.AccessHostHelp.IsVisible);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.HasSaveError); Assert.True(form.AccessHostHelp.IsVisible); Assert.False(form.RequiresReload);
        Assert.Equal("Клиент 'А'", form.Customer.Value); Assert.True(form.SaveCommand.CanExecute(null));
        creator.Block = false;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.False(form.AccessHostHelp.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedActionsAndRecoveryStayReadableInBothThemes(bool dark)
    {
        var reader = new ToggleReader(); var model = new MainWindowViewModel(reader, orderEditor: new BlockingEditor()) { IsDarkTheme = dark };
        var main = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            main.Show(); await model.ReloadCommand.ExecuteAsync(null);
            model.AddOrdersToResult(model.Orders); var result = model.ResultText;
            model.UpdateSelection(model.Orders, model.Orders[0]);
            var delivery = model.CreateDeliveryNumberForm()!;
            delivery.Number = "Номер для сохранения";
            var deliveryWindow = new DeliveryNumberWindow { DataContext = delivery, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                deliveryWindow.Show(); await delivery.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                Assert.True(delivery.HasError); Assert.False(model.RequiresReload); Assert.Equal("Номер для сохранения", delivery.Number);
                AssertHelpVisible(deliveryWindow); SaveScreenshot(deliveryWindow, "blocked-delivery", dark);
            }
            finally { deliveryWindow.Close(); }
            var before = model.Orders.ToArray();
            reader.Block = true; await model.ReloadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            AssertHelpVisible(main); Assert.Equal(before, model.Orders); Assert.Equal(result, model.ResultText);
            SaveScreenshot(main, "blocked-main", dark);
            reader.Block = false; await model.ReloadCommand.ExecuteAsync(null); Assert.False(model.AccessHostHelp.IsVisible);

            var database = new DatabaseSettingsViewModel(OrderSourceTests.DatabasePath, (_, _) => throw BlockedRead());
            var databaseWindow = new DatabaseSettingsWindow { DataContext = database, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                databaseWindow.Show(); await database.ConnectCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                AssertHelpVisible(databaseWindow); SaveScreenshot(databaseWindow, "blocked-database", dark);
            }
            finally { databaseWindow.Close(); }

            var creation = ManualMdbCreationTests.Filled(new BlockingCreator());
            var creationWindow = new NewOrderWindow { DataContext = creation, Width = 1060, Height = 600,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            try
            {
                creationWindow.Show(); await creation.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
                AssertHelpVisible(creationWindow); Assert.True(creationWindow.FindDescendant<JournalButton>("SaveButton")!.IsEnabled);
                SaveScreenshot(creationWindow, "blocked-create", dark);
            }
            finally { creationWindow.Close(); }
        }
        finally { main.Close(); }
    }

    private static void AssertHelpVisible(Window window)
    {
        var help = Assert.Single(window.GetVisualDescendants().OfType<WindowsSecurityHelp>());
        var button = help.FindDescendant<JournalButton>("OpenWindowsSecurityButton")!;
        Assert.True(button.IsEffectivelyVisible); Assert.True(button.IsEnabled);
        var bounds = new Rect(button.Bounds.Size).TransformToAABB(button.TransformToVisual(window)!.Value);
        Assert.True(bounds.Width > 150 && bounds.Left >= 0 && bounds.Right <= window.Width + 1 && bounds.Top >= 0 && bounds.Bottom <= window.Height + 1, bounds.ToString());
        Assert.All(button.GetVisualDescendants().OfType<Avalonia.Svg.Svg>(), svg => Assert.NotNull(svg.Model));
    }

    private static void SaveScreenshot(Window window, string name, bool dark)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, $"{name}-{(dark ? "dark" : "light")}.png"), new PngBitmapEncoderOptions());
    }

    private sealed class BlockedReader : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => throw BlockedRead();
    }
    private sealed class ToggleReader : IOrderReader
    {
        public bool Block { get; set; }
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Block ? throw BlockedRead() : new DemoOrderStore().ReadAsync(cancellationToken);
    }
    private sealed class BlockingCreator : IManualOrderCreator, IPriceReader
    {
        public bool Block { get; set; } = true;
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) => Block ? throw BlockedWrite() : Task.FromResult(draft.ToOrder(99, 977m));
        public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default) => Block ?
            throw new PriceReadException("Windows заблокировала модуль MDB", BlockedRead()) :
            Task.FromResult(new PriceTable(tableName, true, [new(2.5m, "2,5", 17)]));
    }
    private sealed class BlockingEditor : IOrderBatchEditor
    {
        public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color, CancellationToken cancellationToken = default) => throw BlockedWrite();
        public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number, CancellationToken cancellationToken = default) => throw BlockedWrite();
    }
}
