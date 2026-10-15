using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class BatchWindowTests
{
    [AvaloniaTheory]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, false)]
    [InlineData(1366, 768, true)]
    public void StatusButtonsUpdateTheSelectionAndPreserveTableSettings(int width, int height, bool dark)
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store, store) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = width, Height = height };
        try
        {
            window.Show(); Pump();
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.Columns[1].Width = new DataGridLength(128);
            table.Columns[3].IsVisible = false;
            table.CollectionView.SortDescriptions.Add(Avalonia.Collections.DataGridSortDescription.FromPath(
                nameof(OrderRowViewModel.ThicknessMillimeters), System.ComponentModel.ListSortDirection.Descending));
            var first = model.Orders[0];
            table.SelectedItems.Add(first);
            table.SelectedItems.Add(model.Orders[2]);
            Pump();
            Click(window, "AddResultButton");
            var result = model.ResultText;
            foreach (var (button, color) in new[] { ("SetReadyButton", OrderColors.Ready), ("SetShippedButton", OrderColors.Shipped),
                ("ClearColorButton", OrderColors.None), ("SetWorkButton", OrderColors.Work) })
            {
                Click(window, button);
                Assert.False(model.HasError, model.ErrorMessage);
                Assert.Equal(2, table.SelectedItems.Count);
                Assert.Equal(color, first.Color);
                Assert.Equal(color, model.Orders.Single(row => row.Id == 3).Color);
                Assert.Equal(OrderColors.Ready, model.Orders.Single(row => row.Id == 2).Color);
                Assert.Same(first, model.Orders.Single(row => row.Id == 1));
            }
            Assert.Equal(result, model.ResultText);
            Assert.Equal(128, table.Columns[1].Width.Value);
            Assert.False(table.Columns[3].IsVisible);
            Assert.Single(table.CollectionView.SortDescriptions);
            Assert.Equal(3, table.CollectionView.Cast<OrderRowViewModel>().First().Id);
            table.ScrollIntoView(first, table.Columns.Last());
            Pump();
            SaveScreenshot(window, $"batch-{width}-{(dark ? "dark" : "light")}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(520, 420, false)]
    [InlineData(520, 420, true)]
    [InlineData(560, 480, false)]
    [InlineData(560, 480, true)]
    public async Task DeliveryDialogKeepsInputActionsAndValidationVisible(int width, int height, bool dark)
    {
        var store = new DemoOrderStore();
        var model = await BatchOrderTests.Model(store);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        var form = model.CreateDeliveryNumberForm()!;
        var window = new DeliveryNumberWindow { DataContext = form, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            var input = window.FindControl<TextBox>("DeliveryNumberInput")!;
            Assert.Same(input, window.FocusManager!.GetFocusedElement());
            Assert.False(window.FindControl<JournalButton>("SaveDeliveryButton")!.IsEffectivelyEnabled);
            input.Text = new string('1', 256);
            Pump();
            Click(window, "SaveDeliveryButton");
            Assert.True(form.HasError);
            foreach (var name in new[] { "DeliveryNumberInput", "SaveDeliveryButton", "CancelDeliveryButton", "DeliveryNumberError" })
            {
                var control = window.FindControl<Control>(name)!;
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(bounds.Left >= 0 && bounds.Right <= width + 1 && bounds.Top >= 0 && bounds.Bottom <= height + 1, $"{name}: {bounds}");
            }
            SaveScreenshot(window, $"delivery-{width}-{(dark ? "dark" : "light")}-error");
            input.Text = "БЛЦБ-00043210";
            Pump();
            Assert.False(form.HasError);
            SaveScreenshot(window, $"delivery-{width}-{(dark ? "dark" : "light")}");
            Click(window, "SaveDeliveryButton");
            Assert.True(form.IsSaved);
            Assert.False(window.IsVisible);
            Assert.All(model.Orders, row => Assert.Equal("БЛЦБ-00043210", row.DeliveryNumber));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DeliveryFromMainWindowCanBeCancelledAndSavedWithEnter()
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store, store);
        var owner = new MainWindow { DataContext = model };
        try
        {
            owner.Show(); Pump();
            var table = owner.FindControl<DataGrid>("OrdersGrid")!;
            table.SelectAll(); Pump();
            Click(owner, "DeliveryNumberButton");
            var dialog = Assert.Single(owner.OwnedWindows.OfType<DeliveryNumberWindow>());
            dialog.FindControl<TextBox>("DeliveryNumberInput")!.Text = "Не сохранять";
            Press(dialog, Key.Escape, PhysicalKey.Escape, owner);
            Assert.False(dialog.IsVisible);
            Assert.Equal("БЛЦБ-00010001", model.Orders[0].DeliveryNumber);
            Click(owner, "DeliveryNumberButton");
            dialog = Assert.Single(owner.OwnedWindows.OfType<DeliveryNumberWindow>());
            dialog.FindControl<TextBox>("DeliveryNumberInput")!.Text = "БЛЦБ-'Б'-123";
            Pump();
            Press(dialog, Key.Enter, PhysicalKey.Enter, owner);
            Assert.False(dialog.IsVisible);
            Assert.All(model.Orders, row => Assert.Equal("БЛЦБ-'Б'-123", row.DeliveryNumber));
            Assert.Equal(3, table.SelectedItems.Count);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task PendingWriteDisablesActionsAndClosingButPreservesSelection()
    {
        var store = new DemoOrderStore();
        var editor = new DelayedEditor(store);
        var model = new MainWindowViewModel(store, store, editor);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump();
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.SelectAll(); Pump();
            var saving = model.SetOrderColorCommand.ExecuteAsync(OrderColors.Ready);
            Pump();
            Assert.True(model.IsUpdatingOrders);
            Assert.False(model.ReloadCommand.CanExecute(null));
            Assert.False(model.CanCreateOrder);
            Assert.False(window.FindControl<JournalButton>("PersonalizeButton")!.IsEnabled);
            Assert.False(window.FindControl<JournalButton>("DeliveryNumberButton")!.IsEnabled);
            Assert.False(table.IsEnabled);
            Assert.False(window.FindControl<Grid>("FiltersPanel")!.IsEffectivelyEnabled);
            await model.ReloadCommand.ExecuteAsync(null);
            Assert.True(model.IsUpdatingOrders);
            Assert.False(model.IsLoadingOrders);
            window.Close();
            Assert.True(window.IsVisible);
            editor.Completion.SetResult();
            await saving;
            Pump();
            Assert.False(model.IsUpdatingOrders);
            Assert.Equal(3, table.SelectedItems.Count);
            Assert.True(model.CanCreateOrder);
            Assert.All(model.Orders, row => Assert.Equal(OrderColors.Ready, row.Color));
        }
        finally { editor.Completion.TrySetResult(); window.Close(); }
    }

    [AvaloniaFact]
    public void StatusSortReordersTheChangedRowAndKeepsItSelected()
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store, store);
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show(); Pump();
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.CollectionView.SortDescriptions.Add(Avalonia.Collections.DataGridSortDescription.FromPath(
                nameof(OrderRowViewModel.StatusText), System.ComponentModel.ListSortDirection.Ascending));
            Assert.Equal(3, table.CollectionView.Cast<OrderRowViewModel>().First().Id);
            table.SelectedItem = model.Orders.Single(row => row.Id == 3);
            Pump();
            Click(window, "SetShippedButton");
            Assert.Equal(3, table.CollectionView.Cast<OrderRowViewModel>().Last().Id);
            Assert.Equal(3, Assert.IsType<OrderRowViewModel>(Assert.Single(table.SelectedItems)).Id);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DeliverySavePreventsClosingAndSupportsRetryAfterFailure()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var form = new DeliveryNumberViewModel([ResultOutputTests.Order()], _ => completion.Task);
        var window = new DeliveryNumberWindow { DataContext = form };
        try
        {
            window.Show(); Pump();
            form.Number = "БЛЦБ-123";
            var saving = form.SaveCommand.ExecuteAsync(null);
            Pump();
            window.Close();
            Assert.True(window.IsVisible);
            form.ClearNumberCommand.Execute(null);
            Assert.Equal("БЛЦБ-123", form.Number);
            Assert.False(window.FindControl<JournalButton>("CancelDeliveryButton")!.IsEffectivelyEnabled);
            completion.SetResult(false);
            await saving;
            Pump();
            Assert.True(window.IsVisible);
            Assert.True(form.HasError);
            Assert.True(window.FindControl<JournalButton>("SaveDeliveryButton")!.IsEffectivelyEnabled);
            Assert.Equal("БЛЦБ-123", form.Number);
        }
        finally { completion.TrySetResult(false); window.Close(); }
    }

    private sealed class DelayedEditor(DemoOrderStore store) : IOrderBatchEditor
    {
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color, CancellationToken cancellationToken = default)
        {
            await Completion.Task;
            return await store.SetColorAsync(expected, color, cancellationToken);
        }
        public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number, CancellationToken cancellationToken = default) =>
            store.SetDeliveryNumberAsync(expected, number, cancellationToken);
    }
    private static void Click(Window window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Press(Window window, Key key, PhysicalKey physical, Window owner)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        (window.IsVisible ? window : owner).KeyRelease(key, RawInputModifiers.None, physical, null);
        Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void SaveScreenshot(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
