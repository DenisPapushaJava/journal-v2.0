using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderRowAppearanceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoredColorsFillRowsAndRemainReadableDuringHoverAndSelection(bool dark)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        int?[] colors = [OrderColors.Work, OrderColors.Ready, OrderColors.Shipped,
            unchecked((int)0xFF54C6CD), unchecked((int)0xFF101010), unchecked((int)0xFF1010FF), null, OrderColors.None];
        var vm = new MainWindowViewModel(new Reader(colors.Select((color, index) => seed with
        {
            Id = index + 1, Color = color, SpecificationNumber = $"1016-{index + 1:0000}",
            CustomerName = index < 3 ? "Стандартный статус" : "Произвольный цвет"
        }).ToArray())) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(window.Icon);
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            foreach (var item in vm.Orders)
            {
                grid.SelectedItem = null;
                window.FindControl<JournalButton>("ReloadButton")!.Focus();
                window.MouseMove(new Point(1, 1));
                grid.ScrollIntoView(item, grid.Columns[1]);
                Dispatcher.UIThread.RunJobs();
                var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(control => control.IsEffectivelyVisible && control.DataContext == item);
                var cell = row.GetVisualDescendants().OfType<DataGridCell>().First(control => control.Content is TextBlock);
                var background = ColorOf(row.Background);
                if (item.Color is int color && color != OrderColors.None)
                    Assert.Equal(Color.FromUInt32(unchecked((uint)color)), background);
                var rowBorder = row.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "RowBorder");
                Assert.Equal(background, ColorOf(rowBorder.Background));
                Assert.True(Contrast(ColorOf(((TextBlock)cell.Content!).Foreground), background) >= 4.5);
                var checkbox = Assert.Single(row.GetVisualDescendants().OfType<CheckBox>(), box => box.Classes.Contains("row-selection"));
                var checkboxBorder = checkbox.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "NormalRectangle");
                Assert.True(Contrast(ColorOf(checkboxBorder.BorderBrush), background) >= 4.5);
                Assert.Equal(item.Id <= 3, item.HasStandardStatus);
                Assert.Equal(item.Color switch
                {
                    OrderColors.Work => "В работе", OrderColors.Ready => "Готово",
                    OrderColors.Shipped => "Отгружено", _ => ""
                }, item.StatusText);

                var point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                Dispatcher.UIThread.RunJobs();
                var hover = ColorOf(row.Background);
                Assert.NotEqual(background, hover);
                Assert.True(Contrast(ColorOf(((TextBlock)cell.Content!).Foreground), hover) >= 4.5);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                cell.Focus();
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsSelected);
                Assert.True(cell.IsFocused);
                var selectedHover = ColorOf(row.Background);
                Assert.NotEqual(hover, selectedHover);
                Assert.True(Contrast(ColorOf(((TextBlock)cell.Content!).Foreground), selectedHover) >= 4.5);
                Assert.False(cell.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "FocusVisual").IsVisible);
                Assert.True(Assert.Single(row.GetVisualDescendants().OfType<CheckBox>(), box => box.Classes.Contains("row-selection")).IsChecked);
                window.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsSelected);
                Assert.NotEqual(background, ColorOf(row.Background));
                Assert.NotEqual(selectedHover, ColorOf(row.Background));
            }
            grid.SelectedItem = null;
            window.FindControl<JournalButton>("ReloadButton")!.Focus();
            grid.ScrollIntoView(vm.Orders.First(), grid.Columns[1]);
            Dispatcher.UIThread.RunJobs();
            Preview(window, $"row-colors-{(dark ? "dark" : "light")}");
            // Выбор следующей строки с клавиатуры остаётся доступен без рамки ячейки.
            grid.SelectedItem = vm.Orders[0];
            var first = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext == vm.Orders[0]);
            first.GetVisualDescendants().OfType<DataGridCell>().First(cell => cell.Content is TextBlock).Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(vm.Orders[1], grid.SelectedItem);
            Preview(window, $"row-selected-{(dark ? "dark" : "light")}");
            grid.ScrollIntoView(vm.Orders.First(), grid.Columns.Last());
            Dispatcher.UIThread.RunJobs();
            var badges = grid.GetVisualDescendants().OfType<StatusBadge>().ToArray();
            Assert.NotEmpty(badges);
            Assert.All(badges, badge => Assert.Equal(((OrderRowViewModel)badge.DataContext!).HasStandardStatus, badge.IsVisible));
            // Переключение темы пересчитывает фон пустых строк без перезагрузки данных.
            vm.IsDarkTheme = !dark;
            Dispatcher.UIThread.RunJobs();
            var unmarked = vm.Orders.Last();
            grid.ScrollIntoView(unmarked, grid.Columns[1]);
            Dispatcher.UIThread.RunJobs();
            var unmarkedRow = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext == unmarked);
            Assert.Equal(ColorOf((IBrush)window.FindResource(window.ActualThemeVariant, "JournalSurface")!), ColorOf(unmarkedRow.Background));
        }
        finally { window.Close(); }
    }

    private sealed class Reader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows);
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static double Contrast(Color first, Color second)
    {
        static double Linear(byte channel) => channel / 255d <= 0.04045 ? channel / 255d / 12.92 : Math.Pow((channel / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return (Math.Max(Luminance(first), Luminance(second)) + 0.05) / (Math.Min(Luminance(first), Luminance(second)) + 0.05);
    }

    private static void Preview(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1366, 768));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
