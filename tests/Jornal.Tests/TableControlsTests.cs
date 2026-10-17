using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Jornal.Core.Orders;
using Xunit;

namespace Jornal.Tests;

public sealed class TableControlsTests
{
    [AvaloniaFact]
    public async Task KeyboardSelectAllIncludesOrdersOutsideViewportAndBothScrollbarsAreAvailable()
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var rows = Enumerable.Range(1, 80).Select(id => seed with { Id = id, SpecificationNumber = $"1015-{id:0000}" }).ToArray();
        var vm = new MainWindowViewModel(new FixedReader(rows));
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var bars = grid.GetVisualDescendants().OfType<ScrollBar>().ToArray();
            Assert.Contains(bars, bar => bar.Orientation == Avalonia.Layout.Orientation.Horizontal && bar.IsVisible && bar.Maximum > 0);
            Assert.Contains(bars, bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.IsVisible && bar.Maximum > 0);
            grid.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(80, grid.SelectedItems.Count);
            grid.ScrollIntoView(vm.Orders.Last(), grid.Columns[1]);
            Dispatcher.UIThread.RunJobs();
            var last = grid.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Classes.Contains("row-selection") && box.DataContext is OrderRowViewModel { Id: 80 });
            Assert.True(last.IsChecked);
            Assert.Equal("Выбрано: 80", vm.SelectionText);
        }
        finally { window.Close(); }
    }

    private sealed class FixedReader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows);
    }

    [AvaloniaFact]
    public void RowCheckboxesToggleSelectionAndRespectFilteredOrders()
    {
        var vm = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow { DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            CheckBox Row(int id) => grid.GetVisualDescendants().OfType<CheckBox>()
                .Single(box => box.IsEffectivelyVisible && box.Classes.Contains("row-selection") && box.DataContext is OrderRowViewModel row && row.Id == id);
            Click(window, Row(1));
            Assert.Single(grid.SelectedItems);
            Click(window, Row(2));
            Assert.Equal(2, grid.SelectedItems.Count);
            Assert.Equal("Выбрано: 2", vm.SelectionText);
            Click(window, Row(1));
            Assert.Single(grid.SelectedItems);
            Click(window, Row(2));
            Assert.Empty(grid.SelectedItems);
            vm.SearchText = "Менеджер 1";
            Dispatcher.UIThread.RunJobs();
            Click(window, Row(1));
            Click(window, Row(3));
            Assert.Equal(2, grid.SelectedItems.Count);
            Click(window, Row(1));
            Assert.Single(grid.SelectedItems);
            vm.SearchText = "ничего не найдено";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(grid.SelectedItems);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ColumnsMenuCanHideEverythingAndRemainsReachable(bool dark)
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel(new DemoOrderStore()) { IsDarkTheme = dark } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var button = window.FindControl<JournalButton>("ColumnsButton")!;
            var original = button.TranslatePoint(default, window)!.Value;
            grid.ScrollIntoView(grid.ItemsSource.Cast<OrderRowViewModel>().Last(), grid.Columns.Last());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(original.X, button.TranslatePoint(default, window)!.Value.X);
            Assert.Equal(1, grid.FrozenColumnCount);
            Click(window, button);
            Assert.True(button.Flyout!.IsOpen);
            // Flyout имеет собственный TopLevel, используем настоящие кнопки его окна.
            var hide = window.FindControl<JournalButton>("HideAllColumnsButton")!;
            var checkedOption = window.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>().First();
            var checkBackground = checkedOption.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "NormalRectangle");
            var glyph = checkedOption.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.Name == "CheckGlyph");
            Assert.True(Contrast(BrushColor(glyph.Fill), BrushColor(checkBackground.Background)) >= 4.5);
            SavePreview(TopLevel.GetTopLevel(hide)!, $"columns-menu-{(dark ? "dark" : "light")}");
            Click(TopLevel.GetTopLevel(hide)!, hide);
            Assert.All(grid.Columns.Skip(1), column => Assert.False(column.IsVisible));
            Assert.True(grid.Columns[0].IsVisible);
            Assert.True(button.IsEffectivelyVisible);
            Assert.All(window.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>(), box => Assert.False(box.IsChecked));
            SavePreview(window, $"columns-hidden-{(dark ? "dark" : "light")}");
            var show = window.FindControl<JournalButton>("ShowAllColumnsButton")!;
            Click(TopLevel.GetTopLevel(show)!, show);
            Assert.All(grid.Columns, column => Assert.True(column.IsVisible));
            Assert.All(window.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>(), box => Assert.True(box.IsChecked));
            button.Flyout.Hide();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilterLabelsAreCenteredAndHoverKeepsReadableColors(bool dark)
    {
        var store = new DemoOrderStore();
        var window = new MainWindow { DataContext = new MainWindowViewModel(store, store) { IsDarkTheme = dark } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var filter in window.GetVisualDescendants().OfType<RadioButton>().Where(button => button.Classes.Contains("filter")))
            {
                var text = Assert.Single(filter.GetVisualDescendants().OfType<TextBlock>());
                var center = text.TranslatePoint(new Point(text.Bounds.Width / 2, text.Bounds.Height / 2), filter)!.Value;
                Assert.InRange(Math.Abs(center.X - filter.Bounds.Width / 2), 0, 1);
                Assert.InRange(Math.Abs(center.Y - filter.Bounds.Height / 2), 0, 1);
                if (filter.IsChecked == true)
                {
                    var idleBackground = BrushColor(filter.Background);
                    window.MouseMove(Center(window, filter));
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotEqual(idleBackground, BrushColor(filter.Background));
                    Assert.True(Contrast(BrushColor(filter.Foreground), BrushColor(filter.Background)) >= 4.5);
                }
            }
            var primary = window.FindControl<JournalButton>("NewOrderButton")!;
            var idle = BrushColor(primary.Background);
            var point = Center(window, primary);
            window.MouseMove(point);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(idle, BrushColor(primary.Background));
            var presenter = primary.GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "CommandPresenter");
            Assert.Equal(BrushColor(primary.Background), BrushColor(presenter.Background));
            Assert.True(Contrast(BrushColor(primary.Foreground), BrushColor(primary.Background)) >= 4.5);
            SavePreview(window, $"primary-hover-{(dark ? "dark" : "light")}");
            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(idle, BrushColor(primary.Background));
            Assert.True(Contrast(BrushColor(primary.Foreground), BrushColor(primary.Background)) >= 4.5);
            window.MouseMove(new Point(1, 1), RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(1, 1), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(idle, BrushColor(primary.Background));
            var neutral = window.FindControl<JournalButton>("ReloadButton")!;
            var neutralIdle = BrushColor(neutral.Background);
            window.MouseMove(Center(window, neutral));
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(neutralIdle, BrushColor(neutral.Background));
            Assert.True(Contrast(BrushColor(neutral.Foreground), BrushColor(neutral.Background)) >= 4.5);
            var theme = window.FindControl<ToggleButton>("ThemeButton")!;
            Assert.Equal(66, theme.Bounds.Width);
            theme.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(dark ? ThemeVariant.Light : ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(theme)));
            Assert.Equal(32, window.FindControl<JournalButton>("PersonalizeButton")!.Bounds.Width);
        }
        finally { window.Close(); }
    }

    private static void SavePreview(TopLevel window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    private static Point Center(TopLevel root, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
    private static void Click(TopLevel root, Control control)
    {
        var point = Center(root, control);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static double Contrast(Color foreground, Color background)
    {
        static double Linear(byte channel) => channel / 255d <= 0.04045 ? channel / 255d / 12.92 : Math.Pow((channel / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
