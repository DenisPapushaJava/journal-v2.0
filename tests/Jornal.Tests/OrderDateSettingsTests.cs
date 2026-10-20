using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderDateSettingsTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("null", true)]
    [InlineData("[]", true)]
    [InlineData("\"false\"", true)]
    public void OldAndMalformedProfilesPreserveOtherPreferences(string? raw, bool locked)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-date-setting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui.json");
            File.WriteAllText(path, "{\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24},\"AutomaticJournalRefresh\":false" +
                (raw is null ? "" : ",\"LockOrderDate\":" + raw) + "}");
            var store = new JsonUiPreferencesStore(path); var loaded = store.Load();
            Assert.Null(loaded.Error); var profile = Assert.IsType<UiPreferences>(loaded.Preferences);
            Assert.Equal(locked, profile.LockOrderDate); Assert.True(profile.DarkTheme);
            Assert.False(profile.AutomaticJournalRefresh); Assert.Equal(24, profile.Layout.RowHeight);
            store.Save(profile); Assert.Equal(locked, store.Load().Preferences!.LockOrderDate);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsApplyToBothFormsAndSurviveScreenResetAndRestart(bool dark)
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { DarkTheme = dark, EntrySuggestions = new() { Managers = ["Иван"] } });
        var store = new DemoOrderStore(); var journal = new MainWindowViewModel(store, store);
        var owner = new MainWindow(preferences) { DataContext = journal };
        try
        {
            owner.Show(); Pump();
            journal.AllDatesCommand.Execute(null); await journal.OrdersLoadTask; Pump();
            var row = journal.Orders[0]; journal.UpdateSelection([row], row);
            Assert.True(journal.LockOrderDate);
            Assert.False(journal.CreateOrderForm()!.OrderDate.IsInputEnabled);
            Assert.False((await journal.CreateEditFormAsync())!.OrderDate.IsInputEnabled);
            using var model = new SettingsViewModel(journal);
            var settings = new SettingsWindow { DataContext = model, Width = 800, Height = 580 };
            settings.Show(owner); Pump();
            var check = settings.FindControl<CheckBox>("LockOrderDateCheckBox")!;
            check.BringIntoView(); Pump(); Assert.True(check.IsChecked); Assert.True(check.IsEffectivelyEnabled);
            check.IsChecked = false; Pump(); Assert.False(journal.LockOrderDate);
            settings.Close(); Pump();

            var form = journal.CreateOrderForm()!;
            Assert.True(form.OrderDate.IsInputEnabled);
            var window = new NewOrderWindow { DataContext = form, RequestedThemeVariant = owner.ActualThemeVariant };
            try
            {
                window.Show(owner); Pump();
                Assert.True(Entry(window, form.OrderDate).IsEffectivelyEnabled);
                Assert.True(Entry(window, form.Manager).IsKeyboardFocusWithin);
                var input = Entry(window, form.OrderDate).FindControl<TextBox>("Input")!;
                input.Text = "01.02.2026"; Assert.Equal("01.02.2026", form.OrderDate.Value);
            }
            finally { window.Close(); }
            var edit = (await journal.CreateEditFormAsync())!;
            var editor = new EditOrderWindow { DataContext = edit, RequestedThemeVariant = owner.ActualThemeVariant };
            try
            {
                editor.Show(owner); Pump();
                Assert.True(Entry(editor, edit.OrderDate).IsEffectivelyEnabled);
                Assert.True(Entry(editor, edit.Manager).IsKeyboardFocusWithin);
                Assert.False(edit.HasChanges);
            }
            finally { editor.Close(); }
            journal.LockOrderDate = true; Pump();
            Assert.False(journal.CreateOrderForm()!.OrderDate.IsInputEnabled);
            Assert.False((await journal.CreateEditFormAsync())!.OrderDate.IsInputEnabled);
            journal.LockOrderDate = false; Pump();
            owner.FindControl<JournalButton>("PersonalizeButton")!.RaiseEvent(new(Button.ClickEvent)); Pump();
            // Сброс компоновки не должен сбрасывать настройки ввода.
            owner.FindControl<JournalButton>("ResetLayoutButton")!.RaiseEvent(new(Avalonia.Controls.Button.ClickEvent)); Pump();
            Assert.False(journal.LockOrderDate);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        Assert.False(preferences.Value!.LockOrderDate);
        var reloaded = new MainWindowViewModel(store, store);
        var reopened = new MainWindow(preferences) { DataContext = reloaded };
        try { reopened.Show(); Pump(); Assert.False(reloaded.LockOrderDate); Assert.True(reloaded.CreateOrderForm()!.OrderDate.IsInputEnabled); }
        finally { reopened.Close(); }
    }

    [AvaloniaFact]
    public void LockedManualFormsFocusTheManagerAndCannotFocusOrTypeInTheDate()
    {
        var form = new NewOrderViewModel(new DemoOrderStore());
        Assert.Empty(form.OrderDate.Value);
        var store = new EditSpecificationImportTests.Store(); var edit = EditSpecificationImportTests.Form(store);
        foreach (var (window, date, manager) in new (Window, OrderEntryField, OrderEntryField)[]
        { (new NewOrderWindow { DataContext = form }, form.OrderDate, form.Manager),
          (new EditOrderWindow { DataContext = edit }, edit.OrderDate, edit.Manager) })
        {
            try
            {
                window.Show(); Pump();
                var input = Entry(window, date).FindControl<TextBox>("Input")!;
                Assert.False(input.IsEffectivelyEnabled); Assert.False(input.Focus());
                Assert.True(Entry(window, manager).IsKeyboardFocusWithin);
            }
            finally { window.Close(); }
        }
        Assert.False(edit.HasChanges); Assert.Equal(0, store.Updates);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportOverridesTheDateAndBatchInheritsItsLockWithoutAnExtraManualCard(bool locked)
    {
        var store = new DemoOrderStore(); var journal = new MainWindowViewModel(store, store) { LockOrderDate = locked };
        var form = journal.CreateOrderForm()!; var preview = await BatchImportTests.Preview();
        form.ImportReader = new BatchImportTests.Reader(preview);
        form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
        await form.ImportFilesAsync(["one.html"], TestContext.Current.CancellationToken, appendToCurrent: true);
        Assert.False(form.IsBatchMode); Assert.Empty(form.BatchItems);
        Assert.Equal(preview.Fields[nameof(form.OrderDate)].Value, form.OrderDate.Value);
        Assert.Equal(!locked, form.OrderDate.IsInputEnabled);
        await form.ImportFilesAsync(["two.html"], TestContext.Current.CancellationToken, appendToCurrent: true);
        Assert.Equal(2, form.BatchItems.Count);
        var window = new NewOrderWindow { DataContext = form };
        try
        {
            window.Show(); Pump();
            Assert.True(Entry(window, form.Manager).IsKeyboardFocusWithin);
            Assert.All(form.BatchItems, item =>
            {
                Assert.Equal(!locked, item.Form.OrderDate.IsInputEnabled);
                Assert.Equal(preview.Fields[nameof(form.OrderDate)].Value, item.Form.OrderDate.Value);
            });
            Assert.All(window.GetVisualDescendants().OfType<OrderEntryControl>().Where(control =>
                control.IsEffectivelyVisible && form.BatchItems.Any(item => item.Form.OrderDate == control.DataContext)),
                control => Assert.Equal(!locked, control.IsEffectivelyEnabled));
        }
        finally { window.Close(); }
        var missingDate = preview.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        missingDate[nameof(form.OrderDate)] = new("", "Не распознано");
        form = journal.CreateOrderForm()!; form.ApplyImport(preview with { Fields = missingDate });
        Assert.Empty(form.OrderDate.Value); Assert.Equal(!locked, form.OrderDate.IsInputEnabled);
    }

    private static OrderEntryControl Entry(Window window, OrderEntryField field) => window.GetVisualDescendants()
        .OfType<OrderEntryControl>().Single(control => control.IsEffectivelyVisible && control.DataContext == field);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
