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
using Xunit;

namespace Jornal.Tests;

public sealed class StatusColumnFilterWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false, 1366, 1)] [InlineData(true, false, 1366, 1)]
    [InlineData(false, true, 1280, 1)] [InlineData(true, true, 1280, 1)]
    [InlineData(false, true, 1280, 1.5)] [InlineData(true, true, 1280, 1.5)]
    public void CheckboxDraftAppliesAtomicallyAndHeaderFilterDoesNotSort(bool dark, bool personal, int width, double scale)
    {
        var (window, model, grid, header) = Open(dark, personal, width, scale);
        try
        {
            Assert.Null(window.FindControl<Control>("StatusFilters"));
            Assert.Equal(7, model.Orders.Count); model.ResultText = "Письмо";
            grid.SelectedItems.Add(model.Orders[0]); grid.SelectedItems.Add(model.Orders[1]); Pump();
            var before = grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            var (flyout, content) = Show(header); var popup = TopLevel.GetTopLevel(content)!;
            Assert.Equal(before, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id));
            Assert.Equal(personal ? 5 : 4, model.StatusFilterEditor.Options.Count);
            Assert.NotNull(Assert.Single(header.GetVisualDescendants().OfType<Avalonia.Svg.Svg>(), icon => icon.Path?.EndsWith("/funnel.svg", StringComparison.Ordinal) == true).Model);
            InBounds(popup, content);
            var all = header.FindControl<CheckBox>("AllStatusesCheckBox")!;
            var apply = header.FindControl<JournalButton>("ApplyStatusFilterButton")!;
            Click(all); Assert.False(model.StatusFilterEditor.AllChecked); Assert.False(apply.IsEnabled);
            Assert.Equal(7, model.Orders.Count); Assert.Equal(2, grid.SelectedItems.Count);
            Click(Option(content, OrderStatusFilter.Work)); Click(Option(content, OrderStatusFilter.Ready));
            Assert.Null(all.IsChecked); Assert.True(apply.IsEnabled); Assert.Equal(7, model.Orders.Count);
            Assert.Equal("Выбрано: 2", model.SelectionText);
            Save(popup, $"status-popup-{dark}-{personal}-{width}-{scale}");
            Click(apply); Assert.False(flyout.IsOpen);
            Assert.Equal([1, 2], model.Orders.Select(row => row.Id).ToArray()); Assert.Empty(grid.SelectedItems);
            Assert.Equal("Письмо", model.ResultText);
            Assert.Contains("active", header.FindControl<JournalButton>("StatusFilterButton")!.Classes);
            var visibleHeader = Assert.Single(grid.GetVisualDescendants().OfType<StatusFilterHeader>());
            var activeTrigger = visibleHeader.FindControl<JournalButton>("StatusFilterButton")!;
            Assert.Contains("active", activeTrigger.Classes);
            Assert.Equal(((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "JournalAccentSoft")!).Color, ((ISolidColorBrush)activeTrigger.Background!).Color);
            var chip = window.FindControl<JournalButton>("ActiveStatusFilterButton")!;
            Assert.True(chip.IsVisible); Assert.Equal($"Статус: 2 из {(personal ? 5 : 4)}", chip.Label);
            InBounds(window, chip); InBounds(window, window.FindControl<TextBox>("SearchBox")!);
            Assert.True(window.FindControl<TextBox>("SearchBox")!.Bounds.Width >= 160);
            Assert.Contains(window.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>(), choice => choice.Content is "Статус");
            Save(window, $"status-filtered-{dark}-{personal}-{width}-{scale}");
            Click(chip); Assert.False(model.HasStatusFilter); Assert.Equal(7, model.Orders.Count); Assert.Equal("Письмо", model.ResultText);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void EscapeCancelsDraftAndReturnsFocusWhileHeaderStillSorts(bool dark)
    {
        var (window, model, grid, header) = Open(dark, true);
        try
        {
            grid.SelectedItems.Add(model.Orders[0]); Pump(); model.ResultText = "Письмо";
            var (flyout, content) = Show(header); Click(Option(content, OrderStatusFilter.Ready));
            Assert.Null(model.StatusFilterEditor.AllChecked);
            var popup = TopLevel.GetTopLevel(content)!; popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(flyout.IsOpen); Assert.False(model.HasStatusFilter); Assert.Equal(7, model.Orders.Count);
            Assert.Equal("Выбрано: 1", model.SelectionText); Assert.Equal("Письмо", model.ResultText);
            var trigger = header.FindControl<JournalButton>("StatusFilterButton")!;
            Assert.True(trigger.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(); Assert.True(flyout.IsOpen);
            Assert.All(model.StatusFilterEditor.Options, option => Assert.True(option.IsChecked));
            flyout.Hide(); Pump();
            var columnHeader = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(control => control.Content is "Статус");
            ClickAt(window, columnHeader.TranslatePoint(new Point(12, columnHeader.Bounds.Height / 2), window)!.Value);
            Assert.NotEqual(1, grid.CollectionView.Cast<OrderRowViewModel>().First().Id);
            Assert.Single(grid.SelectedItems);
            Assert.False(flyout.IsOpen);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void HiddenColumnKeepsVisibleClearAndPreservesSearchDateAndPersonalPreferences(bool dark)
    {
        var (window, model, grid, _) = Open(dark, true, 1280);
        try
        {
            model.SearchText = "Технолог"; model.FilterDate = StatusColumnFilterTests.Date.ToDateTime(TimeOnly.MinValue);
            model.StatusFilter = OrderStatusFilter.Mine | OrderStatusFilter.Ready; Pump();
            grid.Columns.Single(column => column.Header is "Статус").IsVisible = false; Pump();
            var chip = window.FindControl<JournalButton>("ActiveStatusFilterButton")!;
            Assert.True(chip.IsEffectivelyVisible); InBounds(window, chip); Click(chip);
            Assert.False(model.HasStatusFilter); Assert.Equal(7, model.Orders.Count); Assert.Equal("Технолог", model.SearchText);
            Assert.NotNull(model.FilterDate); Assert.True(model.UseTechnologistColor); Assert.Equal(StatusColumnFilterTests.PersonalColor, model.TechnologistColorArgb);
            Assert.False(chip.IsVisible); Assert.Empty(grid.SelectedItems);
            Assert.Equal(18, grid.Columns.Count);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void BusyJournalAndChangingColorCloseUnappliedPopup()
    {
        var (window, model, _, header) = Open(false, true);
        try
        {
            var (flyout, content) = Show(header); Click(Option(content, OrderStatusFilter.Work));
            model.IsUpdatingOrders = true; Pump(); Assert.False(flyout.IsOpen); Assert.False(model.HasStatusFilter);
            Assert.False(header.FindControl<JournalButton>("StatusFilterButton")!.IsEnabled);
            model.IsUpdatingOrders = false; Pump(); Show(header);
            model.SelectTechnologistColor(StatusColumnFilterTests.OtherColor); Pump(); Assert.False(flyout.IsOpen);
            var (_, reopened) = Show(header); Assert.True(model.StatusFilterEditor.Options[0].HasColor);
            Click(Option(reopened, OrderStatusFilter.Ready));
            Click(window.FindControl<TextBox>("SearchBox")!); Assert.False(flyout.IsOpen); Assert.False(model.HasStatusFilter);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void EmptyJournalAndMinimumWindowKeepPopupAndResetAccessible(bool dark)
    {
        var model = new MainWindowViewModel(new StatusColumnFilterTests.Reader { Items = [] }) { IsDarkTheme = dark, UseTechnologistColor = true };
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 680 };
        try
        {
            window.Show(); Pump(); var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            foreach (var column in grid.Columns.Skip(1)) if (column.Header is not "Дата" and not "Статус") column.IsVisible = false;
            Pump(); var header = Assert.Single(grid.GetVisualDescendants().OfType<StatusFilterHeader>());
            var (flyout, content) = Show(header); InBounds(TopLevel.GetTopLevel(content)!, content);
            Click(header.FindControl<CheckBox>("AllStatusesCheckBox")!); Click(Option(content, OrderStatusFilter.Mine));
            Click(header.FindControl<JournalButton>("ApplyStatusFilterButton")!); Assert.False(flyout.IsOpen);
            Assert.True(model.HasStatusFilter); Assert.True(window.FindControl<Control>("EmptyState")!.IsVisible);
            var chip = window.FindControl<JournalButton>("ActiveStatusFilterButton")!; InBounds(window, chip); Click(chip);
            Assert.False(model.HasStatusFilter); Assert.Empty(model.Orders);
        }
        finally { window.Close(); Pump(); }
    }

    private static (MainWindow Window, MainWindowViewModel Model, DataGrid Grid, StatusFilterHeader Header) Open(bool dark, bool personal, int width = 1366, double scale = 1)
    {
        var model = new MainWindowViewModel(new StatusColumnFilterTests.Reader()) { IsDarkTheme = dark, UseTechnologistColor = personal };
        var window = new MainWindow { DataContext = model, Width = width, Height = 768 }; window.SetRenderScaling(scale);
        window.Show(); Pump(); var grid = window.FindControl<DataGrid>("OrdersGrid")!;
        grid.ScrollIntoView(model.Orders[0], grid.Columns.Single(column => column.Header is "Статус")); Pump();
        var header = Assert.Single(grid.GetVisualDescendants().OfType<StatusFilterHeader>());
        Assert.Same(model, header.DataContext); return (window, model, grid, header);
    }
    private static (Flyout Flyout, Control Content) Show(StatusFilterHeader header)
    {
        var trigger = header.FindControl<JournalButton>("StatusFilterButton")!; Click(trigger);
        var flyout = Assert.IsType<Flyout>(trigger.Flyout); Assert.True(flyout.IsOpen);
        var content = Assert.IsAssignableFrom<Control>(flyout.Content); return (flyout, content);
    }
    private static CheckBox Option(Control content, OrderStatusFilter choice) => content.GetVisualDescendants().OfType<CheckBox>()
        .Single(box => box.DataContext is StatusFilterOption option && option.Value == choice);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        ClickAt(root, control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value);
    }
    private static void ClickAt(TopLevel root, Point point)
    { root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump(); }
    private static void InBounds(TopLevel root, Control control)
    {
        var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(root)!.Value);
        Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ClientSize.Width + 1 && bounds.Bottom <= root.ClientSize.Height + 1, $"{control.Name}: {bounds}");
    }
    private static void Save(TopLevel root, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1));
        bitmap.Render(root); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
