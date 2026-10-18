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
using Jornal.Desktop.Customization;
using Jornal.Desktop.Tables;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderRowAppearanceTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task UncheckingWithMouseRestoresNormalColorWithoutStealingFocus(bool dark, bool colored)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var vm = new MainWindowViewModel(new Reader(Enumerable.Range(1, 3).Select(index => seed with
            { Id = index, Color = colored ? -25089 : null, SpecificationNumber = $"1018-{index:0000}" }).ToArray()))
            { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var items = grid.CollectionView.Cast<OrderRowViewModel>().ToArray();
            var first = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext == items[0]);
            var second = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext == items[1]);
            var box = first.GetVisualDescendants().OfType<CheckBox>().Single();
            var otherBox = second.GetVisualDescendants().OfType<CheckBox>().Single();
            window.MouseMove(new(1, 1)); Dispatcher.UIThread.RunJobs();
            var normal = ColorOf(first.Background);
            Click(window, box); Click(window, otherBox); Click(window, box);
            window.MouseMove(new(1, 1)); Dispatcher.UIThread.RunJobs();
            Assert.False(first.IsSelected); Assert.False(first.IsPointerOver);
            Assert.False(box.IsChecked); Assert.True(second.IsSelected);
            Assert.True(first.IsKeyboardFocusWithin);
            Assert.False(OrderRowFocus.GetKeyboardNavigation(grid));
            Assert.Equal(normal, ColorOf(first.Background));
            Assert.Equal(normal, ColorOf(first.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "RowBorder").Background));
            Assert.DoesNotContain(grid.GetVisualDescendants().OfType<Control>(), control => control.Name == "SelectionChrome");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardFocusStaysVisibleButMouseClickOnTheSameCheckboxClearsItsCue(bool dark)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var vm = new MainWindowViewModel(new Reader([seed with { Id = 1, Color = -25089 }])) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible);
            var box = row.GetVisualDescendants().OfType<CheckBox>().Single();
            window.MouseMove(new(1, 1)); Dispatcher.UIThread.RunJobs();
            var normal = ColorOf(row.Background);
            box.Focus(NavigationMethod.Tab); Dispatcher.UIThread.RunJobs();
            Assert.True(OrderRowFocus.GetKeyboardNavigation(grid)); Assert.True(box.IsFocused);
            Assert.False(row.IsSelected); Assert.NotEqual(normal, ColorOf(row.Background));
            for (var index = 0; index < 2; index++)
            {
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(index == 0, row.IsSelected);
            }
            Assert.NotEqual(normal, ColorOf(row.Background));
            Click(window, box); Click(window, box);
            window.MouseMove(new(1, 1)); Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsFocused); Assert.False(row.IsSelected);
            Assert.False(OrderRowFocus.GetKeyboardNavigation(grid)); Assert.Equal(normal, ColorOf(row.Background));
            var cell = row.GetVisualDescendants().OfType<DataGridCell>().First(cell => cell.Content is TextBlock);
            var point = cell.TranslatePoint(new(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsPointerOver); Assert.NotEqual(normal, ColorOf(row.Background));
            window.MouseMove(new(1, 1)); Dispatcher.UIThread.RunJobs(); Assert.Equal(normal, ColorOf(row.Background));
            // Пробел возвращает клавиатурную подсветку без нового GotFocus.
            for (var index = 0; index < 2; index++)
            {
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(index == 0, row.IsSelected);
            }
            Assert.True(OrderRowFocus.GetKeyboardNavigation(grid)); Assert.True(box.IsFocused);
            cell.Focus(NavigationMethod.Directional); Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsKeyboardFocusWithin); Assert.True(OrderRowFocus.GetKeyboardNavigation(grid));
            grid.SelectedItems.Clear(); Dispatcher.UIThread.RunJobs(); Assert.NotEqual(normal, ColorOf(row.Background));
            window.FindControl<JournalButton>("ReloadButton")!.Focus(NavigationMethod.Pointer);
            Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsKeyboardFocusWithin);
            Assert.Equal(normal, ColorOf(row.Background));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 50)]
    [InlineData(true, 50)]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    public async Task StoredColorsFillRowsAndRemainReadableDuringHoverAndSelection(bool dark, double strength)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        int?[] colors = [OrderColors.Work, OrderColors.Ready, OrderColors.Shipped,
            unchecked((int)0xFFB9D5AC), unchecked((int)0xFF3E566D),
            unchecked((int)0xFF54C6CD), unchecked((int)0xFF101010), unchecked((int)0xFF1010FF), null, OrderColors.None];
        var vm = new MainWindowViewModel(new Reader(colors.Select((color, index) => seed with
        {
            Id = index + 1, Color = color, SpecificationNumber = $"1016-{index + 1:0000}",
            CustomerName = index < 3 ? "Стандартный статус" : "Произвольный цвет"
        }).ToArray())) { IsDarkTheme = dark, RowHighlightStrength = strength };
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
                var surface = ColorOf((IBrush)window.FindResource(window.ActualThemeVariant, "JournalSurface")!);
                if (item.Color is int color && color != OrderColors.None)
                    Assert.Equal(Color.FromUInt32(unchecked((uint)color)), background);
                Assert.Equal(RowAppearance.Resolve(item.Color, false, false, false, strength, surface,
                    ColorOf((IBrush)window.FindResource(window.ActualThemeVariant, "JournalText")!),
                    ColorOf((IBrush)window.FindResource(window.ActualThemeVariant, "JournalRowSelection")!),
                    ColorOf((IBrush)window.FindResource(window.ActualThemeVariant, "JournalRowHover")!)).Background, background);
                Assert.Equal(colors[item.Id - 1], item.Color);
                Assert.Equal(1, row.Opacity);
                var rowBorder = row.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "RowBorder");
                Assert.DoesNotContain(row.GetVisualDescendants().OfType<Border>(), control => control.Name is "SelectionIndicator" or "SelectionOutline");
                var rowBounds = row.Bounds;
                var cellBounds = cell.Bounds;
                Assert.Equal(background, ColorOf(rowBorder.Background));
                Assert.True(Contrast(ColorOf(((TextBlock)cell.Content!).Foreground), background) >= 4.5);
                var checkbox = Assert.Single(row.GetVisualDescendants().OfType<CheckBox>(), box => box.Classes.Contains("row-selection"));
                var checkboxBorder = checkbox.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "NormalRectangle");
                var selectionCell = Assert.Single(checkbox.GetVisualAncestors().OfType<DataGridCell>());
                Assert.Equal(surface, ColorOf(selectionCell.Background));
                Assert.Equal(surface, ColorOf(selectionCell.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "CellBorder").Background));
                Assert.True(Contrast(ColorOf(checkboxBorder.BorderBrush), surface) >= 3);
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
                Assert.Equal(rowBounds, row.Bounds); Assert.Equal(cellBounds, cell.Bounds);
                Assert.Equal(surface, ColorOf(selectionCell.Background));
                Assert.Equal(ColorOf((IBrush)window.FindResource(window.ActualThemeVariant, "JournalAccent")!), ColorOf(checkboxBorder.Background));
                var glyph = checkbox.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(control => control.Name == "CheckGlyph");
                Assert.True(Contrast(ColorOf(glyph.Fill), ColorOf(checkboxBorder.Background)) >= 4.5);
                Assert.NotEqual(hover, selectedHover);
                Assert.True(Contrast(ColorOf(((TextBlock)cell.Content!).Foreground), selectedHover) >= 4.5);
                if (item.Color == unchecked((int)0xFFB9D5AC) && strength == 50)
                    Preview(window, $"row-colored-selected-hover-{(dark ? "dark" : "light")}");
                Assert.False(cell.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "FocusVisual").IsVisible);
                Assert.True(Assert.Single(row.GetVisualDescendants().OfType<CheckBox>(), box => box.Classes.Contains("row-selection")).IsChecked);
                window.MouseMove(new Point(1, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsSelected);
                Assert.NotEqual(background, ColorOf(row.Background));
                Assert.NotEqual(selectedHover, ColorOf(row.Background));
                if (item.Color is null or OrderColors.None && strength == 50)
                    Assert.Equal(Colors.DarkCyan, ColorOf(row.Background));
                if (item.Color == unchecked((int)0xFFB9D5AC) && strength == 50)
                    Preview(window, $"row-colored-selected-{(dark ? "dark" : "light")}");
            }
            grid.SelectedItem = null;
            window.FindControl<JournalButton>("ReloadButton")!.Focus();
            grid.ScrollIntoView(vm.Orders.First(), grid.Columns[1]);
            Dispatcher.UIThread.RunJobs();
            Preview(window, $"row-colors-{(dark ? "dark" : "light")}-{strength}");
            // Выбор следующей строки с клавиатуры остаётся доступен без рамки ячейки.
            var displayed = grid.CollectionView.Cast<OrderRowViewModel>().ToArray();
            grid.ScrollIntoView(displayed[0], grid.Columns[1]);
            grid.SelectedItem = displayed[0];
            Dispatcher.UIThread.RunJobs();
            var first = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext == displayed[0]);
            first.GetVisualDescendants().OfType<DataGridCell>().First(cell => cell.Content is TextBlock).Focus();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(displayed[1], grid.SelectedItem);
            Preview(window, $"row-selected-{(dark ? "dark" : "light")}-{strength}");
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

    [AvaloniaTheory]
    [InlineData(false, 1)]
    [InlineData(true, 1.5)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void ChangingContrastPreservesDenseTableSelectionAndPersistsAcrossScreenReset(bool dark, double scale)
    {
        var store = new MemoryUiPreferencesStore();
        store.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = 24 } });
        var vm = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow(store) { DataContext = vm, Width = 1366, Height = 768 };
        try
        {
            window.SetRenderScaling(scale); window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var items = grid.CollectionView.Cast<OrderRowViewModel>().ToArray();
            grid.SelectedItems.Add(items[0]); grid.SelectedItems.Add(items[1]);
            grid.ScrollIntoView(items[0], grid.Columns[1]); Dispatcher.UIThread.RunJobs();
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().Single(row => row.IsEffectivelyVisible && row.DataContext == items[0]);
            var cell = row.GetVisualDescendants().OfType<DataGridCell>().First(cell => cell.Content is TextBlock);
            cell.Focus(); Dispatcher.UIThread.RunJobs();
            var bounds = row.Bounds; var background = ColorOf(row.Background);
            var resets = 0; vm.Orders.CollectionChanged += (_, _) => resets++;
            vm.RowHighlightStrength = 85; Dispatcher.UIThread.RunJobs();
            Assert.True(cell.IsFocused); Assert.Equal(bounds, row.Bounds);
            Assert.NotEqual(background, ColorOf(row.Background));
            Assert.Equal(new[] { items[0], items[1] }, grid.SelectedItems.Cast<OrderRowViewModel>());
            Assert.Equal(0, resets);
            Assert.DoesNotContain(row.GetVisualDescendants().OfType<Border>(), border => border.Name is "SelectionIndicator" or "SelectionOutline");
            // Высота содержимого прежняя; DataGrid добавляет физическую линию-разделитель.
            Assert.Equal(24, grid.RowHeight);
            Assert.InRange(row.Bounds.Height, 24, 24 + Math.Ceiling(scale) / scale + 0.01);
            Assert.True(Contrast(ColorOf(((TextBlock)cell.Content!).Foreground), ColorOf(row.Background)) >= 4.5);
            Preview(window, $"row-dense-{dark}-{scale}");
            Click(window, "PersonalizeButton"); Click(window, "ResetLayoutButton"); Click(window, "SaveLayoutButton");
            Assert.Equal(85, vm.RowHighlightStrength);
        }
        finally { window.Close(); }
        Assert.Equal(85, store.Value!.RowHighlightStrength);
        var reopened = new MainWindow(store) { DataContext = new MainWindowViewModel(new DemoOrderStore()) };
        try { reopened.Show(); Dispatcher.UIThread.RunJobs(); Assert.Equal(85, ((MainWindowViewModel)reopened.DataContext).RowHighlightStrength); }
        finally { reopened.Close(); }
    }

    private sealed class Reader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows);
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void Click(Window window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        Click(window, control);
    }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }
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
