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
            var lists = Assert.Single(settings.OwnedWindows.OfType<EntrySuggestionsWindow>()); lists.Width = 760; lists.Height = 440; Pump();
            Assert.False(settings.CanRestart); var listForm = (EntrySuggestionsViewModel)lists.DataContext!;
            listForm.ManagersText = "Иван\nПётр"; listForm.CustomersText = "ООО, Пример"; listForm.MaterialsText = "Ст3\n09Г2С\nAisi-304\n5083H111\nАмг3";
            Pump(); CheckButtons(lists); Save(lists, "lists-" + dark);
            var materialsInput = lists.FindControl<TextBox>("MaterialsInput")!;
            Assert.True(materialsInput.IsEffectivelyVisible); Assert.True(materialsInput.Bounds.Width >= 220);
            listForm.SaveCommand.Execute(null); Pump(); Assert.False(lists.IsVisible); Assert.Equal(new[] { "Иван", "Пётр" }, store.Value!.EntrySuggestions.Managers);
            Assert.Equal(new[] { "Ст3", "09Г2С", "Aisi-304", "5083H111", "Амг3" }, store.Value.EntrySuggestions.Materials);
            settings.Close(); owner.Close();
            var restored = new MainWindowViewModel(backend, backend, backend); var reopened = new MainWindow(store) { DataContext = restored };
            try
            {
                reopened.Show(); await reopened.LoadInitialOrdersAsync(); Pump(); var newForm = restored.CreateOrderForm()!;
                Assert.Equal("ООО, Пример", Assert.Single(newForm.Customer.Suggestions!));
                Assert.Equal(store.Value.EntrySuggestions.Materials, newForm.Material.Suggestions);
            }
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
            owner.Show(); Pump(); var before = model.CaptureEntrySuggestions(); store.Fail = true;
            var form = new EntrySuggestionsViewModel(model.CaptureEntrySuggestions(), owner.SaveEntrySuggestions) { ManagersText = "Новый менеджер", MaterialsText = "Ст3" };
            form.SaveCommand.Execute(null);
            Assert.True(form.HasError); Assert.Equal("Новый менеджер", form.ManagersText); Assert.Equal(before.Managers, model.CaptureEntrySuggestions().Managers); Assert.Null(store.Saved);
            Assert.Equal("Ст3", form.MaterialsText); Assert.Equal(before.Materials, model.CaptureEntrySuggestions().Materials);
        }
        finally { store.Fail = false; owner.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false, "Менеджер")] [InlineData(true, "Менеджер")]
    [InlineData(false, "Заказчик")] [InlineData(true, "Заказчик")]
    public async Task EditableComboboxMatchesOnlyTheBeginningAndKeepsFreeInput(bool dark, string label)
    {
        var field = new OrderEntryField(label) { Suggestions = new[] { "Роман", "Руслан", "Пётр", "Артём", "Иван" } };
        var entry = new OrderEntryControl { DataContext = field };
        var window = new Window { Content = entry, Width = 640, Height = 120, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump(); var autocomplete = entry.FindControl<EntrySuggestionComboBox>("SuggestionsInput")!;
            Assert.True(autocomplete.IsVisible); Assert.False(entry.FindControl<TextBox>("Input")!.IsVisible);
            Assert.True(autocomplete.IsEditable); Assert.False(autocomplete.IsTextSearchEnabled);
            var text = Assert.Single(autocomplete.GetVisualDescendants().OfType<TextBox>());
            entry.FocusInput(); Pump(); Assert.True(autocomplete.IsKeyboardFocusWithin);
            Assert.Equal(14, text.FontSize);
            var background = autocomplete.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "Background");
            Assert.Equal(((ISolidColorBrush)Application.Current!.FindResource(window.ActualThemeVariant, "JournalInput")!).Color, ((ISolidColorBrush)background.Background!).Color);
            text.Focus(); text.Text = "Р"; Pump(); await Task.Delay(30, TestContext.Current.CancellationToken); Pump();
            Assert.True(autocomplete.IsDropDownOpen);
            Assert.Equal(new[] { "Роман", "Руслан" }, autocomplete.ItemsView.Cast<string>()); Assert.Equal("Р", field.Value);
            text.Text = "рО"; Pump(); await Task.Delay(30, TestContext.Current.CancellationToken); Pump();
            Assert.Equal(new[] { "Роман" }, autocomplete.ItemsView.Cast<string>()); Assert.Equal("рО", field.Value);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Pump();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump();
            Assert.Equal("Роман", field.Value);
            text.Text = "Свободное имя"; Pump(); Assert.Equal("Свободное имя", field.Value);
            field.Value = "Иван"; Pump(); Assert.Equal("Иван", text.Text);
            Save(window, "autocomplete-" + label + "-" + dark);
            autocomplete.Text = null; Pump(); Assert.Empty(field.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "Сплав_X77")] [InlineData(true, "Сплав_X77")]
    [InlineData(false, "Ст3")] [InlineData(true, "Ст3")]
    public void MaterialDropdownShowsAllGradesAndKeepsEditableTextAndImportReview(bool dark, string initial)
    {
        var field = new OrderEntryField("Материал", initial)
        { UseEditableSuggestions = true, Suggestions = new[] { "Ст3", "09Г2С", "Амг3" }, NeedsReview = true };
        var entry = new OrderEntryControl { DataContext = field };
        var window = new Window { Content = entry, Width = 640, Height = 180, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            var combo = entry.FindControl<EntrySuggestionComboBox>("EditableSuggestionsInput")!;
            Assert.True(combo.IsVisible); Assert.True(combo.IsEditable); Assert.Equal(initial, field.Value); Assert.Equal(initial, combo.Text);
            Assert.False(entry.FindControl<EntrySuggestionComboBox>("SuggestionsInput")!.IsVisible);
            Assert.True(field.NeedsReview); entry.FocusInput(); Pump(); Assert.True(combo.IsKeyboardFocusWithin);
            var text = combo.GetVisualDescendants().OfType<TextBox>().Single();
            text.Text = "Ст"; Pump(); Assert.Equal("Ст", field.Value); Assert.False(field.NeedsReview);
            var border = combo.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "Background");
            Assert.Equal(new Thickness(2), border.BorderThickness);
            Assert.Equal(((ISolidColorBrush)Application.Current!.FindResource(window.ActualThemeVariant, "JournalAccent")!).Color, ((ISolidColorBrush)border.BorderBrush!).Color);
            window.MouseMove(text.TranslatePoint(new Point(20, text.Bounds.Height / 2), window)!.Value); Pump();
            Save(window, "material-focused-" + initial + "-" + dark);
            Assert.Equal(new[] { "Ст3" }, combo.ItemsView.Cast<string>());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Click(combo.GetVisualDescendants().OfType<PathIcon>().Single(icon => icon.Name == "DropDownGlyph")); Pump();
            Assert.True(combo.IsDropDownOpen); Assert.Equal("Ст", field.Value);
            var chosen = combo.ContainerFromIndex(2)!; Assert.NotNull(chosen);
            Save(TopLevel.GetTopLevel(chosen)!, "material-dropdown-" + initial + "-" + dark);
            Click(chosen); Pump(); Assert.False(combo.IsDropDownOpen); Assert.Equal("Амг3", field.Value);
            combo.Focus(); Pump();
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); Pump();
            Assert.Equal("Амг3", field.Value);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); Pump();
            Assert.Equal("Амг3", field.Value);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump();
            Assert.Equal("09Г2С", field.Value);
            text.Text = "Своя_марка"; Pump(); Assert.Equal("Своя_марка", field.Value);
            field.Suggestions = new[] { "Ст3", "Aisi-304" }; Pump(); Assert.Equal("Своя_марка", field.Value);
            field.Suggestions = []; Pump(); Assert.True(entry.FindControl<TextBox>("Input")!.IsVisible); Assert.Equal("Своя_марка", field.Value);
            field.Suggestions = new[] { "Ст3", "Aisi-304" }; Pump(); Assert.Equal("Своя_марка", field.Value);
            text.Text = null; Pump(); Assert.Empty(field.Value);
        }
        finally { window.Close(); }
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void CheckButtons(Window window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
        { var point = button.TranslatePoint(default, window)!.Value; Assert.True(point.X >= 0 && point.Y >= 0 && point.X + button.Bounds.Width <= window.ClientSize.Width + 0.1 && point.Y + button.Bounds.Height <= window.ClientSize.Height + 0.1); }
    }
    private static void Save(TopLevel window, string name)
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
