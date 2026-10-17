using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TableReadabilityTests
{
    [AvaloniaTheory]
    [InlineData(false, 24, 1)]
    [InlineData(true, 24, 1)]
    [InlineData(false, 32, 1)]
    [InlineData(true, 32, 1)]
    [InlineData(false, 24, 1.5)]
    [InlineData(true, 24, 1.5)]
    public async Task AlignmentAndTabularDigitsKeepSingleLineRows(bool dark, int height, double scale)
    {
        var (window, model, grid) = await Open(dark, height, scale);
        try
        {
            string[] numeric = ["Толщ., мм", "Листы", "Рез, м", "Вес, кг", "Стоимость", "Время резки"];
            foreach (var column in grid.Columns.Skip(1))
            {
                grid.ScrollIntoView(model.Orders[0], column); Pump();
                var text = Text(grid, model.Orders[0], column);
                var isNumeric = numeric.Contains(column.Header);
                Assert.Equal(isNumeric ? TextAlignment.Right : TextAlignment.Left, text.TextAlignment);
                Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                Assert.Equal(1, text.MaxLines);
                var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => Equals(control.Content, column.Header));
                Assert.Equal(isNumeric ? HorizontalAlignment.Right : HorizontalAlignment.Left, header.HorizontalContentAlignment);
                if (isNumeric)
                {
                    Assert.NotNull(text.FontFeatures);
                    double Width(string digits)
                    {
                        var sample = new TextBlock { Text = digits, FontFamily = text.FontFamily, FontSize = text.FontSize, FontFeatures = text.FontFeatures };
                        sample.Measure(Size.Infinity); return sample.DesiredSize.Width;
                    }
                    Assert.InRange(Math.Abs(Width("11111111") - Width("88888888")), 0, 0.01);
                }
            }
            Assert.Equal(height, grid.RowHeight);
            grid.ScrollIntoView(model.Orders[0], grid.Columns[1]); Pump();
            Save(window, $"readability-{height}-{scale}-{dark}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TruncatedValuesHaveFullWrappedTipsAndResizingRemovesThem(bool dark)
    {
        var (window, model, grid) = await Open(dark);
        try
        {
            var customer = Column(grid, "Заказчик");
            var text = Text(grid, model.Orders[0], customer);
            var cell = text.FindAncestorOfType<DataGridCell>()!;
            Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
            var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(cell));
            var content = Assert.IsType<TextBlock>(tip.Content);
            Assert.Equal(model.Orders[0].CustomerName, content.Text);
            Assert.Equal(TextWrapping.Wrap, content.TextWrapping); Assert.Equal(400, content.MaxWidth);
            Assert.Null(ToolTip.GetTip(Text(grid, model.Orders[1], customer).FindAncestorOfType<DataGridCell>()!));
            Assert.Equal(600, ToolTip.GetShowDelay(cell));
            window.FindControl<TextBox>("SearchBox")!.Focus();
            window.MouseMove(Center(window, cell));
            await Until(() => ToolTip.GetIsOpen(cell));
            await Task.Delay(250, TestContext.Current.CancellationToken); Pump();
            Assert.True(ToolTip.GetIsOpen(cell));
            Assert.True(tip.IsEffectivelyVisible); Assert.True(tip.Bounds.Width > 0 && tip.Bounds.Height > 0);
            Assert.Equal(window.ActualThemeVariant, tip.ActualThemeVariant);
            Save(TopLevel.GetTopLevel(tip)!, $"full-value-tooltip-{dark}");
            SaveTip(tip, $"tooltip-content-{dark}");
            Assert.True(window.FindControl<TextBox>("SearchBox")!.IsKeyboardFocusWithin);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(ToolTip.GetIsOpen(cell));
            ToolTip.SetIsOpen(cell, true); Pump();
            customer.Width = new DataGridLength(650); Pump();
            Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
            Assert.Null(ToolTip.GetTip(cell)); Assert.False(ToolTip.GetIsOpen(cell));
            customer.Width = new DataGridLength(180); Pump();
            Assert.Equal(model.Orders[0].CustomerName, Assert.IsType<TextBlock>(Assert.IsType<ToolTip>(ToolTip.GetTip(cell)).Content).Text);
            window.FindControl<TextBox>("RowHeightInput")!.Text = "48"; Pump();
            Assert.Equal(model.Orders[0].CustomerName, Assert.IsType<TextBlock>(Assert.IsType<ToolTip>(ToolTip.GetTip(cell)).Content).Text);
            model.SearchText = "ничего не найдено"; Pump();
            Assert.False(ToolTip.GetIsOpen(cell));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardFocusShowsFullValueAndEscapeDismissesItWithoutChangingSelection(bool dark)
    {
        var (window, model, grid) = await Open(dark);
        try
        {
            var text = Text(grid, model.Orders[0], Column(grid, "Заказчик"));
            var cell = text.FindAncestorOfType<DataGridCell>()!;
            window.MouseMove(new Point(1, 1));
            Assert.True(cell.Focus(NavigationMethod.Directional)); Pump();
            var selected = grid.SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            await Until(() => ToolTip.GetIsOpen(cell));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(ToolTip.GetIsOpen(cell));
            await Task.Delay(700, TestContext.Current.CancellationToken); Pump();
            Assert.False(ToolTip.GetIsOpen(cell));
            Assert.Equal(selected, grid.SelectedItems.Cast<OrderRowViewModel>().Select(row => row.Id));
            var shortCell = Text(grid, model.Orders[1], Column(grid, "Заказчик")).FindAncestorOfType<DataGridCell>()!;
            Assert.True(shortCell.Focus(NavigationMethod.Directional)); Pump();
            Assert.Null(ToolTip.GetTip(shortCell));
            Assert.False(ToolTip.GetIsOpen(cell));
            Assert.True(cell.Focus(NavigationMethod.Directional)); Pump();
            await Until(() => ToolTip.GetIsOpen(cell));
            Column(grid, "Заказчик").IsVisible = false; Pump();
            Assert.False(ToolTip.GetIsOpen(cell));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ExistingCuttingTimeWarningRemainsAvailableWithoutTruncation()
    {
        var (window, model, grid) = await Open(false, staleTime: true);
        try
        {
            var column = Column(grid, "Время резки");
            grid.ScrollIntoView(model.Orders[0], column); Pump();
            var text = Text(grid, model.Orders[0], column);
            Assert.Equal("—", text.Text);
            Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
            var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(text.FindAncestorOfType<DataGridCell>()!));
            Assert.Equal(model.Orders[0].CuttingTimeHint, Assert.IsType<TextBlock>(tip.Content).Text);
        }
        finally { window.Close(); }
    }

    private static async Task<(MainWindow, MainWindowViewModel, DataGrid)> Open(bool dark, int height = 32, double scale = 1, bool staleTime = false)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var first = seed with { Id = 1, CustomerName = "ООО «Производственная компания Северный блок»", ManagerName = "Менеджер с длинной фамилией",
            SpecificationNumber = "2026-10-17-1205-длинный-номер-спецификации", MaterialName = "5083H111_Материал_с_длинным_названием",
            SheetSize = "1250x2500, 1500x3000, 1500x6000", SheetCount = "12", WeightKilograms = "1250,750", StoredCuttingPrice = 9876543.21m };
        first = first with { CuttingTime = OrderCuttingTime.For(first, 1767) };
        if (staleTime) first = first with { WeightKilograms = "1251" };
        var second = seed with { Id = 2, CustomerName = "Кратко", SpecificationNumber = "0102-1133" };
        var model = new MainWindowViewModel(new Reader([first, second]));
        var settings = new MemoryUiPreferencesStore(); settings.Save(new() { DarkTheme = dark, Layout = new() { RowHeight = height } });
        var window = new MainWindow(settings) { DataContext = model, Width = 1366, Height = 768 };
        window.SetRenderScaling(scale); window.Show(); Pump();
        return (window, model, window.FindControl<DataGrid>("OrdersGrid")!);
    }
    private sealed class Reader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows);
    }
    private static DataGridColumn Column(DataGrid grid, string name) => grid.Columns.Single(column => Equals(column.Header, name));
    private static TextBlock Text(DataGrid grid, OrderRowViewModel row, DataGridColumn column) => grid.GetVisualDescendants().OfType<DataGridRow>()
        .Single(control => control.IsEffectivelyVisible && control.DataContext == row).GetVisualDescendants().OfType<TextBlock>()
        .Single(text => text.IsEffectivelyVisible && ReferenceEquals(DataGridColumn.GetColumnContainingElement(text), column));
    private static Point Center(MainWindow window, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static async Task Until(Func<bool> condition)
    {
        var time = Stopwatch.StartNew();
        while (!condition() && time.Elapsed < TimeSpan.FromSeconds(3)) { await Task.Delay(25, TestContext.Current.CancellationToken); Pump(); }
        Assert.True(condition());
    }
    private static void Save(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
    private static void SaveTip(ToolTip tip, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(tip.Bounds.Size, 1));
        bitmap.Render(tip); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
