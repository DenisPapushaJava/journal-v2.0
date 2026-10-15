using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class NewOrderWindowTests
{
    [AvaloniaTheory]
    [InlineData(1060, 600, false)]
    [InlineData(1060, 600, true)]
    [InlineData(1200, 720, false)]
    [InlineData(1200, 720, true)]
    public async Task FormUsesBothThemesAndKeepsActionsVisible(int width, int height, bool dark)
    {
        var form = ManualOrderTests.ValidForm(new DemoOrderStore());
        var window = new NewOrderWindow { DataContext = form, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var name in new[] { "SaveButton", "CancelButton" })
            {
                var button = window.FindControl<JournalButton>(name)!;
                var bounds = new Rect(button.Bounds.Size).TransformToAABB(button.TransformToVisual(window)!.Value);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= height && bounds.Right <= width);
                Assert.True(((Control)button.Content!).Bounds.Width <= button.Bounds.Width);
            }
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
            var scroll = window.FindControl<ScrollViewer>("FormScroll")!;
            Assert.True(scroll.Viewport.Height > 200);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.All(window.GetVisualDescendants().OfType<OrderEntryControl>(), field => Assert.True(field.Bounds.Width >= 200));
            var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"new-order-{width}-{(dark ? "dark" : "light")}.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            form.OrderDate.Value = "31.02.2026";
            form.Material.Value = "Амг 3";
            await form.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<TextBlock>("ValidationSummary")!.IsVisible);
            var save = window.FindControl<JournalButton>("SaveButton")!;
            Assert.True(save.TranslatePoint(new Point(0, save.Bounds.Height), window)!.Value.Y <= height);
            if (directory is not null)
            {
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"new-order-{width}-{(dark ? "dark" : "light")}-errors.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MainWindowOpensFormValidatesAndSelectsCreatedOrder()
    {
        var store = new DemoOrderStore();
        var viewModel = new MainWindowViewModel(store, store) { IsDarkTheme = true };
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            viewModel.SearchText = "нет совпадений";
            Click(window, window.FindControl<JournalButton>("NewOrderButton")!);
            var dialog = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            var form = (NewOrderViewModel)dialog.DataContext!;
            Assert.Equal(ThemeVariant.Dark, dialog.ActualThemeVariant);
            Click(dialog, dialog.FindControl<JournalButton>("SaveButton")!);
            Assert.True(form.HasValidationErrors);
            Assert.True(dialog.IsVisible);
            Assert.Equal(3, (await store.ReadAsync()).Count);
            var first = dialog.GetVisualDescendants().OfType<OrderEntryControl>().First();
            Assert.True(first.FindControl<TextBox>("Input")!.IsFocused);
            var valid = ManualOrderTests.ValidForm(store);
            foreach (var control in dialog.GetVisualDescendants().OfType<OrderEntryControl>())
            {
                var field = (OrderEntryField)control.DataContext!;
                var value = valid.Fields.Single(source => source.Label == field.Label).Value;
                if (field.HasChoices) control.FindControl<ComboBox>("Choice")!.SelectedItem = value;
                else control.FindControl<TextBox>("Input")!.Text = value;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(form.HasValidationErrors);
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsVisible);
            Assert.Equal(4, viewModel.Orders.Count);
            Assert.Empty(viewModel.SearchText);
            var selected = (OrderRowViewModel)window.FindControl<DataGrid>("OrdersGrid")!.SelectedItem!;
            Assert.Equal("1015--1004", selected.SpecificationNumber);
            Assert.Equal("Не рассчитана", selected.StoredPriceText);
            await viewModel.ReloadCommand.ExecuteAsync(null);
            Assert.Equal(4, viewModel.Orders.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EscapeCancelsWithoutAddingAndNextFormIsEmpty()
    {
        var store = new DemoOrderStore();
        var window = new MainWindow { DataContext = new MainWindowViewModel(store, store) };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Click(window, window.FindControl<JournalButton>("NewOrderButton")!);
            var dialog = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            ((NewOrderViewModel)dialog.DataContext!).Customer.Value = "Отмена";
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsVisible);
            Assert.Equal(3, (await store.ReadAsync()).Count);
            Click(window, window.FindControl<JournalButton>("NewOrderButton")!);
            var next = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            Assert.Empty(((NewOrderViewModel)next.DataContext!).Customer.Value);
            Click(next, next.FindControl<JournalButton>("CancelButton")!);
            Assert.Equal(3, (await store.ReadAsync()).Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PendingSaveBlocksRepeatedClickAndClosing()
    {
        var store = new DemoOrderStore();
        var creator = new BlockingCreator(store);
        var form = ManualOrderTests.ValidForm(creator);
        var window = new NewOrderWindow { DataContext = form };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<JournalButton>("SaveButton")!;
            Click(window, save);
            Assert.True(form.SaveCommand.IsRunning);
            Assert.False(save.IsEffectivelyEnabled);
            Assert.False(window.FindControl<JournalButton>("CancelButton")!.IsEnabled);
            window.Close();
            Assert.True(window.IsVisible);
            Click(window, save);
            Assert.Equal(1, creator.CallCount);
            creator.Complete();
            await form.SaveCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
            Assert.Equal(4, (await store.ReadAsync()).Count);
        }
        finally { creator.Complete(); window.Close(); }
    }

    private sealed class BlockingCreator(DemoOrderStore store) : IManualOrderCreator
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public void Complete() => _completion.TrySetResult();
        public async Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        {
            CallCount++;
            await _completion.Task.WaitAsync(cancellationToken);
            return await store.CreateAsync(draft, cancellationToken);
        }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
