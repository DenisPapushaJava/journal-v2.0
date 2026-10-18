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
        using var fixture = new LocalJournalFixture();
        var original = JournalPeriodTests.Row(42, DateOnly.FromDateTime(DateTime.Today)) with { Color = unchecked((int)0xFF42C6FF) };
        await LocalJournalStore.InitializeAsync(fixture.Path, [original], LocalJournalFixture.Prices, "synthetic.mdb", new string('a', 64),
            TestContext.Current.CancellationToken);
        var store = new LocalJournalStore(fixture.Path);
        var model = new MainWindowViewModel(store, store, store);
        model.InitializeSource(new(fixture.Path, store, store, store, IsLocalCopy: true)); model.InitializeStartupPeriod();
        var window = new MainWindow { DataContext = model, ShowActivated = false };
        var other = new Window { Width = 200, Height = 100 };
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); Pump();
            window.FindControl<JournalSearchBox>("SearchBox")!.Focus(); Pump();
            other.Show(); other.Activate(); Pump();
            Assert.True(window.IsVisible); Assert.False(window.IsActive);
            if (restoreFromMinimized) { window.WindowState = WindowState.Minimized; Pump(); }
            const int newColor = unchecked((int)0xFFE4BC88);
            await new LocalJournalStore(fixture.Path).SetColorAsync([original], newColor, TestContext.Current.CancellationToken);
            if (restoreFromMinimized)
            {
                await Task.Delay(TimeSpan.FromSeconds(16), TestContext.Current.CancellationToken); Pump();
                Assert.Equal(original.Color, model.Orders.Single().Color);
                window.WindowState = WindowState.Normal; Pump();
            }
            for (var attempt = 0; attempt < 400 && model.Orders.Single().Color != newColor; attempt++)
            { await Task.Delay(50, TestContext.Current.CancellationToken); Pump(); }
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
        var directory = Path.Combine(Path.GetTempPath(), "journal-empty-today-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.mdb"); File.WriteAllText(path, "synthetic");
        var reader = new JournalPeriodTests.Reader { Rows = [] };
        var model = new MainWindowViewModel(reader);
        model.InitializeSource(new(path, reader)); model.InitializeStartupPeriod();
        var window = new MainWindow { DataContext = model };
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
            Assert.Empty(model.Orders); Assert.Single(reader.Days);
            var letter = window.FindControl<TextBox>("ResultTextBox")!;
            letter.Focus(); letter.Text = text; Pump(); Assert.True(letter.IsKeyboardFocusWithin);
            reader.Rows = [JournalPeriodTests.Row(42, DateOnly.FromDateTime(DateTime.Today))];
            File.AppendAllText(path, "changed");
            for (var attempt = 0; attempt < 400 && model.Orders.Count == 0; attempt++)
            { await Task.Delay(50, TestContext.Current.CancellationToken); Pump(); }
            Assert.Equal(42, Assert.Single(model.Orders).Id);
            Assert.True(model.IsTodaySelected); Assert.True(letter.IsKeyboardFocusWithin);
            Assert.Equal(text, model.ResultText); Assert.Equal(0, reader.FullCalls);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task OpenColumnFilterKeepsItsDraftUntilTheUserClosesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-refresh-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.mdb"); File.WriteAllText(path, "watch-test");
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        model.InitializeSource(new(path, reader));
        var window = new MainWindow { DataContext = model };
        try
        {
            await window.LoadInitialOrdersAsync(); window.Show(); window.Activate(); Pump();
            var header = window.GetVisualDescendants().OfType<ColumnFilterHeader>().First();
            header.ShowFilter(); Pump(); Assert.True(header.IsFilterOpen);
            await Task.Delay(TimeSpan.FromSeconds(4.5), TestContext.Current.CancellationToken); Pump();
            Assert.Equal(1, reader.FullCalls); Assert.True(header.IsFilterOpen);
            var button = header.GetVisualDescendants().OfType<Button>().Single(value => value.Flyout is not null);
            button.Flyout!.Hide();
            var letter = window.FindControl<TextBox>("ResultTextBox")!;
            letter.Focus(); letter.Text = "Ручной ввод письма"; Pump();
            await Task.Delay(700, TestContext.Current.CancellationToken); Pump();
            Assert.Equal(1, reader.FullCalls); Assert.True(letter.IsEffectivelyEnabled);
            Assert.Equal("Ручной ввод письма", model.ResultText);
            window.FindControl<DataGrid>("OrdersGrid")!.Focus();
            for (var attempt = 0; attempt < 320 && reader.FullCalls == 1; attempt++)
            { await Task.Delay(50, TestContext.Current.CancellationToken); Pump(); }
            Assert.Equal(2, reader.FullCalls);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
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
