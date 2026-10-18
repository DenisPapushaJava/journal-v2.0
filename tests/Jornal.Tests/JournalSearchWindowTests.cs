using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalSearchWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 1.5)] [InlineData(true, 1.5)]
    public async Task CompoundSearchKeepsControlsInOneFrameAndAppliesCurrentInputImmediately(bool dark, double scale)
    {
        var reader = new GlobalJournalSearchTests.Reader { Items = GlobalJournalSearchTests.NumberRows() };
        var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 680 };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); Pump();
            var field = window.FindControl<ComboBox>("SearchScopeSelector")!;
            var exact = window.FindControl<ToggleButton>("ExactSearchButton")!;
            var box = window.FindControl<JournalSearchBox>("SearchBox")!;
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var group = window.FindControl<Border>("SearchField")!;
            var glyph = window.FindControl<Avalonia.Svg.Svg>("ExactSearchGlyph")!;
            var inputStart = box.TranslatePoint(default, window)!.Value.X;
            var groupWidth = group.Bounds.Width;
            Assert.False(exact.IsEffectivelyVisible);
            Assert.False(glyph.IsEffectivelyVisible);
            if (scale == 1) Save(window, $"compound-search-all-{dark}");
            field.SelectedIndex = (int)OrderSearchField.DeliveryNumber; Pump();
            Assert.True(exact.IsEffectivelyVisible); Assert.False(model.SearchExactMatch);
            Assert.InRange(Math.Abs(box.TranslatePoint(default, window)!.Value.X - inputStart), 0, 0.1);
            Assert.Equal(groupWidth, group.Bounds.Width);
            Assert.Equal("Полное совпадение", Avalonia.Automation.AutomationProperties.GetName(exact));
            Assert.True(box.Bounds.Width >= 160);
            var fieldPoint = field.TranslatePoint(default, window)!.Value;
            var exactPoint = exact.TranslatePoint(default, window)!.Value;
            var boxPoint = box.TranslatePoint(default, window)!.Value;
            Assert.True(fieldPoint.X + field.Bounds.Width <= boxPoint.X);
            Assert.True(boxPoint.X + box.Bounds.Width <= exactPoint.X);
            Assert.InRange(Math.Abs(exactPoint.Y + exact.Bounds.Height / 2 - boxPoint.Y - box.Bounds.Height / 2), 0, 1);
            Assert.True(boxPoint.X + box.Bounds.Width <= window.ClientSize.Width - 24);
            foreach (var control in new Control[] { field, box, exact })
            {
                Assert.Contains(group, control.GetVisualAncestors());
                var point = control.TranslatePoint(default, group)!.Value;
                Assert.True(point.X >= 1 && point.X + control.Bounds.Width <= group.Bounds.Width - 1);
                Assert.True(point.Y >= 1 && point.Y + control.Bounds.Height <= group.Bounds.Height - 1);
            }
            var normalBorder = ((ISolidColorBrush)group.BorderBrush!).Color;
            box.Focus(); box.Text = "9285"; Pump(); Press(window, Key.Enter);
            Assert.True(group.IsKeyboardFocusWithin);
            Assert.NotEqual(normalBorder, ((ISolidColorBrush)group.BorderBrush!).Color);
            var inputBorder = box.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_BorderElement");
            Assert.Equal(default, inputBorder.BorderThickness);
            Assert.Equal(default, field.BorderThickness);
            Assert.Equal(4, model.Orders.Count);
            grid.SelectedItem = model.Orders[0]; Pump();
            box.Focus(); Press(window, Key.Tab);
            Assert.Same(exact, window.FocusManager!.GetFocusedElement());
            Press(window, Key.Space);
            Assert.True(exact.IsChecked); Assert.True(model.SearchExactMatch); Assert.Equal([1], Ids(model));
            Assert.True(glyph.IsEffectivelyVisible); Assert.NotNull(glyph.Model);
            Assert.Empty(grid.SelectedItems);
            if (scale == 1) Save(window, $"compound-search-exact-{dark}");
            var focusedBorder = ((ISolidColorBrush)group.BorderBrush!).Color;
            field.Focus(); Press(window, Key.F4); Assert.True(field.IsDropDownOpen);
            Assert.Equal(focusedBorder, ((ISolidColorBrush)group.BorderBrush!).Color);
            Press(window, Key.Escape); Assert.False(field.IsDropDownOpen);
            Assert.Equal("9285", model.SearchText); Assert.True(model.SearchExactMatch);
            Press(window, Key.F5); Assert.True(model.SearchExactMatch); Assert.Equal(2, reader.Calls);
            box.Focus(); box.Text = "009285"; Pump(); Assert.True(box.IsPending);
            Click(window, exact);
            Assert.False(model.SearchExactMatch); Assert.False(box.IsPending);
            Assert.Equal("009285", model.SearchText); Assert.Equal([3], Ids(model));
            box.Focus(); box.Text = "9285"; Pump(); Click(window, exact);
            Assert.True(model.SearchExactMatch); Assert.False(box.IsPending); Assert.Equal([1], Ids(model));
            exact.Focus(); Press(window, Key.Space);
            Assert.False(model.SearchExactMatch); Assert.Equal(4, model.Orders.Count);
            Click(window, exact); box.Focus(); Press(window, Key.Escape);
            Assert.True(model.SearchExactMatch); Assert.Equal("", box.Text); Assert.Equal(4, model.Orders.Count);
            field.SelectedIndex = 0; Pump(); Assert.False(exact.IsEffectivelyVisible); Assert.False(model.SearchExactMatch);
            field.SelectedIndex = (int)OrderSearchField.Specification; Pump(); Click(window, exact);
            box.Focus(); box.Text = "не применять"; Pump(); Assert.True(box.IsPending);
            model.ResetFiltersCommand.Execute(null); Pump();
            Assert.False(model.SearchExactMatch); Assert.False(exact.IsEffectivelyVisible); Assert.False(box.IsPending);
            await Task.Delay(350, TestContext.Current.CancellationToken); Pump();
            Assert.Equal("", model.SearchText); Assert.Equal(4, model.Orders.Count); Assert.Equal(2, reader.Calls);
        }
        finally { window.Close(); }
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NewSearchDebouncesAndFindsAcrossEmptyFiltersThenCanBeNarrowedAndCleared(bool dark)
    {
        var reader = new GlobalJournalSearchTests.Reader();
        var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 720 };
        try
        {
            window.Show(); Pump();
            var box = window.FindControl<JournalSearchBox>("SearchBox")!;
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            model.StatusFilter = OrderStatusFilter.Ready; model.FilterDate = new(2000, 1, 1); Pump();
            Assert.Empty(model.Orders);
            var applied = 0;
            model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.SearchText)) applied++; };
            box.Focus(); box.Text = "92"; Pump();
            Assert.True(box.IsPending); Assert.True(model.IsSearchPending); Assert.Equal("", model.SearchText);
            Assert.True(box.IsEffectivelyEnabled); Assert.False(grid.IsEffectivelyEnabled);
            Assert.False(model.CanUseJournal); Assert.False(model.CanAddResult);
            await Task.Delay(60, TestContext.Current.CancellationToken);
            box.Text = "9285"; Pump();
            await Until(() => !box.IsPending && model.SearchText == "9285");
            Assert.Equal(1, applied); Assert.Equal(new[] { 1, 2, 6 }, Ids(model));
            Assert.Null(model.FilterDate); Assert.False(model.HasAnyColumnFilter); Assert.True(grid.IsEffectivelyEnabled);
            Assert.Empty(grid.SelectedItems); Assert.Equal(1, reader.Calls);

            LinkedColumnFilterTests.Select(model.ManagerFilter, "Анна"); Pump(); Assert.Equal(new[] { 1, 6 }, Ids(model));
            Save(window, $"journal-search-{dark}");
            grid.SelectedItem = model.Orders[0]; Pump(); Assert.True(model.CanAddResult);
            model.StatusFilter = OrderStatusFilter.Ready; Pump(); Assert.Empty(model.Orders);
            box.Focus(); box.Text = "9286"; Pump(); Press(window, Key.Enter);
            Assert.Equal("9286", model.SearchText); Assert.Equal(new[] { 3, 4, 5 }, Ids(model));
            Assert.False(model.HasAnyColumnFilter); Assert.Empty(grid.SelectedItems);

            box.Text = "незавершённый запрос"; Pump(); Assert.True(model.IsSearchPending);
            var clear = window.FindControl<JournalButton>("ClearSearchButton")!;
            Assert.True(clear.IsEffectivelyVisible); Assert.True(clear.IsEffectivelyEnabled);
            var point = clear.TranslatePoint(new Point(clear.Bounds.Width / 2, clear.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
            Assert.Equal("", box.Text); Assert.Equal("", model.SearchText); Assert.Equal(6, model.Orders.Count);
            Assert.True(box.IsKeyboardFocusWithin);
            Assert.False(model.IsSearchPending); Assert.False(model.HasAnyColumnFilter);
            await Task.Delay(350, TestContext.Current.CancellationToken); Pump();
            Assert.Equal(6, model.Orders.Count); Assert.Equal(1, reader.Calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task FieldChangeRefreshResetAndCloseHandlePendingTextWithoutRevivingOldQueries(bool dark)
    {
        var reader = new GlobalJournalSearchTests.Reader();
        var model = new MainWindowViewModel(reader) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Pump();
            var box = window.FindControl<JournalSearchBox>("SearchBox")!;
            box.Focus(); box.Text = "Анна"; Pump(); Assert.True(model.IsSearchPending);
            model.SearchScopeIndex = (int)OrderSearchField.Manager; Pump();
            Assert.Equal("Анна", model.SearchText); Assert.False(model.IsSearchPending); Assert.Equal(new[] { 1, 3, 6 }, Ids(model));
            model.SearchScopeIndex = (int)OrderSearchField.DeliveryNumber; Pump(); Assert.Empty(model.Orders);
            box.Text = "9285"; Pump(); Press(window, Key.Enter);
            Assert.Equal(new[] { 1, 2, 6 }, Ids(model));
            LinkedColumnFilterTests.Select(model.ManagerFilter, "Анна"); model.StatusFilter = OrderStatusFilter.Work;
            reader.Items = reader.Items.Append(reader.Items[0] with { Id = 7, SpecificationNumber = "1018-900" }).ToArray();
            Press(window, Key.F5);
            Assert.Equal(new[] { 1, 7 }, Ids(model)); Assert.Equal("9285", model.SearchText);
            Assert.True(model.ManagerFilter.HasFilter); Assert.Equal(OrderStatusFilter.Work, model.StatusFilter); Assert.Equal(2, reader.Calls);

            reader.Items = reader.Items.Append(reader.Items[2] with { Id = 8, SpecificationNumber = "1018-901" }).ToArray();
            box.Text = "9286"; Pump(); Assert.True(model.IsSearchPending); Press(window, Key.F5);
            Assert.Equal("9286", model.SearchText); Assert.False(model.HasAnyColumnFilter); Assert.Equal(new[] { 3, 4, 5, 8 }, Ids(model));
            Assert.Equal(3, reader.Calls);
            box.Text = "не применять"; Pump(); model.ResetFiltersCommand.Execute(null); Pump();
            Assert.False(box.IsPending); Assert.Equal("", box.Text); Assert.Equal(8, model.Orders.Count);
            await Task.Delay(350, TestContext.Current.CancellationToken); Pump(); Assert.Equal("", model.SearchText);
            box.Focus(); box.Text = "сбросить по Esc"; Pump(); Press(window, Key.Escape);
            Assert.Equal("", box.Text); Assert.False(model.IsSearchPending); Assert.Equal(8, model.Orders.Count);
            box.Text = "после закрытия"; Pump(); Assert.True(box.IsPending);
            window.Close(); Pump();
            await Task.Delay(350, TestContext.Current.CancellationToken); Pump();
            Assert.Equal("", model.SearchText); Assert.False(model.IsSearchPending); Assert.Equal(3, reader.Calls);
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private static int[] Ids(MainWindowViewModel model) => model.Orders.Select(row => row.Id).Order().ToArray();
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Press(Window window, Key key)
    {
        var physical = key switch { Key.Enter => PhysicalKey.Enter, Key.F5 => PhysicalKey.F5, Key.Escape => PhysicalKey.Escape,
            Key.Space => PhysicalKey.Space, Key.Tab => PhysicalKey.Tab, Key.F4 => PhysicalKey.F4, _ => PhysicalKey.None };
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Pump();
    }
    private static async Task Until(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate() && timer.Elapsed < TimeSpan.FromSeconds(4))
        { await Task.Delay(10, TestContext.Current.CancellationToken); Pump(); }
        Assert.True(predicate());
    }
    private static void Save(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
