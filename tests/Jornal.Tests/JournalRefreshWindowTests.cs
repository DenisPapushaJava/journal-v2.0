using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Local;
using Jornal.Core.Orders;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalRefreshWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task VisibleInactiveWindowReceivesExternalColorAndRepaintsTheRow(bool restoreFromMinimized)
    {
        var refresh = new ControlledRefresh();
        using var fixture = new LocalJournalFixture();
        var original = JournalPeriodTests.Row(42, DateOnly.FromDateTime(DateTime.Today)) with { Color = unchecked((int)0xFF42C6FF) };
        await LocalJournalStore.InitializeAsync(fixture.Path, [original], LocalJournalFixture.Prices, "synthetic.mdb", new string('a', 64),
            TestContext.Current.CancellationToken);
        var store = new LocalJournalStore(fixture.Path);
        var model = new MainWindowViewModel(store, store, store);
        model.InitializeSource(new(fixture.Path, store, store, store, IsLocalCopy: true)); model.InitializeStartupPeriod();
        var window = refresh.Window(model); window.ShowActivated = false;
        var other = new Window { Width = 200, Height = 100 };
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            window.FindControl<JournalSearchBox>("SearchBox")!.Focus(); Pump();
            other.Show(); other.Activate(); Pump();
            await refresh.Tick(window);
            Assert.True(window.IsVisible); Assert.False(window.IsActive);
            if (restoreFromMinimized) { window.WindowState = WindowState.Minimized; Pump(); }
            const int newColor = unchecked((int)0xFFE4BC88);
            await new LocalJournalStore(fixture.Path).SetColorAsync([original], newColor, TestContext.Current.CancellationToken);
            refresh.Signal(); refresh.Clock.Advance(16);
            if (restoreFromMinimized)
            {
                await refresh.Tick(window);
                Assert.Equal(original.Color, model.Orders.Single().Color);
                window.WindowState = WindowState.Normal; Pump();
            }
            await refresh.Tick(window);
            Assert.Equal(newColor, Assert.Single(model.Orders).Color);
            var row = window.FindControl<DataGrid>("OrdersGrid")!.GetVisualDescendants().OfType<DataGridRow>().Single(control => control.IsVisible);
            Assert.Equal(Color.FromUInt32(unchecked((uint)newColor)), Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);
            Assert.False(window.IsActive); Assert.True(model.IsTodaySelected);
        }
        finally { other.Close(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyTodayRefreshesWithFocusInAnEmptyLetter(string text)
    {
        var refresh = new ControlledRefresh();
        var directory = Path.Combine(Path.GetTempPath(), "journal-empty-today-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.mdb"); File.WriteAllText(path, "synthetic");
        var reader = new JournalPeriodTests.Reader { Rows = [] };
        var model = new MainWindowViewModel(reader);
        model.InitializeSource(new(path, reader)); model.InitializeStartupPeriod();
        var window = refresh.Window(model);
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
            await refresh.Tick(window);
            Assert.Empty(model.Orders); Assert.Single(reader.Days);
            var letter = window.FindControl<TextBox>("ResultTextBox")!;
            letter.Focus(); letter.Text = text; Pump(); Assert.True(letter.IsKeyboardFocusWithin);
            reader.Rows = [JournalPeriodTests.Row(42, DateOnly.FromDateTime(DateTime.Today))];
            File.AppendAllText(path, "changed");
            refresh.Signal(); refresh.Clock.Advance(15); await refresh.Tick(window);
            Assert.Equal(42, Assert.Single(model.Orders).Id);
            Assert.True(model.IsTodaySelected); Assert.True(letter.IsKeyboardFocusWithin);
            Assert.Equal(text, model.ResultText); Assert.Equal(0, reader.FullCalls);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task OpenColumnFilterKeepsItsDraftUntilTheUserClosesIt()
    {
        var refresh = new ControlledRefresh();
        var directory = Path.Combine(Path.GetTempPath(), "journal-refresh-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.mdb"); File.WriteAllText(path, "watch-test");
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        model.InitializeSource(new(path, reader));
        var window = refresh.Window(model);
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
            var header = window.GetVisualDescendants().OfType<ColumnFilterHeader>().First();
            header.ShowFilter(); Pump(); Assert.True(header.IsFilterOpen);
            await refresh.Tick(window); refresh.Clock.Advance(16); await refresh.Tick(window);
            Assert.Equal(1, reader.FullCalls); Assert.True(header.IsFilterOpen);
            var button = header.GetVisualDescendants().OfType<Button>().Single(value => value.Flyout is not null);
            button.Flyout!.Hide();
            var letter = window.FindControl<TextBox>("ResultTextBox")!;
            letter.Focus(); letter.Text = "Ручной ввод письма"; Pump();
            refresh.Clock.Advance(1); await refresh.Tick(window);
            Assert.Equal(1, reader.FullCalls); Assert.True(letter.IsEffectivelyEnabled);
            Assert.Equal("Ручной ввод письма", model.ResultText);
            window.FindControl<DataGrid>("OrdersGrid")!.Focus();
            await refresh.Tick(window);
            Assert.Equal(2, reader.FullCalls);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(false, "column")] [InlineData(true, "column")]
    [InlineData(false, "status")] [InlineData(true, "status")]
    [InlineData(false, "font")] [InlineData(true, "font")]
    [InlineData(false, "color")] [InlineData(true, "color")]
    public async Task PopupsKeepPausingRefreshAfterThemeVisibilityAndTemplateChanges(bool dark, string popup)
    {
        var refresh = new ControlledRefresh();
        using var fixture = new LocalJournalFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Path)!); File.WriteAllText(fixture.Path, "synthetic");
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        model.InitializeSource(new(fixture.Path, reader));
        var window = refresh.Window(model);
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            var manager = table.Columns.Single(column => column.Header is "Менеджер");
            manager.IsVisible = false; Pump(); manager.IsVisible = true; Pump();
            model.IsDarkTheme = !dark; Pump();
            var template = table.Template; table.Template = null; Pump(); table.Template = template; Pump();
            Func<bool> isOpen; Action close;
            if (popup == "column")
            {
                var header = table.GetVisualDescendants().OfType<ColumnFilterHeader>().Single(item => item.DataContext == model.ManagerFilter);
                var button = header.FindControl<JournalButton>("ColumnFilterButton")!;
                header.ShowFilter(); isOpen = () => header.IsFilterOpen; close = () => button.Flyout!.Hide();
            }
            else if (popup == "status")
            {
                var header = table.GetVisualDescendants().OfType<StatusFilterHeader>().Single();
                var button = header.FindControl<JournalButton>("StatusFilterButton")!;
                header.ShowFilter(); isOpen = () => header.IsFilterOpen; close = () => button.Flyout!.Hide();
            }
            else
            {
                var bar = window.FindControl<ResultFormattingBar>("ResultFormatting")!;
                if (popup == "font")
                {
                    var combo = bar.FindControl<ComboBox>("ResultFontSize")!;
                    combo.IsDropDownOpen = true; isOpen = () => combo.IsDropDownOpen; close = () => combo.IsDropDownOpen = false;
                }
                else
                {
                    var button = bar.FindControl<Button>("ResultColorButton")!;
                    button.Flyout!.ShowAt(button); isOpen = () => button.Flyout!.IsOpen; close = () => button.Flyout!.Hide();
                }
            }
            Pump(); Assert.True(isOpen());
            await refresh.Tick(window); refresh.Clock.Advance(16); await refresh.Tick(window);
            Assert.True(isOpen()); Assert.Equal(1, reader.FullCalls);
            close(); table.Focus(); Pump(); await refresh.Tick(window);
            Assert.Equal(2, reader.FullCalls);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    [Trait("Category", "RealTime")]
    public async Task RealWatcherAndDispatcherTimerRefreshAnEmptyDay()
    {
        using var fixture = new LocalJournalFixture();
        var reader = new JournalPeriodTests.Reader { Rows = [] };
        var model = new MainWindowViewModel(reader);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fixture.Path)!);
        File.WriteAllText(fixture.Path, "synthetic");
        model.InitializeSource(new(fixture.Path, reader)); model.InitializeStartupPeriod();
        var window = new MainWindow { DataContext = model };
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
            window.FindControl<TextBox>("ResultTextBox")!.Focus();
            reader.Rows = [JournalPeriodTests.Row(42, DateOnly.FromDateTime(DateTime.Today))];
            File.AppendAllText(fixture.Path, "changed");
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (model.Orders.Count == 0 && deadline.Elapsed < TimeSpan.FromSeconds(20))
            { await Task.Delay(50, TestContext.Current.CancellationToken); Pump(); }
            Assert.Equal(42, Assert.Single(model.Orders).Id);
            Assert.Equal(0, reader.FullCalls);
        }
        finally { window.Close(); }
    }

    private sealed class ControlledRefresh
    {
        public JournalAutoRefreshTests.Clock Clock { get; } = new();
        private Action? _changed;
        private sealed class Subscription : IDisposable { public void Dispose() { } }
        public MainWindow Window(MainWindowViewModel model) => new(Clock, (_, changed, _) =>
        {
            _changed = changed;
            return Task.FromResult<IDisposable>(new Subscription());
        }) { DataContext = model };
        public void Signal() { Assert.NotNull(_changed); _changed(); Pump(); }
        public async Task Tick(MainWindow window) { Pump(); await window.TickJournalRefreshAsync(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticUpdateRetainsCheckedRowsCustomSortingAndLetter(bool dark)
    {
        var reader = new JournalPeriodTests.Reader();
        var journal = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = journal };
        try
        {
            window.Show(); Pump(); await window.LoadInitialOrdersAsync();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => Equals(control.Content, "Вес, кг"));
            Click(header);
            var sort = grid.CollectionView.SortDescriptions.ToArray();
            var selected = journal.Orders.Take(2).ToArray();
            foreach (var row in selected) grid.SelectedItems.Add(row);
            journal.ResultText = "Письмо с ручными правками";
            var before = selected.Select(row => row.Id).Order().ToArray();
            reader.Rows = reader.Rows.Select(row => row with { WeightKilograms = (row.Id * 100).ToString() }).ToArray();
            Assert.Equal(JournalRefreshOutcome.Refreshed, await journal.RefreshAutomaticallyAsync(TestContext.Current.CancellationToken));
            Pump();
            Assert.Equal(before, grid.SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id).Order());
            Assert.Equal("Выбрано: 2", journal.SelectionText); Assert.Equal(sort, grid.CollectionView.SortDescriptions.ToArray());
            Assert.Equal("Письмо с ручными правками", journal.ResultText); Assert.True(journal.HasCustomSort);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 900, 660)]
    [InlineData(true, 800, 580)]
    public void NewPreferencesFitExistingSectionsAndSurviveLayoutReset(bool dark, int width, int height)
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { DarkTheme = dark });
        var (journal, _, _) = OrderSourceTests.Create();
        var owner = new MainWindow(preferences) { DataContext = journal };
        var model = new SettingsViewModel(journal);
        var settings = new SettingsWindow { DataContext = model, Width = width, Height = height };
        try
        {
            owner.Show(); settings.Show(owner); Pump();
            var retention = settings.FindControl<ComboBox>("TrashRetentionSelector")!;
            retention.BringIntoView(); Pump();
            Assert.Equal(0, retention.SelectedIndex); retention.SelectedIndex = 2;
            Assert.Equal(90, journal.TrashRetentionDays); Save(settings, $"retention-{dark}");
            var automatic = settings.FindControl<CheckBox>("AutomaticJournalRefreshCheckBox")!;
            automatic.BringIntoView(); Pump();
            Assert.True(automatic.IsEffectivelyVisible);
            Assert.True(automatic.IsChecked); Assert.True(automatic.IsEffectivelyEnabled);
            Click(automatic); Assert.False(journal.AutomaticJournalRefresh);
            Save(settings, $"automatic-refresh-{dark}");
            model.SelectedIndex = 1; Pump(); Assert.False(automatic.IsEffectivelyVisible);
            settings.Close();
            Click(owner.FindControl<Control>("PersonalizeButton")!);
            Click(owner.FindControl<Control>("ResetLayoutButton")!);
            Assert.Equal(90, journal.TrashRetentionDays); Assert.False(journal.AutomaticJournalRefresh);
        }
        finally { settings.Close(); owner.Close(); }
        Assert.Equal(90, preferences.Value!.TrashRetentionDays); Assert.False(preferences.Value.AutomaticJournalRefresh);
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Save(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
