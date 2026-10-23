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
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class LinkedColumnFilterWindowTests
{
    [AvaloniaTheory]
    [InlineData(OrderFilterColumn.Manager, false, 1)] [InlineData(OrderFilterColumn.Manager, true, 1.5)]
    [InlineData(OrderFilterColumn.CuttingType, false, 1)] [InlineData(OrderFilterColumn.CuttingType, true, 1.5)]
    [InlineData(OrderFilterColumn.Material, false, 1.5)] [InlineData(OrderFilterColumn.Material, true, 1)]
    [InlineData(OrderFilterColumn.Thickness, false, 1)] [InlineData(OrderFilterColumn.Thickness, true, 1.5)]
    [InlineData(OrderFilterColumn.Bending, false, 1.5)] [InlineData(OrderFilterColumn.Bending, true, 1)]
    [InlineData(OrderFilterColumn.Chamfer, false, 1)] [InlineData(OrderFilterColumn.Chamfer, true, 1.5)]
    public void EveryColumnOpensAppliesAndClearsFromVisibleChipWithoutChangingHeaderIdentity(OrderFilterColumn field, bool dark, double scale)
    {
        var (window, model, grid) = Open(dark, scale);
        try
        {
            var filter = model.ColumnFilters.Single(item => item.Column == field);
            var column = grid.Columns.Single(item => Equals(item.Header, filter.Header));
            grid.ScrollIntoView(model.Orders[0], column); Pump();
            var header = grid.GetVisualDescendants().OfType<ColumnFilterHeader>().Single(item => item.DataContext == filter);
            grid.SelectedItems.Add(model.Orders[0]); grid.SelectedItems.Add(model.Orders[1]); Pump(); model.ResultText = "Письмо";
            var before = grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            var flyout = Show(header); var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            Assert.Equal(before, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id)); Assert.Equal("Выбрано: 2", model.SelectionText);
            InBounds(TopLevel.GetTopLevel(content)!, content);
            var all = header.FindControl<CheckBox>("AllValuesCheckBox")!; Click(all);
            var apply = header.FindControl<JournalButton>("ApplyColumnFilterButton")!; Assert.False(apply.IsEnabled);
            var option = filter.Editor.Options.First(item => item.Key.Length > 0);
            var check = Option(content, option.Key); Click(check); Assert.True(apply.IsEnabled); Assert.Null(all.IsChecked);
            Assert.Equal(6, model.Orders.Count); Save(window, $"facet-{field}-{dark}-{scale}");
            Click(apply); Assert.False(flyout.IsOpen); Assert.True(filter.HasFilter); Assert.Empty(grid.SelectedItems); Assert.Equal("Письмо", model.ResultText);
            int[] expected = field switch
            {
                OrderFilterColumn.Manager => [1, 3, 6],
                OrderFilterColumn.CuttingType => [4], OrderFilterColumn.Material => [3],
                OrderFilterColumn.Thickness => [6], _ => [1, 4]
            };
            Assert.Equal(expected, model.Orders.Select(row => row.Id).ToArray());
            var visibleHeader = grid.GetVisualDescendants().OfType<ColumnFilterHeader>().Single(item => item.DataContext == filter);
            var trigger = visibleHeader.FindControl<JournalButton>("ColumnFilterButton")!;
            Assert.Contains("active", trigger.Classes);
            Assert.Equal(((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "JournalAccentSoft")!).Color, ((ISolidColorBrush)trigger.Background!).Color);
            column.IsVisible = false; Pump();
            var chip = Chip(window, filter); InBounds(window, chip); Assert.True(chip.IsEffectivelyVisible); Click(chip);
            Assert.False(filter.HasFilter); Assert.False(model.HasAnyColumnFilter); Assert.Equal(6, model.Orders.Count);
            Assert.Contains(window.FindControl<StackPanel>("ColumnChoices")!.Children.OfType<CheckBox>(), item => Equals(item.Content, filter.Header));
            Assert.Equal(19, grid.Columns.Count);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void PopupSearchKeepsChoicesAndEscapeOutsideBusyAndContextChangesCancelDraft(bool dark)
    {
        var (window, model, grid) = Open(dark);
        try
        {
            var header = Header(grid, model, model.ManagerFilter);
            var flyout = Show(header); var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            Click(header.FindControl<CheckBox>("AllValuesCheckBox")!); Click(Option(content, "Анна"));
            header.FindControl<TextBox>("FilterSearch")!.Text = "Борис"; Pump();
            Assert.Equal("Борис", Assert.Single(model.ManagerFilter.Editor.VisibleOptions).Label);
            Assert.True(model.ManagerFilter.Editor.CanApply); Assert.Equal(6, model.Orders.Count);
            var root = TopLevel.GetTopLevel(content)!;
            root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(flyout.IsOpen); Assert.False(model.ManagerFilter.HasFilter);
            var trigger = header.FindControl<JournalButton>("ColumnFilterButton")!; Assert.True(trigger.IsFocused);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); Pump(); Assert.True(flyout.IsOpen);
            Assert.Equal("", model.ManagerFilter.Editor.SearchText); Assert.True(model.ManagerFilter.Editor.AllChecked);
            Click(header.FindControl<CheckBox>("AllValuesCheckBox")!); Click(window.FindControl<TextBox>("SearchBox")!);
            Assert.False(flyout.IsOpen); Assert.False(model.ManagerFilter.HasFilter);
            Show(header); model.StatusFilter = OrderStatusFilter.Work; Pump(); Assert.False(flyout.IsOpen);
            header = Header(grid, model, model.ManagerFilter); flyout = Show(header);
            Assert.Equal(1, Assert.Single(model.ManagerFilter.Editor.Options).Count);
            model.IsUpdatingOrders = true; Pump(); Assert.False(flyout.IsOpen); Assert.False(trigger.IsEnabled);
            model.IsUpdatingOrders = false; model.ResetFiltersCommand.Execute(null); Pump();
            header = Header(grid, model, model.ManagerFilter); flyout = Show(header);
            var columnHeader = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(item => item.Content is "Менеджер");
            flyout.Hide(); Pump(); var before = grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray();
            ClickAt(window, columnHeader.TranslatePoint(new Point(12, columnHeader.Bounds.Height / 2), window)!.Value);
            Assert.False(flyout.IsOpen); Assert.NotEqual(before, grid.CollectionView.Cast<OrderRowViewModel>().Select(row => row.Id).ToArray());
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 1.5)] [InlineData(true, 1.5)]
    public void ManyActiveFiltersUseOverflowAndRemainClearableForHiddenColumns(bool dark, double scale)
    {
        var rows = LinkedColumnFilterTests.Rows().Select(row => row with
        {
            ManagerName = row.ManagerName + " — длинное имя менеджера", CustomerName = row.CustomerName + " — длинное название заказчика",
            CuttingType = row.CuttingType + " — длинное название станка", MaterialName = row.MaterialName + "_ДлинноеНазваниеМатериала", ThicknessMillimeters = row.Id == 1 ? 12.123456789012345678901234567m : row.ThicknessMillimeters
        }).ToArray();
        var model = new MainWindowViewModel(new LinkedColumnFilterTests.Reader { Items = rows });
        model.ReloadCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        // Готовим независимые черновики до сужения вариантов соседними фильтрами.
        foreach (var filter in model.ColumnFilters)
        {
            filter.BeginEdit();
            foreach (var option in filter.Editor.Options) option.IsChecked = false;
            var choice = filter.Column switch { OrderFilterColumn.Manager => rows[0].ManagerName,
                OrderFilterColumn.CuttingType => rows[0].CuttingType, OrderFilterColumn.Material => rows[0].MaterialName, OrderFilterColumn.Thickness => "12.123456789012345678901234567", _ => "yes" };
            filter.Editor.Options.Single(option => option.Key == choice).IsChecked = true;
        }
        foreach (var filter in model.ColumnFilters) Assert.True(filter.ApplyEdit());
        model.StatusFilter = OrderStatusFilter.Work;
        var (window, _, grid) = Open(dark, scale, model: model);
        try
        {
            var strip = window.FindControl<AppliedFilterStrip>("ActiveColumnFilters")!;
            var table = window.FindControl<Border>("TablePanel")!;
            var viewBar = window.FindControl<Grid>("TableViewBar")!;
            var tableHeight = table.Bounds.Height;
            var tableTop = table.TranslatePoint(default, window)!.Value.Y;
            void AssertStableTable()
            {
                Assert.Equal(32, viewBar.Bounds.Height);
                Assert.Equal(tableHeight, table.Bounds.Height);
                Assert.Equal(tableTop, table.TranslatePoint(default, window)!.Value.Y);
            }
            Assert.Equal(7, strip.Filters!.Count);
            Assert.NotEmpty(strip.OverflowFilters);
            foreach (var chip in strip.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)) InBounds(window, chip);
            var search = window.FindControl<TextBox>("SearchBox")!; Assert.True(search.Bounds.Width >= 160); InBounds(window, search);
            var panel = window.FindControl<Control>("ActiveColumnFilters")!;
            AssertStableTable(); Assert.True(grid.Bounds.Height >= 150);
            foreach (var column in grid.Columns.Skip(1)) column.IsVisible = false; Pump();
            Save(window, $"facets-all-{dark}-{scale}");
            var more = strip.GetVisualDescendants().OfType<JournalButton>().Single(button => button.Name == "MoreFiltersButton");
            Click(more); var flyout = Assert.IsType<Flyout>(more.Flyout); Assert.True(flyout.IsOpen);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content); InBounds(TopLevel.GetTopLevel(content)!, content);
            Save(TopLevel.GetTopLevel(content)!, $"facets-overflow-{dark}-{scale}");
            window.Width = 1920; Pump();
            Assert.Empty(strip.OverflowFilters); Assert.False(more.IsEffectivelyVisible); Assert.False(flyout.IsOpen);
            AssertStableTable();
            Assert.All(strip.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("status-filter-chip")),
                button => Assert.True(button.IsEffectivelyVisible));
            window.Width = 1280; Pump(); Assert.NotEmpty(strip.OverflowFilters); AssertStableTable();
            more.Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); Pump(); Assert.True(flyout.IsOpen);
            var popup = TopLevel.GetTopLevel(content)!;
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(flyout.IsOpen); Assert.True(more.IsKeyboardFocusWithin);
            Click(more); Assert.True(flyout.IsOpen);
            var hiddenChip = content.GetVisualDescendants().OfType<Button>().Single(button => button.DataContext == model.ThicknessFilter);
            Click(hiddenChip); Assert.False(model.ThicknessFilter.HasFilter); AssertStableTable();
            Assert.True(flyout.IsOpen); Assert.DoesNotContain(strip.OverflowFilters, filter => filter.Source == model.ThicknessFilter);
            model.IsUpdatingOrders = true; Pump(); Assert.False(flyout.IsOpen); Assert.False(more.IsEffectivelyEnabled);
            model.IsUpdatingOrders = false; Pump();
            model.ResetFiltersCommand.Execute(null); Pump(); Assert.Empty(strip.Filters!); Assert.False(more.IsEffectivelyVisible);
            Assert.False(flyout.IsOpen); Assert.Equal(6, model.Orders.Count);
            AssertStableTable(); Assert.True(window.FindControl<TextBlock>("DefaultSortText")!.IsEffectivelyVisible);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void LongListsAndNamesUseBoundedScrollAndEmptyJournalStillOpensMenu(bool dark)
    {
        var rows = Enumerable.Range(1, 80).Select(id => LinkedColumnFilterTests.Rows()[0] with
        { Id = id, ManagerName = $"Менеджер {id:D2} «Очень длинное название производственной компании»" }).ToArray();
        var (window, model, grid) = Open(dark, rows: rows);
        try
        {
            var header = Header(grid, model, model.ManagerFilter); var flyout = Show(header);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content); InBounds(TopLevel.GetTopLevel(content)!, content);
            var scroll = header.FindControl<ScrollViewer>("FilterScroll")!; Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.InRange(scroll.Bounds.Height, 100, 272);
            var optionText = content.GetVisualDescendants().OfType<TextBlock>().First(item => item.Text == rows[0].ManagerName);
            Assert.Contains(optionText.TextLayout.TextLines, line => line.HasCollapsed);
            ToolTip.SetIsOpen(optionText, true); Pump();
            Assert.Equal(rows[0].ManagerName, Assert.IsType<TextBlock>(ToolTip.GetTip(optionText)).Text);
            ToolTip.SetIsOpen(optionText, false); Pump();
            Save(window, $"facets-long-{dark}"); flyout.Hide(); Pump();
            model.SearchText = "нет совпадений"; Pump();
            header = Header(grid, model, model.ManagerFilter); Show(header); Assert.Empty(model.ManagerFilter.Editor.Options);
            Assert.False(header.FindControl<JournalButton>("ApplyColumnFilterButton")!.IsEnabled);
            InBounds(window, Assert.IsAssignableFrom<Control>(flyout.Content));
        }
        finally { window.Close(); Pump(); }
    }

    private static (MainWindow Window, MainWindowViewModel Model, DataGrid Grid) Open(bool dark, double scale = 1,
        OrderSummary[]? rows = null, MainWindowViewModel? model = null)
    {
        model ??= new MainWindowViewModel(new LinkedColumnFilterTests.Reader { Items = rows ?? LinkedColumnFilterTests.Rows() });
        var preferences = new MemoryUiPreferencesStore(); preferences.Save(new() { DarkTheme = dark });
        var window = new MainWindow(preferences) { DataContext = model, Width = 1280, Height = 680 };
        window.SetRenderScaling(scale); window.Show(); Pump(); return (window, model, window.FindControl<DataGrid>("OrdersGrid")!);
    }
    private static ColumnFilterHeader Header(DataGrid grid, MainWindowViewModel model, OrderColumnFilterViewModel filter)
    {
        var column = grid.Columns.Single(item => Equals(item.Header, filter.Header));
        if (model.Orders.Count > 0) grid.ScrollIntoView(model.Orders[0], column);
        Pump(); return grid.GetVisualDescendants().OfType<ColumnFilterHeader>().Single(item => item.DataContext == filter);
    }
    private static Button Chip(MainWindow window, OrderColumnFilterViewModel filter) => window.FindControl<Control>("ActiveColumnFilters")!
        .GetVisualDescendants().OfType<Button>().Single(item => item.DataContext == filter && item.Classes.Contains("status-filter-chip"));
    private static Flyout Show(ColumnFilterHeader header)
    {
        var trigger = header.FindControl<JournalButton>("ColumnFilterButton")!; Click(trigger);
        var flyout = Assert.IsType<Flyout>(trigger.Flyout); Assert.True(flyout.IsOpen); return flyout;
    }
    private static CheckBox Option(Control content, string key) => content.GetVisualDescendants().OfType<CheckBox>()
        .Single(item => item.DataContext is ColumnFilterOption option && option.Key == key);
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
        Directory.CreateDirectory(directory); using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(root.ClientSize, 1));
        bitmap.Render(root); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
