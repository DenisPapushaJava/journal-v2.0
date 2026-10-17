using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class DailyToolsWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ColorAndListsFitBothThemesAndSuggestionsPersist(bool dark)
    {
        var store = new MemoryUiPreferencesStore(); store.Save(new() { DarkTheme = dark });
        var backend = new DemoOrderStore(); var model = new MainWindowViewModel(backend, backend, backend); var owner = new MainWindow(store) { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            owner.Show(); await owner.LoadInitialOrdersAsync(); Pump();
            var table = owner.FindControl<DataGrid>("OrdersGrid")!; table.SelectedItems.Add(model.Orders[0]); Pump();
            Assert.True(owner.FindControl<JournalButton>("SetCustomColorButton")!.IsEnabled);
            Assert.Null(owner.FindControl<JournalButton>("GetColorCodeButton"));
            var form = model.CreateRowColorForm()!;
            var color = new RowColorWindow { DataContext = form, Width = 500, Height = 440, RequestedThemeVariant = owner.ActualThemeVariant };
            var shown = color.ShowDialog(owner); Pump();
            form.Code = "#123456"; Pump();
            Assert.True(color.FindControl<JournalButton>("ConfirmRowColorButton")!.IsEnabled); CheckButtons(color); Save(color, "color-" + dark);
            await form.ConfirmCommand.ExecuteAsync(null); await shown; Pump();
            Assert.Equal(unchecked((int)0xFF123456), model.Orders[0].Color);
            owner.FindControl<JournalButton>("SettingsButton")!.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent)); Pump();
            var settings = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            settings.FindControl<JournalButton>("EntrySuggestionsButton")!.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent)); Pump();
            var lists = Assert.Single(settings.OwnedWindows.OfType<EntrySuggestionsWindow>()); lists.Width = 640; lists.Height = 440; Pump();
            Assert.False(settings.CanRestart); var listForm = (EntrySuggestionsViewModel)lists.DataContext!;
            listForm.ManagersText = "Иван\nПётр"; listForm.CustomersText = "ООО, Пример"; Pump(); CheckButtons(lists); Save(lists, "lists-" + dark);
            listForm.SaveCommand.Execute(null); Pump(); Assert.False(lists.IsVisible); Assert.Equal(new[] { "Иван", "Пётр" }, store.Value!.EntrySuggestions.Managers);
            settings.Close(); owner.Close();
            var restored = new MainWindowViewModel(backend, backend, backend); var reopened = new MainWindow(store) { DataContext = restored };
            try { reopened.Show(); await reopened.LoadInitialOrdersAsync(); Pump(); Assert.Equal("ООО, Пример", Assert.Single(restored.CreateOrderForm()!.Customer.Suggestions!)); }
            finally { reopened.Close(); }
        }
        finally { foreach (var settings in owner.OwnedWindows.ToArray()) { foreach (var child in settings.OwnedWindows.ToArray()) child.Close(); settings.Close(); } owner.Close(); }
    }
    [AvaloniaFact]
    public async Task BusyColorApplyProtectsWindowsAndPreservesLetter()
    {
        var store = new DemoOrderStore(); var editor = new WaitingColorEditor(store);
        var model = new MainWindowViewModel(store, store, editor); var owner = new MainWindow { DataContext = model };
        RowColorWindow? window = null; Task? applying = null;
        try
        {
            owner.Show(); await owner.LoadInitialOrdersAsync(); Pump(); model.ResultText = "Письмо";
            model.UpdateSelection([model.Orders[0]], model.Orders[0]);
            var form = model.CreateRowColorForm()!; form.Code = "#AABBCC";
            window = new RowColorWindow { DataContext = form };
            var shown = window.ShowDialog(owner); Pump(); applying = form.ConfirmCommand.ExecuteAsync(null);
            Assert.True(model.IsUpdatingOrders); Assert.True(form.IsBusy); window.Close(); owner.Close();
            Assert.True(window.IsVisible); Assert.True(owner.IsVisible);
            editor.Done.SetResult(); await applying; await shown; Pump(); Assert.False(model.IsUpdatingOrders); Assert.Equal("Письмо", model.ResultText);
        }
        finally { editor.Done.TrySetResult(); if (applying is not null) await applying; window?.Close(); owner.Close(); }
    }
    [AvaloniaFact]
    public void ListSaveFailureKeepsDialogInputAndExistingSuggestions()
    {
        var store = new FailingStore(); var model = new MainWindowViewModel(new DemoOrderStore()); var owner = new MainWindow(store) { DataContext = model };
        try
        {
            owner.Show(); Pump(); store.Fail = true;
            var form = new EntrySuggestionsViewModel(model.CaptureEntrySuggestions(), owner.SaveEntrySuggestions) { ManagersText = "Новый менеджер" };
            form.SaveCommand.Execute(null);
            Assert.True(form.HasError); Assert.Equal("Новый менеджер", form.ManagersText); Assert.Empty(model.CaptureEntrySuggestions().Managers); Assert.Null(store.Saved);
        }
        finally { store.Fail = false; owner.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task AutocompleteKeepsFreeInputAndUsesSharedInputSurface(bool dark)
    {
        var field = new OrderEntryField("Менеджер") { Suggestions = new[] { "Иван", "Пётр" } };
        var entry = new OrderEntryControl { DataContext = field };
        var window = new Window { Content = entry, Width = 640, Height = 120, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); var autocomplete = entry.FindControl<AutoCompleteBox>("SuggestionsInput")!;
            Assert.True(autocomplete.IsVisible); Assert.False(entry.FindControl<TextBox>("Input")!.IsVisible);
            Assert.False(autocomplete.IsTextCompletionEnabled); Assert.Equal(AutoCompleteFilterMode.Contains, autocomplete.FilterMode);
            var text = Assert.Single(autocomplete.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(14, text.FontSize);
            Assert.Equal(((ISolidColorBrush)Application.Current!.FindResource(window.ActualThemeVariant, "JournalInput")!).Color, ((ISolidColorBrush)text.Background!).Color);
            text.Focus(); text.Text = "ва"; Pump(); await Task.Delay(30, TestContext.Current.CancellationToken); Pump();
            Assert.True(autocomplete.IsDropDownOpen);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Pump();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump();
            Assert.Equal("Иван", field.Value);
            text.Text = "Свободное имя"; Pump(); Assert.Equal("Свободное имя", field.Value);
            field.Value = "Иван"; Pump(); Assert.Equal("Иван", text.Text);
            Save(window, "autocomplete-" + dark);
            autocomplete.Text = null; Pump(); Assert.Empty(field.Value);
        }
        finally { window.Close(); }
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void CheckButtons(Window window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
        { var point = button.TranslatePoint(default, window)!.Value; Assert.True(point.X >= 0 && point.Y >= 0 && point.X + button.Bounds.Width <= window.ClientSize.Width + 0.1 && point.Y + button.Bounds.Height <= window.ClientSize.Height + 0.1); }
    }
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory); using var image = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); image.Render(window); image.Save(Path.Combine(directory, "daily-" + name + ".png"), new PngBitmapEncoderOptions());
    }
    private sealed class FailingStore : IUiPreferencesStore
    {
        public bool Fail; public UiPreferences? Saved;
        public PreferencesLoadResult Load() => new(new());
        public void Save(UiPreferences preferences) { if (Fail) throw new IOException("Тестовый отказ"); Saved = preferences; }
    }
    private sealed class WaitingColorEditor(DemoOrderStore store) : IOrderBatchEditor
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color, CancellationToken cancellationToken = default)
        { await Done.Task.WaitAsync(cancellationToken); return await store.SetColorAsync(expected, color, cancellationToken); }
        public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number, CancellationToken cancellationToken = default) =>
            store.SetDeliveryNumberAsync(expected, number, cancellationToken);
    }
}
