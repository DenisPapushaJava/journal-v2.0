using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TableSortingTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DateSortingOrdersEqualDaysByRecordIdAndSurvivesCalendarChanges(bool dark, bool sortBeforeFilter)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var day = new DateOnly(2026, 10, 18);
        OrderSummary[] rows =
        [
            seed with { Id = 30, OrderDate = day, SpecificationNumber = "1018-0900" },
            seed with { Id = 50, OrderDate = day.AddDays(-1) },
            seed with { Id = 2, OrderDate = day, SpecificationNumber = "1018-1700" },
            seed with { Id = 3, OrderDate = null },
            seed with { Id = 10, OrderDate = day, SpecificationNumber = "1018-1200" },
            seed with { Id = 100, OrderDate = day.AddDays(1) },
            seed with { Id = 1, OrderDate = null }
        ];
        int[] ascending = [1, 3, 50, 2, 10, 30, 100];
        var reader = new Reader(rows);
        var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var calendar = window.FindControl<JournalDatePicker>("DateFilter")!;
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => control.Content is "Дата");
            if (sortBeforeFilter)
            {
                Click(window, header);
                Assert.Equal(ascending, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            }
            // Меняем именно календарь: проверяем его привязку, а не только предикат модели.
            calendar.SelectedDate = day.ToDateTime(TimeOnly.MinValue); Pump();
            Assert.Equal(calendar.SelectedDate, model.FilterDate);
            if (!sortBeforeFilter) Click(window, header);
            Assert.Equal(new[] { 2, 10, 30 }, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.Contains(":sortascending", header.Classes);
            var arrow = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.Name == "SortIcon");
            Assert.True(arrow.IsEffectivelyVisible);

            var selected = grid.GetVisualDescendants().OfType<DataGridRow>()
                .Single(row => row.IsEffectivelyVisible && row.DataContext is OrderRowViewModel { Id: 10 });
            Click(window, selected.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Classes.Contains("row-selection")));
            Click(window, header);
            Assert.Equal(new[] { 30, 10, 2 }, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.Contains(":sortdescending", header.Classes);
            Assert.Equal(10, Assert.IsType<OrderRowViewModel>(Assert.Single(grid.SelectedItems)).Id);
            Assert.Equal(new[] { "1018-0900", "1018-1200", "1018-1700" },
                grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.SpecificationNumber));
            Assert.All(model.Orders, row => Assert.Equal("18.10.2026", row.DateText));

            calendar.SelectedDate = day.AddDays(10).ToDateTime(TimeOnly.MinValue); Pump();
            Assert.Empty(grid.CollectionView); Assert.Empty(grid.SelectedItems);
            calendar.SelectedDate = day.ToDateTime(TimeOnly.MinValue); Pump();
            Assert.Equal(new[] { 30, 10, 2 }, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            reader.Rows = rows.Reverse().ToArray();
            await model.ReloadCommand.ExecuteAsync(null); Pump();
            Assert.Equal(new[] { 30, 10, 2 }, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.Contains(":sortdescending", header.Classes);

            calendar.SelectedDate = null; Pump();
            Assert.Equal(ascending.Reverse(), grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Click(window, header);
            Assert.Equal(ascending, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.Contains(":sortascending", header.Classes);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "Вес, кг")]
    [InlineData(true, "Вес, кг")]
    [InlineData(false, "Листы")]
    [InlineData(true, "Листы")]
    public async Task NumericColumnsSortByValueAndShowDirection(bool dark, string title)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var values = new[] { "20", "100", "3", "1,3", "1.03", "-2", "+2", "0",
            "1234567890123456789012345678901234567891", "1234567890123456789012345678901234567890",
            "0.000000000000000000000000000001", "0.000000000000000000000000000002" };
        int[] ascending = [6, 8, 11, 12, 5, 4, 7, 3, 1, 2, 10, 9];
        var rows = values.Select((value, index) => seed with { Id = index + 1, SheetCount = value, WeightKilograms = value }).ToArray();
        var model = new MainWindowViewModel(new Reader(rows)) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => control.Content is string text && text == title);
            var column = grid.Columns.Single(column => column.Header is string text && text == title);
            grid.ScrollIntoView(model.Orders[0], column); Pump();
            var checkbox = grid.GetVisualDescendants().OfType<CheckBox>().First(box => box.IsEffectivelyVisible && box.Classes.Contains("row-selection"));
            Click(window, checkbox);
            var selectedId = Assert.IsType<OrderRowViewModel>(grid.SelectedItem).Id;
            Click(window, header);
            Save(window, $"sorting-{(title == "Листы" ? "sheets" : "weight")}-{dark}");
            var arrow = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(path => path.Name == "SortIcon");
            Assert.True(arrow.IsEffectivelyVisible);
            Assert.NotNull(arrow.Data);
            Assert.Equal(12, arrow.Bounds.Width); Assert.Equal(12, arrow.Bounds.Height);
            var position = arrow.TranslatePoint(default, header)!.Value;
            Assert.True(position.X >= 0 && position.X + arrow.Bounds.Width <= header.Bounds.Width);
            var label = header.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == title);
            Assert.True(label.Bounds.Height <= header.Bounds.Height - 2);
            Assert.True(Contrast(Color(arrow.Fill), Color(header.Background)) >= 3);
            var ascendingArrow = arrow.Data;
            Assert.Contains(":sortascending", header.Classes);
            Assert.Equal(ascending, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.Equal(selectedId, Assert.IsType<OrderRowViewModel>(Assert.Single(grid.SelectedItems)).Id);
            Click(window, header);
            Assert.Equal(ascending.Reverse(), grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.True(arrow.IsEffectivelyVisible);
            Assert.NotSame(ascendingArrow, arrow.Data);
            Assert.Contains(":sortdescending", header.Classes);
            Assert.Equal(selectedId, Assert.IsType<OrderRowViewModel>(Assert.Single(grid.SelectedItems)).Id);
            Assert.Equal(values, model.Orders.Select(row => row.SheetCount));
            Assert.Equal(values.Select(value => value.Replace('.', ',')), model.Orders.Select(row => row.WeightText));
            Click(window, grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => control.Content is "Дата"));
            Assert.False(arrow.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "Вес, кг")]
    [InlineData(true, "Вес, кг")]
    [InlineData(false, "Листы")]
    [InlineData(true, "Листы")]
    public async Task EmptyAndLegacyTextValuesAreUnknownAndAreNeverConvertedToZero(bool dark, string title)
    {
        var seed = (await new DemoOrderStore().ReadAsync()).First();
        var values = new[] { "3", "", "2+1", "0", "NaN", "-1" };
        var rows = values.Select((value, index) => seed with { Id = index + 1, SheetCount = value, WeightKilograms = value }).ToArray();
        var model = new MainWindowViewModel(new Reader(rows)) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => control.Content is string text && text == title);
            Click(window, header);
            var ordered = grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            Assert.Equal(new[] { 2, 3, 5 }, ordered.Take(3).Order());
            Assert.Equal(new[] { 6, 4, 1 }, ordered.Skip(3));
            Click(window, header);
            ordered = grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            Assert.Equal(new[] { 1, 4, 6 }, ordered.Take(3));
            Assert.Equal(new[] { 2, 3, 5 }, ordered.Skip(3).Order());
            Assert.Equal(values, model.Orders.Select(row => row.SheetCount));
            Assert.Equal(values, model.Orders.Select(row => row.WeightText));
        }
        finally { window.Close(); }
    }

    private sealed class Reader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    {
        public IReadOnlyList<OrderSummary> Rows { get; set; } = rows;
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Rows);
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static Color Color(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
        static double Luminance(Color value) => 0.2126 * Channel(value.R) + 0.7152 * Channel(value.G) + 0.0722 * Channel(value.B);
        return (Math.Max(Luminance(first), Luminance(second)) + 0.05) / (Math.Min(Luminance(first), Luminance(second)) + 0.05);
    }
    private static void Click(MainWindow window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Save(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
