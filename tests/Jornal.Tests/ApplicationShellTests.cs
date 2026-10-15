using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ApplicationShellTests
{
    [AvaloniaTheory]
    [InlineData(1366, 768, false)]
    [InlineData(1366, 768, true)]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1600, 1000, false)]
    [InlineData(1600, 1000, true)]
    public void CommandsStayVisibleAndThemesRender(int width, int height, bool dark)
    {
        var viewModel = new MainWindowViewModel(new DemoOrderReader()) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = viewModel, Width = width, Height = height };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            Assert.Equal(32, grid.RowHeight);
            Assert.Equal(13, grid.FontSize);
            Assert.Equal(15, grid.Columns.Count);

            foreach (var name in new[] { "ResultPanel", "OrdersCommands", "ClipboardCommands", "StatusCommands", "FiltersPanel", "TablePanel", "ColumnsButton" })
            {
                var control = window.FindControl<Control>(name)!;
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1, $"{name}: {bounds}");
            }
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>())
            {
                Assert.True(button.IsVisible);
                var content = (Control)button.Content!;
                Assert.True(content.Bounds.Width <= button.Bounds.Width + 1, $"Обрезана команда: {button.Label}");
            }
            Assert.True(grid.Bounds.Height >= 200);
            Assert.All(grid.GetVisualDescendants().OfType<DataGridCell>(), cell => Assert.Equal(13, cell.FontSize));
            var search = window.FindControl<Control>("SearchControls")!;
            Assert.Equal(0, Grid.GetRow(search));
            Assert.Equal(2, Grid.GetColumn(search));
            Assert.True(window.FindControl<TextBox>("SearchBox")!.Bounds.Width >= 160);
            Assert.False(window.FindControl<JournalButton>("AddResultButton")!.IsEnabled);
            Assert.All(window.GetVisualDescendants().OfType<Avalonia.Svg.Svg>().Where(icon => icon.IsVisible), icon => Assert.NotNull(icon.Model));
            Assert.Contains(window.GetVisualDescendants().OfType<Avalonia.Svg.Svg>(), icon => icon.Path?.EndsWith("/calendar-days.svg", StringComparison.Ordinal) == true);

            var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"shell-{width}-{(dark ? "dark" : "light")}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void StatusDateAndSearchComposeAndReset()
    {
        var viewModel = new MainWindowViewModel(new DemoOrderReader());
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            viewModel.SetStatusFilterCommand.Execute(OrderStatusFilter.Ready);
            Assert.Equal("5083H111", Assert.Single(viewModel.Orders).MaterialName);
            viewModel.FilterDate = new DateTime(2026, 10, 14);
            Assert.Empty(viewModel.Orders);
            Assert.True(viewModel.HasNoOrders);
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindControl<Control>("EmptyState")!.IsVisible);
            viewModel.ResetFiltersCommand.Execute(null);
            Assert.Equal(3, viewModel.Orders.Count);

            var search = window.FindControl<TextBox>("SearchBox")!;
            search.Text = "00010002";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(viewModel.Orders);
            viewModel.SearchScopeIndex = 1;
            Assert.Empty(viewModel.Orders);
            viewModel.SearchScopeIndex = 6;
            search.Text = "2,5";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Ст-3", Assert.Single(viewModel.Orders).MaterialName);
            viewModel.ResetFiltersCommand.Execute(null);
            Assert.Equal(3, viewModel.Orders.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SelectionAndColumnVisibilityFollowUserActions()
    {
        var viewModel = new MainWindowViewModel(new DemoOrderReader());
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.SelectedItems.Add(viewModel.Orders[0]);
            grid.SelectedItems.Add(viewModel.Orders[1]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Выбрано: 2", viewModel.SelectionText);
            var choices = window.FindControl<StackPanel>("ColumnChoices")!;
            var manager = choices.Children.OfType<CheckBox>().Single(option => Equals(option.Content, "Менеджер"));
            manager.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(grid.Columns.Single(column => Equals(column.Header, "Менеджер")).IsVisible);
            viewModel.SearchText = "нет такой заявки";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Выбрано: 0", viewModel.SelectionText);
            Assert.Empty(grid.SelectedItems);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThemeButtonChangesWindowAndStatusColors()
    {
        var viewModel = new MainWindowViewModel(new DemoOrderReader());
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var light = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color;
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.ScrollIntoView(viewModel.Orders[1], grid.Columns.Last());
            Dispatcher.UIThread.RunJobs();
            var badge = window.GetVisualDescendants().OfType<StatusBadge>().First(b => b.Status == "ready");
            Assert.Equal(Avalonia.Media.Color.Parse("#E8F0EB"), Assert.IsAssignableFrom<ISolidColorBrush>(badge.Background).Color);
            var themeButton = window.FindControl<JournalButton>("ThemeButton")!;
            var point = themeButton.TranslatePoint(new Point(themeButton.Bounds.Width / 2, themeButton.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(viewModel.IsDarkTheme);
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.NotEqual(light, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
            Assert.Equal("Тёмная тема", themeButton.Label);
            Assert.Equal(Avalonia.Media.Color.Parse("#293930"), Assert.IsAssignableFrom<ISolidColorBrush>(badge.Background).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardSearchRefreshAndHeaderSortingWork()
    {
        var reader = new CountingReader();
        var viewModel = new MainWindowViewModel(reader);
        var window = new MainWindow { DataContext = viewModel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            window.KeyRelease(Key.F, RawInputModifiers.Control, PhysicalKey.F, null);
            var search = window.FindControl<TextBox>("SearchBox")!;
            Assert.Same(search, window.FocusManager!.GetFocusedElement());
            search.Text = "5083";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(viewModel.Orders);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, viewModel.Orders.Count);
            window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            window.KeyRelease(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, reader.ReadCount);

            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "Дата"));
            var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var first = grid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(row => row.Bounds.Y).First();
            Assert.Equal("30.09.2026", ((OrderRowViewModel)first.DataContext!).DateText);
            var thickness = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "Толщ., мм"));
            point = thickness.TranslatePoint(new Point(thickness.Bounds.Width / 2, thickness.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            first = grid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(row => row.Bounds.Y).First();
            Assert.Equal(2.5m, ((OrderRowViewModel)first.DataContext!).ThicknessMillimeters);
        }
        finally { window.Close(); }
    }

    private sealed class CountingReader : Jornal.Core.Orders.IOrderReader
    {
        public int ReadCount { get; private set; }
        public async Task<IReadOnlyList<Jornal.Core.Orders.OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            var orders = await new DemoOrderReader().ReadAsync(cancellationToken);
            return orders.Select(order => order.Id == 3 ? order with { OrderDate = new DateOnly(2026, 9, 30) } : order).ToArray();
        }
    }
}
