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
using Jornal.Desktop.Customization;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

public sealed class SettingsWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, 900, 660)]
    [InlineData(true, 900, 660)]
    [InlineData(false, 800, 580)]
    [InlineData(true, 800, 580)]
    public void AllSectionsFitAndKeyboardNavigationWorks(bool dark, int width, int height)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-settings-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient();
        var service = new DiagnosticService(new(root), http, null, "2026.1.0-dev");
        var (journal, _, _) = OrderSourceTests.Create(); journal.IsDarkTheme = dark;
        var model = new SettingsViewModel(journal, service);
        var window = new SettingsWindow { DataContext = model, Width = width, Height = height };
        try
        {
            window.Show(); Pump();
            var nav = window.FindDescendant<ListBox>("SettingsNavigation")!;
            Assert.Equal(5, nav.ItemCount); Assert.Equal(0, nav.SelectedIndex);
            nav.ContainerFromIndex(0)!.Focus(); window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Pump();
            Assert.Equal(1, nav.SelectedIndex); Assert.True(model.IsDatabase);
            for (var index = 0; index < 5; index++)
            {
                nav.SelectedIndex = index;
                if (index == 1) model.Database!.UseDatabase = true;
                Pump();
                Assert.Equal(index, model.SelectedIndex);
                Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
                foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(control => control.IsEffectivelyVisible))
                    InBounds(window, button);
                InBounds(window, nav); InBounds(window, window.FindDescendant<Control>("CloseSettingsButton")!);
                Save(window, $"settings-{index}-{width}-{dark}");
            }
            model.SelectedIndex = 3; Pump();
            Click(window, "AutomaticReportsCheckBox");
            Assert.False(service.SendAutomatically);
            Assert.False(new DiagnosticStore(root).LoadAutomatic());
            model.SelectedIndex = 0; model.SelectedIndex = 3; Pump();
            Assert.False(window.FindDescendant<CheckBox>("AutomaticReportsCheckBox")!.IsChecked);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); service.Dispose(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void GearOpensGeneralThemeIsSharedAndCustomizationReturnsToTheJournal()
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { Layout = new() { RowHeight = 24 }, DarkTheme = false });
        var (journal, _, _) = OrderSourceTests.Create();
        var owner = new MainWindow(preferences) { DataContext = journal };
        try
        {
            owner.Show(); Pump();
            Click(owner, "SettingsButton");
            var settings = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            Assert.True(((SettingsViewModel)settings.DataContext!).IsGeneral);
            Click(settings, "SettingsDarkThemeCheckBox");
            Assert.True(journal.IsDarkTheme);
            Assert.Equal(ThemeVariant.Dark, owner.ActualThemeVariant);
            Assert.Equal(ThemeVariant.Dark, settings.ActualThemeVariant);
            Click(settings, "CustomizeFromSettingsButton");
            Assert.False(settings.IsVisible); Assert.True(journal.IsCustomizing);
            Assert.Empty(owner.OwnedWindows);
            owner.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(journal.IsCustomizing);
            Assert.Equal(24, owner.FindDescendant<DataGrid>("OrdersGrid")!.RowHeight);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        Assert.True(preferences.Value!.DarkTheme); Assert.Equal(24, preferences.Value.Layout.RowHeight);
    }

    [AvaloniaFact]
    public async Task DatabaseDraftSurvivesNavigationAndOnlyConnectAppliesIt()
    {
        var (journal, saved, _) = OrderSourceTests.Create();
        var owner = new MainWindow { DataContext = journal };
        try
        {
            owner.Show(); Pump();
            journal.ResultText = "Черновик письма";
            var before = journal.Orders.ToArray();
            Click(owner, "SettingsButton");
            var window = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            var model = (SettingsViewModel)window.DataContext!;
            model.SelectedIndex = 1; Pump();
            model.Database!.UseDatabase = true;
            model.Database.DatabasePath = "неполный путь"; Pump(); Click(window, "ConnectDatabaseButton");
            Assert.True(model.Database.HasError); Assert.Null(saved.Saved);
            model.SelectedIndex = 2; model.SelectedIndex = 1; Pump();
            Assert.Equal("неполный путь", window.FindDescendant<TextBox>("DatabasePathInput")!.Text);
            Assert.True(window.FindDescendant<Control>("DatabaseConnectionError")!.IsEffectivelyVisible);
            Click(window, "CloseSettingsButton");
            Assert.Null(journal.DatabasePath); Assert.Equal(before, journal.Orders); Assert.Equal("Черновик письма", journal.ResultText);
            Click(owner, "SettingsButton");
            window = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            model = (SettingsViewModel)window.DataContext!;
            Assert.True(model.Database!.UseDemo);
            model.SelectedIndex = 1; model.Database.UseDatabase = true; model.Database.DatabasePath = OrderSourceTests.DatabasePath; Pump();
            Click(window, "ConnectDatabaseButton"); await model.Database.ConnectCommand.ExecutionTask!; Pump();
            Assert.True(model.Database.IsConnected, model.Database.Error);
            Assert.True(window.IsVisible); Assert.Equal(OrderSourceTests.DatabasePath, saved.Saved!.DatabasePath);
            Assert.Equal(journal.SourceDescription, window.FindDescendant<TextBlock>("CurrentSourceLabel")!.Text);
            model.Database.DatabasePath = "Новый черновик";
            Assert.False(model.Database.IsConnected);
            model.SelectedIndex = 4; Pump(); Assert.True(model.IsAbout);
            Click(window, "CloseSettingsButton");
            Assert.Equal(OrderSourceTests.DatabasePath, journal.DatabasePath);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task SettingsRemainAvailableWhenTheDatabaseCannotBeRead()
    {
        var saved = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: OrderSourceTests.DatabasePath)) };
        var journal = OrderSourceTests.Model(new OrderSourceManager(saved, _ => new OrderSourceTests.Reader { Fail = true }));
        var owner = new MainWindow { DataContext = journal };
        try
        {
            owner.Show(); await journal.ReloadCommand.ExecutionTask!; Pump();
            Assert.True(journal.HasError);
            Assert.True(owner.FindDescendant<JournalButton>("SettingsButton")!.IsEffectivelyEnabled);
            Click(owner, "SettingsButton");
            var window = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            var model = (SettingsViewModel)window.DataContext!;
            model.SelectedIndex = 1; Pump();
            Assert.Equal(OrderSourceTests.DatabasePath, window.FindDescendant<TextBox>("DatabasePathInput")!.Text);
            Assert.True(window.FindDescendant<JournalButton>("BrowseDatabaseButton")!.IsEffectivelyEnabled);
            Click(window, "CloseSettingsButton");
            Assert.True(journal.HasError);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task InstallingFromSettingsProtectsTheWindowUntilRestartIsScheduledAndPreservesDraft()
    {
        var updater = new AppUpdatesTests.FakeUpdater();
        var updates = new AppUpdatesViewModel(updater);
        var draft = new RestartStore();
        var journal = new MainWindowViewModel(new DemoOrderStore(), appUpdates: updates);
        var owner = new MainWindow(new MemoryUiPreferencesStore(), draft) { DataContext = journal };
        try
        {
            owner.Show(); Pump(); journal.ResultText = "Текст для письма";
            await updates.CheckCommand.ExecuteAsync(null); await updates.DownloadCommand.ExecuteAsync(null);
            Click(owner, "UpdatesButton");
            var window = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            updater.OnRestart = () =>
            {
                Assert.False(((SettingsViewModel)window.DataContext!).CanClose);
                Assert.False(window.FindDescendant<ListBox>("SettingsNavigation")!.IsEffectivelyEnabled);
                window.Close(); Assert.True(window.IsVisible);
            };
            Click(window, "InstallUpdateButton");
            Assert.False(updates.HasError, updates.Error);
            Assert.Equal(1, updater.Restarts); Assert.True(updates.RestartScheduled);
            Assert.False(window.IsVisible); Assert.False(owner.IsVisible);
            Assert.Equal("Текст для письма", draft.Saved!.ResultText);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    private sealed class RestartStore : IUpdateRestartStore
    {
        public UpdateRestartDraft? Saved { get; private set; }
        public void Save(UpdateRestartDraft draft) => Saved = draft;
        public UpdateRestartDraft? Take() => null;
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, string name)
    {
        var control = window.FindDescendant<Control>(name)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void InBounds(Window window, Control control)
    {
        var rect = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
        Assert.True(rect.Left >= -1 && rect.Top >= -1 && rect.Right <= window.ClientSize.Width + 1 && rect.Bottom <= window.ClientSize.Height + 1,
            $"{control.Name}: {rect}, окно {window.ClientSize}.");
    }
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
