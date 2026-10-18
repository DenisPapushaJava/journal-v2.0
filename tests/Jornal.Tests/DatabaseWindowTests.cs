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
using Jornal.Desktop.DataSources;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class DatabaseWindowTests
{
    [AvaloniaFact]
    public async Task CancellingPendingConnectionClosesTheDialogAndKeepsPreviousRows()
    {
        var model = OrderSourceTests.Model(new OrderSourceManager(new OrderSourceTests.MemorySettings(), _ => new OrderSourceTests.DelayedReader()));
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump();
            var dialog = OpenSourceSettings(window);
            var form = ((SettingsViewModel)dialog.DataContext!).Database!;
            form.UseDatabase = true; form.DatabasePath = OrderSourceTests.DatabasePath; Pump();
            Click(dialog, "ConnectDatabaseButton");
            Assert.True(model.IsLoadingOrders); Assert.True(window.FindDescendant<Border>("LoadingState")!.IsVisible);
            Assert.False(model.ShowEmptyState);
            Click(dialog, "CloseSettingsButton");
            await form.ConnectCommand.ExecutionTask!; Pump();
            Assert.False(dialog.IsVisible); Assert.False(model.IsLoadingOrders); Assert.Null(model.DatabasePath);
            Assert.Equal(3, model.Orders.Count); Assert.True(model.CanCreateOrder);
        }
        finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalLocalDatabaseIsVisibleInTheActualWindow(bool dark)
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_TEST_MDB");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Локальная копия MDB не задана.");
        var expected = await new AccessOrderReader(path!).ReadAsync(TestContext.Current.CancellationToken);
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: Path.GetFullPath(path!))) };
        var model = OrderSourceTests.Model(new OrderSourceManager(settings));
        model.IsDarkTheme = dark;
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            await model.ReloadCommand.ExecutionTask!; Pump();
            Assert.False(model.HasError, model.ErrorMessage);
            Assert.Equal(expected.Count, model.Orders.Count);
            foreach (var order in expected)
            {
                var row = model.Orders.Single(row => row.Id == order.Id);
                Assert.Equal(order.OrderDate, row.OrderDate); Assert.Equal(order.MaterialName, row.MaterialName);
                Assert.Equal(order.StoredCuttingPrice, row.StoredCuttingPrice); Assert.Equal(order.Color, row.Color);
                Assert.Equal(order.DeliveryNumber ?? "", row.DeliveryNumber);
            }
            Assert.Equal(OperatingSystem.IsWindows(), model.CanCreateOrder);
            var table = window.FindDescendant<DataGrid>("OrdersGrid")!;
            table.SelectAll(); Pump(); Click(window, "AddResultButton");
            Assert.False(model.HasResultError, model.ResultMessage);
            SaveScreenshot(window, $"database-local-{(dark ? "dark" : "light")}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SeveralThousandOrdersCanBeSearchedAndStayVirtualized()
    {
        var model = new MainWindowViewModel(new LargeReader());
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            await model.ReloadCommand.ExecutionTask!; Pump();
            Assert.Equal(5000, model.Orders.Count);
            var realized = window.GetVisualDescendants().OfType<DataGridRow>().Count();
            Assert.InRange(realized, 1, 100);
            model.SearchText = "Тест 4321"; Pump();
            Assert.Equal(4321, Assert.Single(model.Orders).Id);
            model.SearchText = ""; Pump(); Assert.Equal(5000, model.Orders.Count);
            var table = window.FindDescendant<DataGrid>("OrdersGrid")!;
            table.ScrollIntoView(model.Orders.Last(), table.Columns[2]); Pump();
            Assert.InRange(window.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 100);
        }
        finally { window.Close(); }
    }

    private sealed class LargeReader : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderSummary>>(Enumerable.Range(1, 5000).Select(id =>
                AccessOrderMapper.Map(AccessOrderTests.Row()) with { Id = id, CustomerName = $"Тест {id}" }).ToArray());
    }

    [AvaloniaTheory]
    [InlineData(600, 460, false)]
    [InlineData(600, 460, true)]
    [InlineData(660, 480, false)]
    [InlineData(660, 480, true)]
    public void SourceFormKeepsFeedbackAndActionsVisible(int width, int height, bool dark)
    {
        var (model, _, _) = OrderSourceTests.Create();
        var form = model.CreateDatabaseSettingsForm()!;
        var window = new DatabaseSettingsWindow { DataContext = form, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            Click(window, "DatabaseModeButton");
            Assert.True(form.UseDatabase);
            window.FindDescendant<TextBox>("DatabasePathInput")!.Text = "неполный путь";
            Click(window, "ConnectDatabaseButton");
            Assert.True(form.HasError);
            foreach (var name in new[] { "DatabaseConnectionError", "CancelDatabaseButton", "ConnectDatabaseButton" })
                InBounds(window, window.FindDescendant<Control>(name)!);
            SaveScreenshot(window, $"database-{width}-{(dark ? "dark" : "light")}-error");
            window.FindDescendant<TextBox>("DatabasePathInput")!.Text = OrderSourceTests.DatabasePath;
            Pump(); Assert.False(form.HasError);
            SaveScreenshot(window, $"database-{width}-{(dark ? "dark" : "light")}");
            Click(window, "ConnectDatabaseButton");
            Assert.True(form.IsConnected, form.Error); Assert.False(window.IsVisible);
            Assert.Single(model.Orders); Assert.False(model.CanCreateOrder);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealSourceShowsReadOnlyActionsAndCanStillCopyResults(bool dark)
    {
        var (model, _, _) = OrderSourceTests.Create();
        model.IsDarkTheme = dark;
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            var dialog = OpenSourceSettings(window);
            var form = ((SettingsViewModel)dialog.DataContext!).Database!;
            form.UseDatabase = true; form.DatabasePath = OrderSourceTests.DatabasePath;
            Pump();
            dialog.FindDescendant<TextBox>("DatabasePathInput")!.Focus();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            dialog.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump();
            Assert.True(form.IsConnected, form.Error);
            Click(dialog, "CloseSettingsButton");
            window.Activate(); Pump();
            var table = window.FindDescendant<DataGrid>("OrdersGrid")!;
            table.SelectAll(); Pump();
            foreach (var name in new[] { "NewOrderButton", "DeliveryNumberButton", "SetCustomColorButton", "ClearColorButton" })
                Assert.False(window.FindDescendant<JournalButton>(name)!.IsEffectivelyEnabled, name);
            Assert.True(window.FindDescendant<JournalButton>("AddResultButton")!.IsEffectivelyEnabled);
            Assert.True(window.FindDescendant<JournalButton>("SettingsButton")!.IsEffectivelyEnabled);
            var resultButton = window.FindDescendant<JournalButton>("AddResultButton")!;
            var clicks = 0; resultButton.Click += (_, _) => clicks++;
            var before = $"active={window.IsActive}, selected={table.SelectedItems.Count}, loading={model.IsLoadingOrders}, refreshing={model.IsRefreshingOrders}";
            Click(window, "AddResultButton");
            Assert.True(model.ResultText.Contains("1235", StringComparison.Ordinal),
                $"{before}; clicks={clicks}, selected={table.SelectedItems.Count}, loading={model.IsLoadingOrders}, refreshing={model.IsRefreshingOrders}, message={model.ResultMessage}");
            Assert.Equal(1234.5678m, model.Orders[0].StoredCuttingPrice);
            Assert.Contains("только чтение", window.FindDescendant<TextBlock>("SourceDescriptionText")!.Text);
            InBounds(window, window.FindDescendant<Control>("SettingsButton")!);
            InBounds(window, window.FindDescendant<Control>("SearchBox")!);
            SaveScreenshot(window, $"database-main-{(dark ? "dark" : "light")}");
            dialog = OpenSourceSettings(window);
            dialog.FindDescendant<TextBox>("DatabasePathInput")!.Text = "Не сохранять";
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            // Escape уже закрыл диалог; KeyUp получает вернувшееся главное окно.
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(dialog.IsVisible); Assert.Equal(OrderSourceTests.DatabasePath, model.DatabasePath);
        }
        finally { foreach (var child in window.OwnedWindows.ToArray()) child.Close(); window.Close(); }
    }

    private static SettingsWindow OpenSourceSettings(MainWindow window)
    {
        Click(window, "SettingsButton");
        var dialog = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
        ((SettingsViewModel)dialog.DataContext!).SelectedIndex = (int)SettingsSection.Database;
        Pump(); return dialog;
    }

    private static void InBounds(Window window, Control control)
    {
        var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
        Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.Width + 1 && bounds.Bottom <= window.Height + 1, $"{control.Name}: {bounds}");
    }
    private static void Click(Window window, string name)
    {
        var control = window.FindDescendant<Control>(name)!;
        control.BringIntoView(); Pump();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        // После закрытия модального окна сначала переводим указатель в главное,
        // как при обычном действии пользователя, затем нажимаем кнопку.
        window.MouseMove(point); Pump();
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true,
            $"Клик {name} попал в {hit?.GetType().Name}: {point}, bounds={control.Bounds}");
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); }
    private static void SaveScreenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
