using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class EntrySuggestionDefaultsTests
{
    [Fact]
    public void ReleaseIncludesApprovedListsAndReturnsIndependentCopies()
    {
        var defaults = EntrySuggestionDefaults.Create();
        Assert.Equal(new[] { "Вахреева", "Репин" }, defaults.Managers);
        Assert.Equal(new[] { "СТМПО", "НЭК" }, defaults.Customers);
        Assert.Equal(new[] { "Ст-3", "09Г2С", "10ХСНД", "АМГ-3" }, defaults.Materials);
        defaults.Managers[0] = "Личная правка";
        defaults.Customers[0] = "Другой заказчик";
        defaults.Materials[0] = "Другая марка";
        AssertDefaults(new UiPreferences().EntrySuggestions);
        var form = new EntrySuggestionsViewModel(EntrySuggestionDefaults.Create(), _ => null);
        Assert.Equal("Вахреева\nРепин", form.ManagersText);
        Assert.Equal("СТМПО\nНЭК", form.CustomersText);
        Assert.Equal("Ст-3\n09Г2С\n10ХСНД\nАМГ-3", form.MaterialsText);
    }

    [Theory]
    [InlineData("{\"Version\":1,\"DarkTheme\":true}")]
    [InlineData("{\"Version\":1,\"DarkTheme\":true,\"EntrySuggestions\":null}")]
    public void LegacyProfileWithoutListsGetsDefaultsWithoutChangingOtherSettings(string json)
    {
        WithStore((store, path) =>
        {
            File.WriteAllText(path, json);
            var loaded = store.Load();
            Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme);
            AssertDefaults(loaded.Preferences.EntrySuggestions);
            // Однократная миграция панелей записывает профиль; последующие чтения его не меняют.
            Assert.True(loaded.Preferences.Layout.CompactPanelsPresetApplied);
            var migratedJson = File.ReadAllText(path);
            store.Load(); Assert.Equal(migratedJson, File.ReadAllText(path));
            store.Save(loaded.Preferences);
            AssertDefaults(new JsonUiPreferencesStore(path).Load().Preferences!.EntrySuggestions);
        });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReleaseDefaultsNeverReplaceSavedCustomOrIntentionallyEmptyLists(bool empty)
    {
        WithStore((store, path) =>
        {
            var personal = empty ? new EntrySuggestions() : EntrySuggestions.FromText("Другой менеджер", "Личный заказчик", "Сплав_X77");
            store.Save(new() { EntrySuggestions = personal, DarkTheme = true });
            for (var launch = 0; launch < 2; launch++)
            {
                // Новый экземпляр хранилища имитирует запуск после установки/обновления приложения.
                var nextStore = new JsonUiPreferencesStore(path);
                var loaded = nextStore.Load();
                Assert.Null(loaded.Error); Assert.True(loaded.Preferences!.DarkTheme);
                Assert.Equal(personal.Managers, loaded.Preferences.EntrySuggestions.Managers);
                Assert.Equal(personal.Customers, loaded.Preferences.EntrySuggestions.Customers);
                Assert.Equal(personal.Materials, loaded.Preferences.EntrySuggestions.Materials);
                nextStore.Save(loaded.Preferences with { ShowTodayOnStartup = false });
            }
        });
    }

    [AvaloniaFact]
    public async Task FreshInstallUsesDefaultsAndSavedClearedListsStayEmptyOnReopen()
    {
        var preferences = new MemoryUiPreferencesStore();
        var backend = new DemoOrderStore(); var model = new MainWindowViewModel(backend, backend, backend);
        var window = new MainWindow(preferences) { DataContext = model };
        try
        {
            window.Show(); await window.LoadInitialOrdersAsync(); Dispatcher.UIThread.RunJobs();
            AssertDefaults(model.CaptureEntrySuggestions());
            var form = model.CreateOrderForm()!;
            Assert.Equal(EntrySuggestionDefaults.Create().Managers, form.Manager.Suggestions);
            Assert.Equal(EntrySuggestionDefaults.Create().Customers, form.Customer.Suggestions);
            Assert.Equal(EntrySuggestionDefaults.Create().Materials, form.Material.Suggestions);
            Assert.Empty(form.Manager.Value); Assert.Empty(form.Customer.Value); Assert.Empty(form.Material.Value);
            form.Material.Value = "Своя_марка"; Assert.Equal("Своя_марка", form.Material.Value);
            Assert.Null(window.SaveEntrySuggestions(new()));
        }
        finally { window.Close(); }

        var restored = new MainWindowViewModel(backend, backend, backend);
        var reopened = new MainWindow(preferences) { DataContext = restored };
        try
        {
            reopened.Show(); Dispatcher.UIThread.RunJobs();
            var personal = restored.CaptureEntrySuggestions();
            Assert.Empty(personal.Managers); Assert.Empty(personal.Customers); Assert.Empty(personal.Materials);
        }
        finally { reopened.Close(); }
    }

    private static void AssertDefaults(EntrySuggestions value)
    {
        var expected = EntrySuggestionDefaults.Create();
        Assert.Equal(expected.Managers, value.Managers); Assert.Equal(expected.Customers, value.Customers);
        Assert.Equal(expected.Materials, value.Materials);
    }

    private static void WithStore(Action<JsonUiPreferencesStore, string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "jr-list-defaults-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui-settings.json");
            action(new JsonUiPreferencesStore(path), path);
        }
        finally { Directory.Delete(root, true); }
    }
}
