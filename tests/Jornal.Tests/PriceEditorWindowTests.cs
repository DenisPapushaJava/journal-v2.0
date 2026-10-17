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
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class PriceEditorWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, 960, 700)]
    [InlineData(true, 960, 700)]
    [InlineData(false, 840, 560)]
    [InlineData(true, 840, 560)]
    public void NavigationEditingValidationAndUnsavedCloseFitBothThemes(bool dark, int width, int height)
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Деможурнал · без сохранения", "Демонстрационные тарифы", PriceStorageKind.Session);
        var window = new PriceEditorWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = width, Height = height };
        try
        {
            window.Show(); Pump();
            Assert.Equal(5, window.FindControl<ListBox>("PriceGroups")!.ItemCount);
            Assert.True(model.CanEdit); Assert.Equal(16, model.Rows.Count);
            Assert.False(window.FindControl<DataGrid>("PriceGrid")!.Columns[2].IsVisible);
            Save(window, $"prices-view-{width}-{dark}");
            Click(window, "EditPricesButton");
            Assert.True(model.IsEditing); Assert.False(window.FindControl<ListBox>("PriceGroups")!.IsEffectivelyEnabled);
            var grid = window.FindControl<DataGrid>("PriceGrid")!;
            Assert.True(grid.Columns[2].IsVisible);
            var input = grid.GetVisualDescendants().OfType<TextBox>().First();
            input.Text = "2+1"; Pump(); Assert.True(model.HasInvalidPrices);
            Assert.False(window.FindControl<JournalButton>("SavePricesButton")!.IsEffectivelyEnabled);
            input.Text = "32"; Pump(); Assert.Equal("32", model.Rows[0].Value); Assert.True(model.CanSave);
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible)) InBounds(window, button);
            Save(window, $"prices-edit-{width}-{dark}");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.True(window.IsVisible); Assert.True(model.ConfirmDiscard);
            Click(window, "DiscardPricesButton"); Assert.False(window.IsVisible);
            Assert.Equal(25, store.ReadAsync(PriceTables.Laser).Result.Rates[0].RublesPerMeter);
        }
        finally { if (window.IsVisible) { model.CancelEditCommand.Execute(null); window.Close(); } }
    }

    [AvaloniaFact]
    public void MainHeaderOpensPricesAndSharedLaserPriceAppearsUnderHansLaser()
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store, store);
        var owner = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            owner.Show(); Pump(); Click(owner, "PricesButton");
            var window = Assert.Single(owner.OwnedWindows.OfType<PriceEditorWindow>());
            var prices = (PriceEditorViewModel)window.DataContext!;
            Click(window, "EditPricesButton"); prices.Rows.First(row => row.Original.ThicknessMillimeters == 2.5m).Value = "70";
            Click(window, "SavePricesButton");
            Assert.False(prices.IsEditing); Assert.False(prices.HasError, prices.Error);
            prices.SelectedGroup = PriceCatalog.Groups[3]; Pump();
            Assert.Equal(PriceTables.Laser, prices.TableName);
            Assert.Equal("70", prices.Rows.First(row => row.Original.ThicknessMillimeters == 2.5m).CurrentPrice);
            prices.SelectedProfile = prices.Profiles[1]; Pump(); Assert.Equal(PriceTables.HansLaserId1, prices.TableName);
            Assert.Equal("25", prices.Rows.First(row => row.Original.ThicknessMillimeters == 2.5m).CurrentPrice);
            prices.SelectedGroup = PriceCatalog.Groups[^1]; Pump();
            Assert.True(prices.IsReadOnly); Assert.False(prices.CanEdit); Assert.Equal("PriceLM500", prices.TableName);
            prices.SelectedProfile = prices.Profiles[1]; Pump(); Assert.Equal("PriceHL", prices.TableName); Assert.NotEmpty(prices.Rows);
            Click(window, "ClosePricesButton"); Assert.False(window.IsVisible);
            Assert.True(owner.FindControl<JournalButton>("PricesButton")!.IsEffectivelyEnabled);
            InBounds(owner, owner.FindControl<Control>("PricesButton")!);
            InBounds(owner, owner.FindControl<Control>("UpdatesButton")!);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task PendingSaveBlocksRepeatedCommandsAndClosingEitherWindow()
    {
        var store = new DemoOrderStore(); var pending = new PendingEditor(store.Prices);
        var journal = new MainWindowViewModel(store, store, store);
        journal.InitializeSource(new(null, store, store, store) { Prices = store.Prices, PriceEditor = pending });
        var owner = new MainWindow { DataContext = journal };
        Task? saving = null;
        try
        {
            owner.Show(); Pump(); Click(owner, "PricesButton");
            var window = Assert.Single(owner.OwnedWindows.OfType<PriceEditorWindow>());
            var prices = (PriceEditorViewModel)window.DataContext!;
            Click(window, "EditPricesButton"); prices.Rows[0].Value = "41";
            saving = prices.SaveCommand.ExecuteAsync(null); Pump();
            Assert.True(prices.IsSaving); Assert.True(journal.IsUpdatingOrders);
            Assert.False(prices.SaveCommand.CanExecute(null)); Assert.False(prices.CanClose); Assert.False(prices.CanRefresh);
            Click(window, "SavePricesButton"); Assert.Equal(1, pending.Attempts);
            window.Close(); Assert.True(window.IsVisible);
            owner.Close(); Assert.True(owner.IsVisible); Assert.True(window.IsVisible);
            pending.Complete.TrySetResult(); await saving; Pump();
            Assert.False(prices.HasError, prices.Error); Assert.False(prices.IsSaving); Assert.False(journal.IsUpdatingOrders);
            Assert.Equal(1, pending.Attempts); Assert.False(prices.HasChanges);
            Click(window, "ClosePricesButton"); Assert.False(window.IsVisible);
        }
        finally
        {
            pending.Complete.TrySetResult(); if (saving is not null) await saving;
            foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 960, 700)]
    [InlineData(true, 960, 700)]
    [InlineData(false, 840, 560)]
    [InlineData(true, 840, 560)]
    public void AddThicknessPanelFitsAndEnterStagesWithoutSaving(bool dark, int width, int height)
    {
        var store = new DemoOrderStore();
        var prices = new PriceEditorViewModel(store.Prices, store.Prices, "OrderDB.mdb", "Проверочный источник", PriceStorageKind.SharedDatabase);
        var window = new PriceEditorWindow { DataContext = prices, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = width, Height = height };
        try
        {
            window.Show(); Pump(); Click(window, "EditPricesButton"); Click(window, "BeginAddPriceRateButton");
            Assert.True(prices.ShowAddRate); Assert.True(prices.ShowExpansionNotice);
            window.FindControl<TextBox>("NewThicknessInput")!.Text = "2.800";
            window.FindControl<TextBox>("NewPriceRateInput")!.Text = "47"; Pump();
            Assert.False(prices.CanSave);
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible)) InBounds(window, button);
            InBounds(window, window.FindControl<Control>("NewThicknessInput")!); InBounds(window, window.FindControl<Control>("NewPriceRateInput")!);
            Save(window, $"prices-new-thickness-{width}-{dark}");
            window.FindControl<TextBox>("NewPriceRateInput")!.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump();
            Assert.False(prices.ShowAddRate); Assert.True(prices.CanSave);
            var added = Assert.Single(prices.Rows, row => row.IsNew); Assert.Equal("2,8", added.Original.ColumnName); Assert.Equal("47", added.Value);
            Assert.DoesNotContain(store.ReadAsync(PriceTables.Laser).Result.Rates, rate => rate.ThicknessMillimeters == 2.8m);
            Save(window, $"prices-staged-thickness-{width}-{dark}");
            Click(window, "SavePricesButton"); Assert.False(prices.HasError, prices.Error); Assert.False(prices.IsEditing);
            Assert.Equal(47, store.ReadAsync(PriceTables.Laser).Result.Rates.Single(rate => rate.ThicknessMillimeters == 2.8m).RublesPerMeter);
        }
        finally { if (prices.IsEditing) prices.CancelEditCommand.Execute(null); window.Close(); }
    }

    [AvaloniaFact]
    public void UnstagedThicknessInputAlsoRequiresExplicitDiscardOnClose()
    {
        var store = new DemoOrderStore(); var prices = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "");
        var window = new PriceEditorWindow { DataContext = prices };
        try
        {
            window.Show(); Pump(); Click(window, "EditPricesButton"); Click(window, "BeginAddPriceRateButton");
            window.FindControl<TextBox>("NewThicknessInput")!.Text = "2,8"; Pump();
            window.Close(); Pump(); Assert.True(window.IsVisible); Assert.True(prices.ConfirmDiscard);
            Click(window, "DiscardPricesButton"); Assert.False(window.IsVisible);
            Assert.DoesNotContain(store.ReadAsync(PriceTables.Laser).Result.Rates, rate => rate.ThicknessMillimeters == 2.8m);
        }
        finally { if (prices.IsEditing) prices.CancelEditCommand.Execute(null); window.Close(); }
    }

    private sealed class PendingEditor(IPriceEditor inner) : IPriceEditor
    {
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts { get; private set; }
        public async Task<PriceTable> SaveAsync(PriceTableChange change, CancellationToken cancellationToken = default)
        { Attempts++; await Complete.Task.WaitAsync(cancellationToken); return await inner.SaveAsync(change, cancellationToken); }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, string name)
    {
        var button = window.FindControl<Control>(name)!;
        var position = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(position, MouseButton.Left); window.MouseUp(position, MouseButton.Left); Pump();
    }
    private static void InBounds(Window window, Control control)
    {
        var rect = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
        Assert.True(rect.Left >= -1 && rect.Top >= -1 && rect.Right <= window.ClientSize.Width + 1 && rect.Bottom <= window.ClientSize.Height + 1,
            $"{control.Name}: {rect}, окно {window.ClientSize}");
    }
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory); using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
