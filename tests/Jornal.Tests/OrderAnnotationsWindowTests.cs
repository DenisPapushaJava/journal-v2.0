using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderAnnotationsWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ManualFieldsFitFormsAndUpdateDiffAndReadOnlyTable(bool dark)
    {
        var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
        var newWindow = new NewOrderWindow { DataContext = form, Width = 1240, Height = 760,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            newWindow.Show(); Dispatcher.UIThread.RunJobs();
            var bending = newWindow.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "HasBendingCheckBox" && c.IsEffectivelyVisible);
            var comment = newWindow.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "OrderCommentInput" && c.IsEffectivelyVisible);
            bending.IsChecked = true; comment.Text = "По чертежу №2\nНужна проверка";
            Dispatcher.UIThread.RunJobs();
            Assert.True(form.Annotations.HasBending); Assert.Equal(comment.Text, form.Annotations.Comment);
            Assert.Equal(255, comment.MaxLength);
            AssertInside(newWindow, bending); AssertInside(newWindow, comment);
            AssertBendingPosition(newWindow, bending, comment);
            var scroll = newWindow.FindControl<ScrollViewer>("FormScroll")!;
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1);
            Save(newWindow, $"annotations-new-{dark}");
            var clear = newWindow.GetVisualDescendants().OfType<JournalButton>().Single(c => c.Name == "ClearCommentButton" && c.IsEffectivelyVisible);
            AssertInside(newWindow, clear);
            var point = clear.TranslatePoint(new Point(clear.Bounds.Width / 2, clear.Bounds.Height / 2), newWindow)!.Value;
            newWindow.MouseDown(point, MouseButton.Left); newWindow.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal("", form.Annotations.Comment); Assert.Equal("", comment.Text); Assert.False(clear.IsVisible);
            Assert.True(comment.IsFocused);
            Assert.True(form.Annotations.HasBending); Assert.Null(form.CreatedOrder);
        }
        finally { newWindow.Close(); }

        var record = OrderMaintenanceTests.Record();
        record = record with { Annotations = OrderAnnotations.For(record.ToSummary(), true, "По чертежу №2") };
        var edit = new EditOrderViewModel(record, store.Maintenance, "synthetic", _ => Task.FromResult<OrderRecord?>(record));
        var editWindow = new EditOrderWindow { DataContext = edit, Width = 1060, Height = 600,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            editWindow.Show(); Dispatcher.UIThread.RunJobs();
            var bending = editWindow.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "HasBendingCheckBox");
            Assert.True(bending.IsChecked); bending.IsChecked = false;
            Dispatcher.UIThread.RunJobs(); Assert.Contains(edit.Changes, c => c.Label == "Гибка");
            var comment = editWindow.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "OrderCommentInput");
            AssertInside(editWindow, comment);
            AssertBendingPosition(editWindow, bending, comment);
            var scroll = editWindow.FindControl<ScrollViewer>("FormScroll")!;
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1);
            Save(editWindow, $"annotations-edit-{dark}");
            editWindow.GetVisualDescendants().OfType<JournalButton>().Single(c => c.Name == "ClearCommentButton").Command!.Execute(null);
            Assert.Contains(edit.Changes, c => c.Label == "Комментарий" && c.After == "—");
            Assert.Equal("По чертежу №2", record.Annotations!.Comment); Assert.Null(edit.SavedRecord);
        }
        finally { editWindow.Close(); }

        var model = new MainWindowViewModel(new Reader(record.ToSummary())) { IsDarkTheme = dark };
        var preferences = new MemoryUiPreferencesStore();
        var main = new MainWindow(preferences) { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            main.Show(); Dispatcher.UIThread.RunJobs();
            var grid = main.FindControl<DataGrid>("OrdersGrid")!;
            var bending = grid.Columns.Single(c => Equals(c.Header, "Гибка"));
            var comment = grid.Columns.Single(c => Equals(c.Header, "Комментарий"));
            Assert.True(grid.IsReadOnly); Assert.True(bending.IsReadOnly); Assert.True(comment.IsReadOnly);
            grid.ScrollIntoView(model.Orders[0], bending); Dispatcher.UIThread.RunJobs();
            Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "✓");
            grid.ScrollIntoView(model.Orders[0], comment); Dispatcher.UIThread.RunJobs();
            Assert.Contains(grid.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "По чертежу №2");
            Save(main, $"annotations-table-{dark}");
            main.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>().Single(c => Equals(c.Content, "Гибка")).IsChecked = false;
            Assert.False(bending.IsVisible);
        }
        finally { main.Close(); }
        Assert.False(preferences.Value!.Layout.Columns.Single(c => c.Id == "bending").Visible);
        Assert.Contains(preferences.Value.Layout.Columns, c => c.Id == "comment");
    }

    private static void AssertInside(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(point.Y >= 0 && point.Y + control.Bounds.Height <= window.ClientSize.Height);
    }
    private static void AssertBendingPosition(Window window, CheckBox bending, TextBox comment)
    {
        Assert.Equal("Гибка", bending.Content);
        var point = bending.TranslatePoint(default, window)!.Value;
        var commentPoint = comment.TranslatePoint(default, window)!.Value;
        Assert.Equal(commentPoint.X, point.X, 1);
        Assert.True(point.Y + bending.Bounds.Height <= commentPoint.Y);
        var lastFieldBottom = window.GetVisualDescendants().OfType<OrderEntryControl>().Where(control => control.IsEffectivelyVisible)
            .Max(control => control.TranslatePoint(default, window)!.Value.Y + control.Bounds.Height);
        Assert.True(lastFieldBottom <= point.Y);
    }
    private static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
    private sealed class Reader(OrderSummary row) : IOrderReader
    { public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderSummary>>([row]); }
}
