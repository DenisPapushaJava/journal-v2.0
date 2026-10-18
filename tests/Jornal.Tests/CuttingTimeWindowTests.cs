using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class CuttingTimeWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TableShowsDurationAndSortsByMinutesInsteadOfText(bool dark)
    {
        var rows = new[] { 1786L, 45L, 6000L }.Select((minutes, i) =>
            (LocalJournalFixture.Draft with { CuttingDurationMinutes = minutes }).ToOrder(i + 1, 500)).ToArray();
        var model = new MainWindowViewModel(new Reader(rows)) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var column = grid.Columns.Single(c => Equals(c.Header, "Время резки"));
            Assert.Equal(nameof(OrderRowViewModel.CuttingDurationMinutes), column.SortMemberPath);
            grid.ScrollIntoView(model.Orders[0], column); Dispatcher.UIThread.RunJobs();
            Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "29:46");
            var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, "Время резки"));
            var point = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            var shown = grid.GetVisualDescendants().OfType<DataGridRow>().Where(r => r.IsVisible && r.Bounds.Height > 0 && r.DataContext is OrderRowViewModel)
                .DistinctBy(r => ((OrderRowViewModel)r.DataContext!).Id).OrderBy(r => r.Bounds.Y).Select(r => ((OrderRowViewModel)r.DataContext!).CuttingDurationMinutes).ToArray();
            Assert.Equal(new long?[] { 45, 1786, 6000 }, shown);
            var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1366, 768));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory, $"time-column-{(dark ? "dark" : "light")}.png"),
                    new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OldColumnProfileKeepsItsOrderAndRelativeWeightsAndCanHideNewTimeColumn()
    {
        string[] ids = ["date", "specification", "manager", "customer", "cutting", "material", "thickness", "sheet-size",
            "sheet-count", "cut-length", "weight", "price", "delivery", "status"];
        string[] headers = ["Дата", "Спецификация", "Менеджер", "Заказчик", "Вид резки", "Материал", "Толщ., мм", "Размер листа",
            "Листы", "Рез, м", "Вес, кг", "Стоимость", "БЛЦБ", "Статус"];
        var store = new MemoryUiPreferencesStore();
        store.Save(new() { Layout = new() { Columns = ids.Select((id, i) => new ColumnPreference(id, 14 - i, id != "manager", false, 200 + i)).ToArray() } });
        var window = new MainWindow(store) { DataContext = new MainWindowViewModel(new Reader([])) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            Assert.Equal(headers.Reverse(), grid.Columns.Skip(1).Where(c => headers.Contains(c.Header))
                .OrderBy(c => c.DisplayIndex).Select(c => (string)c.Header!));
            Assert.False(grid.Columns.Single(c => Equals(c.Header, "Менеджер")).IsVisible);
            var time = grid.Columns.Single(c => Equals(c.Header, "Время резки"));
            Assert.True(time.IsVisible);
            window.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Время резки")).IsChecked = false;
            Dispatcher.UIThread.RunJobs(); Assert.False(time.IsVisible);
        }
        finally { window.Close(); }
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            var saved = store.Value!.Layout.Columns.Single(column => column.Id == id);
            Assert.True(saved.Star); Assert.Equal(200 + index, saved.Width);
        }
        Assert.False(store.Value!.Layout.Columns.Single(c => c.Id == "cutting-time").Visible);
        var reopened = new MainWindow(store) { DataContext = new MainWindowViewModel(new Reader([])) };
        try
        {
            reopened.Show(); Dispatcher.UIThread.RunJobs();
            Assert.False(reopened.FindControl<DataGrid>("OrdersGrid")!.Columns.Single(c => Equals(c.Header, "Время резки")).IsVisible);
        }
        finally { reopened.Close(); }
    }

    private sealed class Reader(IReadOnlyList<OrderSummary> rows) : IOrderReader
    { public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(rows); }
}
