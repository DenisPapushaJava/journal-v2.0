using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalPeriodWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    public async Task StartupAndPeriodControlsStayClearAndSearchFits(bool dark, double scale)
    {
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark });
        var reader = new JournalPeriodTests.Reader(); var model = new MainWindowViewModel(reader);
        var window = new MainWindow(preferences) { DataContext = model, Width = 1280, Height = 720 };
        window.SetRenderScaling(scale);
        try
        {
            // Настоящий порядок запуска: настройки/чтение до показа главного окна.
            window.InitializeStartupPeriod(); await window.LoadInitialOrdersAsync();
            window.Show(); Pump();
            var today = window.FindControl<RadioButton>("TodayFilterButton")!;
            var all = window.FindControl<RadioButton>("AllDatesFilterButton")!;
            var date = window.FindControl<JournalDatePicker>("DateFilter")!;
            Assert.True(today.IsChecked); Assert.False(all.IsChecked);
            Assert.Equal(DateTime.Today, date.SelectedDate);
            Assert.Equal(1, Assert.Single(model.Orders).Id);
            Assert.Single(reader.Days); Assert.Equal(0, reader.FullCalls);
            Click(window, today); Assert.True(today.IsChecked); Assert.Single(reader.Days);
            var search = window.FindControl<JournalSearchBox>("SearchBox")!;
            Assert.True(search.Bounds.Width >= 160);
            Assert.True(search.TranslatePoint(default, window)!.Value.X >=
                all.TranslatePoint(default, window)!.Value.X + all.Bounds.Width);
            Save(window, $"period-{dark}-{scale}");
            date.SelectedDate = DateTime.Today.AddDays(-1); await model.OrdersLoadTask; Pump();
            Assert.False(today.IsChecked); Assert.False(all.IsChecked);
            Assert.Equal(2, Assert.Single(model.Orders).Id);
            Click(window, all); await model.OrdersLoadTask; Pump();
            Assert.True(all.IsChecked); Assert.False(today.IsChecked); Assert.Null(date.SelectedDate);
            Assert.Equal(3, model.Orders.Count); Assert.Equal(1, reader.FullCalls);
            Click(window, today); await model.OrdersLoadTask; Pump();
            Assert.True(today.IsChecked); Assert.False(all.IsChecked);
            all.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Pump();
            await model.OrdersLoadTask;
            Assert.True(all.IsChecked); Assert.Null(model.FilterDate);
            // Выбор радиокнопки с клавиатуры должен менять данные, а не только оформление.
            today.IsChecked = true; Pump(); await model.OrdersLoadTask;
            Assert.True(model.IsTodaySelected); Assert.Equal(1, Assert.Single(model.Orders).Id);
            search.Text = "spec-3"; search.SubmitInput(); await model.OrdersLoadTask; Pump();
            Assert.True(all.IsChecked); Assert.Equal(3, Assert.Single(model.Orders).Id);
            Assert.Equal(1, reader.FullCalls);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task GeneralSettingPersistsForNextStartupWithoutChangingCurrentPeriod()
    {
        var preferences = new MemoryUiPreferencesStore();
        var model = new MainWindowViewModel(new JournalPeriodTests.Reader());
        var owner = new MainWindow(preferences) { DataContext = model };
        SettingsWindow? settings = null;
        try
        {
            owner.InitializeStartupPeriod(); await owner.LoadInitialOrdersAsync(); owner.Show(); Pump();
            settings = new SettingsWindow { DataContext = new SettingsViewModel(model) };
            settings.Show(owner); Pump();
            var selector = settings.FindControl<ComboBox>("StartupPeriodSelector")!;
            Assert.Equal(0, selector.SelectedIndex);
            selector.SelectedIndex = 1; Pump();
            Assert.False(model.ShowTodayOnStartup); Assert.True(model.IsTodaySelected);
            settings.Close(); owner.Close();
            Assert.False(preferences.Value!.ShowTodayOnStartup);
            var reader = new JournalPeriodTests.Reader(); var next = new MainWindowViewModel(reader);
            var restarted = new MainWindow(preferences) { DataContext = next };
            try
            {
                restarted.InitializeStartupPeriod(); await restarted.LoadInitialOrdersAsync();
                Assert.True(next.IsAllDatesSelected); Assert.Equal(3, next.Orders.Count);
                Assert.Equal(1, reader.FullCalls); Assert.Empty(reader.Days);
            }
            finally { restarted.Close(); }
        }
        finally { settings?.Close(); owner.Close(); }
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Save(Window window, string name)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), ".local", "journal-period"); Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
