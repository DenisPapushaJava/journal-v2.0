using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ScreenCustomizationTests
{
    [AvaloniaTheory]
    [InlineData(1280, 680, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, false)]
    [InlineData(1366, 768, true)]
    [InlineData(1600, 1000, false)]
    [InlineData(1600, 1000, true)]
    public void EditingShowsHandlesAndKeepsCommandsReadable(int width, int height, bool dark)
    {
        var (window, model) = Open(width: width, height: height, dark: dark);
        try
        {
            Click(window, "PersonalizeButton");
            Assert.True(model.IsCustomizing);
            Assert.False(model.CanCreateOrder);
            Assert.False(model.ReloadCommand.CanExecute(null));
            Assert.False(Find<CheckBox>(window, "SelectAllRowsBox").IsEnabled);
            Assert.False(Find<Grid>(window, "FiltersPanel").IsEffectivelyEnabled);
            foreach (var name in new[] { "OrdersMoveHandle", "ClipboardMoveHandle", "StatusMoveHandle", "ResultMoveHandle", "SaveLayoutButton", "CancelLayoutButton", "ResetLayoutButton" })
            {
                var control = Find<Control>(window, name);
                Assert.True(control.IsEffectivelyVisible);
                Assert.NotNull(ToolTip.GetTip(control));
                var bounds = Bounds(window, control);
                Assert.True(bounds.Left >= 0 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1, $"{name}: {bounds}");
            }
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
                Assert.True(((Control)button.Content!).Bounds.Width <= button.Bounds.Width + 1, $"Обрезана команда {button.Label}");
            Assert.True(Find<DataGrid>(window, "OrdersGrid").Bounds.Height >= 128);
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(Find<JournalButton>(window, "SaveLayoutButton").Background).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(Find<Border>(window, "ResultPanel").BorderBrush).Color);
            Screenshot(window, $"customization-{width}-{height}-{(dark ? "dark" : "light")}");
            Click(window, "CancelLayoutButton");
            Assert.False(model.IsCustomizing);
            Assert.True(model.CanCreateOrder);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PointerDragMovesGroupsAndResultAndCancelRestoresOriginal()
    {
        var (window, model) = Open();
        try
        {
            var table = Find<DataGrid>(window, "OrdersGrid");
            model.SearchText = "5083";
            table.SelectedItem = model.Orders[0];
            Find<TextBox>(window, "ResultTextBox").Text = "Текст для письма";
            Pump();
            var initialWidth = table.Columns[1].Width;
            Click(window, "PersonalizeButton");
            Drag(window, "OrdersMoveHandle", Center(window, Find<Border>(window, "StatusCommands")));
            Assert.Equal(4, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            Drag(window, "ResultMoveHandle", Center(window, Find<Grid>(window, "CommandGroups")));
            Assert.Equal(2, Grid.GetRow(Find<Grid>(window, "ResultSlot")));
            table.Columns[1].Width = new DataGridLength(190);
            table.Columns[2].DisplayIndex = 1;
            table.Columns[3].IsVisible = false;
            model.IsDarkTheme = true;
            Click(window, "CancelLayoutButton");
            Assert.Equal(0, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            Assert.Equal(0, Grid.GetRow(Find<Grid>(window, "ResultSlot")));
            Assert.Equal(initialWidth, table.Columns[1].Width);
            Assert.Equal(2, table.Columns[2].DisplayIndex);
            Assert.True(table.Columns[3].IsVisible);
            Assert.Equal("5083", model.SearchText);
            Assert.Single(table.SelectedItems);
            Assert.Equal("Текст для письма", Find<TextBox>(window, "ResultTextBox").Text);
            Assert.True(model.IsDarkTheme);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SavedLayoutAndThemeSurviveNewWindowAndResetIsReversible()
    {
        var store = new MemoryUiPreferencesStore();
        var (window, model) = Open(store);
        try
        {
            Click(window, "PersonalizeButton");
            Arrow(window, "OrdersMoveHandle", Key.Right);
            Arrow(window, "ResultMoveHandle", Key.Down);
            Arrow(window, "ResultResizeHandle", Key.Down);
            Find<DataGrid>(window, "OrdersGrid").Columns[3].IsVisible = false;
            Find<DataGrid>(window, "OrdersGrid").Columns[2].DisplayIndex = 1;
            model.IsDarkTheme = true;
            // Смена темы внутри редактирования не записывает черновик компоновки.
            Assert.True(store.Value!.DarkTheme);
            Assert.Equal("orders", store.Value.Layout.PanelOrder[0]);
            Assert.True(store.Value.Layout.ResultAboveCommands);
            Click(window, "SaveLayoutButton");
            Assert.False(model.IsCustomizing);
            Assert.Equal("clipboard", store.Value.Layout.PanelOrder[0]);
        }
        finally { window.Close(); }
        (window, model) = Open(store);
        try
        {
            var table = Find<DataGrid>(window, "OrdersGrid");
            Assert.True(model.IsDarkTheme);
            Assert.Equal(2, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            Assert.Equal(2, Grid.GetRow(Find<Grid>(window, "ResultSlot")));
            Assert.Equal(160, Find<Grid>(window, "ResultSlot").Bounds.Height);
            Assert.False(table.Columns[3].IsVisible);
            Assert.Equal(1, table.Columns[2].DisplayIndex);
            model.SearchText = "5083";
            table.SelectedItem = model.Orders[0];
            Find<TextBox>(window, "ResultTextBox").Text = "Сохранить текст";
            Pump();
            Click(window, "PersonalizeButton");
            Click(window, "ResetLayoutButton");
            Assert.Equal(0, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            Assert.All(table.Columns, column => Assert.True(column.IsVisible));
            Assert.Equal("5083", model.SearchText);
            Assert.Single(table.SelectedItems);
            Assert.Equal("Сохранить текст", Find<TextBox>(window, "ResultTextBox").Text);
            Assert.True(model.IsDarkTheme);
            Click(window, "CancelLayoutButton");
            Assert.Equal(2, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            Assert.False(table.Columns[3].IsVisible);
            Click(window, "PersonalizeButton");
            Click(window, "ResetLayoutButton");
            Click(window, "SaveLayoutButton");
            Assert.True(store.Value!.Layout.ResultAboveCommands);
            Assert.Equal(152, store.Value.Layout.ResultHeight);
            Assert.True(model.IsDarkTheme);
            Assert.All(store.Value.Layout.Columns, column => Assert.True(column.Visible));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResultAndPanelResizeRespectMinimumsAndAreSaved()
    {
        var store = new MemoryUiPreferencesStore();
        var (window, _) = Open(store, width: 1600, height: 1000);
        try
        {
            Click(window, "PersonalizeButton");
            var result = Find<Grid>(window, "ResultSlot");
            var sizeHandle = Find<LayoutHandle>(window, "ResultResizeHandle");
            var point = Center(window, sizeHandle);
            Drag(window, "ResultResizeHandle", new Point(point.X, point.Y + 70));
            Assert.Equal(222, result.Bounds.Height);
            var splitter = Find<GridSplitter>(window, "FirstPanelSplitter");
            var before = Find<Border>(window, "OrdersCommands").Bounds.Width;
            splitter.Focus();
            PressKey(window, Key.Right);
            Assert.True(Find<Border>(window, "OrdersCommands").Bounds.Width > before);
            for (var i = 0; i < 80; i++) PressKey(window, Key.Left);
            Assert.True(Find<Border>(window, "OrdersCommands").Bounds.Width >= 312);
            Click(window, "SaveLayoutButton");
            Assert.Equal(222, store.Value!.Layout.ResultHeight);
            window.Width = 1280;
            window.Height = 680;
            Pump();
            Assert.True(Find<DataGrid>(window, "OrdersGrid").Bounds.Height >= 128);
            Assert.True(Find<Border>(window, "ClipboardCommands").Bounds.Width >= 430);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ClosingAnUncommittedEditKeepsOriginalButNormalColumnChangesPersist()
    {
        var store = new MemoryUiPreferencesStore();
        var (window, model) = Open(store);
        var table = Find<DataGrid>(window, "OrdersGrid");
        table.Columns[1].Width = new DataGridLength(180);
        table.Columns[2].DisplayIndex = 1;
        table.Columns[3].IsVisible = false;
        model.IsDarkTheme = true;
        Click(window, "PersonalizeButton");
        Arrow(window, "OrdersMoveHandle", Key.Right);
        table.Columns[1].Width = new DataGridLength(240);
        window.Close();
        Assert.Equal("orders", store.Value!.Layout.PanelOrder[0]);
        Assert.Equal(180, store.Value.Layout.Columns.Single(column => column.Id == "date").Width);
        Assert.Equal(1, store.Value.Layout.Columns.Single(column => column.Id == "specification").Position);
        Assert.False(store.Value.Layout.Columns.Single(column => column.Id == "manager").Visible);
        Assert.True(store.Value.DarkTheme);
    }

    [AvaloniaFact]
    public void SaveFailureStaysInEditModeAndCanBeRetried()
    {
        var store = new FailingStore();
        var (window, model) = Open(store);
        try
        {
            Assert.True(model.HasPreferencesError);
            Click(window, "PersonalizeButton");
            Arrow(window, "OrdersMoveHandle", Key.Right);
            Click(window, "SaveLayoutButton");
            Assert.True(model.IsCustomizing);
            Assert.True(model.HasPreferencesError);
            Assert.Null(store.Value);
            store.Fail = false;
            Click(window, "SaveLayoutButton");
            Assert.False(model.IsCustomizing);
            Assert.False(model.HasPreferencesError);
            Assert.Equal("clipboard", store.Value!.Layout.PanelOrder[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EscapeCancelsDragFirstAndThenEditWithoutClearingSearch()
    {
        var (window, model) = Open();
        try
        {
            model.SearchText = "5083";
            Click(window, "PersonalizeButton");
            var start = Center(window, Find<LayoutHandle>(window, "OrdersMoveHandle"));
            var target = Center(window, Find<Border>(window, "StatusCommands"));
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(target, RawInputModifiers.LeftMouseButton);
            Pump();
            Assert.True(Find<Border>(window, "DragPreview").IsVisible);
            PressKey(window, Key.Escape);
            window.MouseUp(target, MouseButton.Left);
            Pump();
            Assert.True(model.IsCustomizing);
            Assert.False(Find<Border>(window, "DragPreview").IsVisible);
            Assert.Equal(0, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            PressKey(window, Key.Escape);
            Assert.False(model.IsCustomizing);
            Assert.Equal("5083", model.SearchText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UntouchedWindowDoesNotCreateProfileAndKeyboardCanSave()
    {
        var store = new MemoryUiPreferencesStore();
        var (window, _) = Open(store);
        window.Close();
        Assert.Null(store.Value);
        var reopened = Open(store);
        window = reopened.Item1;
        var model = reopened.Item2;
        try
        {
            Click(window, "PersonalizeButton");
            Arrow(window, "OrdersMoveHandle", Key.Right);
            window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            Pump();
            Assert.False(model.IsCustomizing);
            Assert.Equal("clipboard", store.Value!.Layout.PanelOrder[0]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CorruptProfileCanBeViewedWithoutWritingAReplacement()
    {
        var store = new FailingStore { Fail = false };
        var (window, model) = Open(store);
        Assert.True(model.HasPreferencesError);
        Assert.Equal(3, model.Orders.Count);
        window.Close();
        Assert.Null(store.Value);
    }

    private static (MainWindow, MainWindowViewModel) Open(IUiPreferencesStore? preferences = null, int width = 1366, int height = 768, bool dark = false)
    {
        var orders = new DemoOrderStore();
        var model = new MainWindowViewModel(orders, orders) { IsDarkTheme = dark };
        var window = new MainWindow(preferences ?? new MemoryUiPreferencesStore()) { DataContext = model, Width = width, Height = height };
        window.Show();
        Pump();
        return (window, model);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    private static Rect Bounds(MainWindow window, Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    private static Point Center(MainWindow window, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Click(MainWindow window, string name)
    {
        var point = Center(window, Find<Control>(window, name));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();
    }
    private static void Drag(MainWindow window, string name, Point target)
    {
        var start = Center(window, Find<Control>(window, name));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        Pump();
        if (name == "OrdersMoveHandle")
        {
            Assert.Equal(new Thickness(2), Find<Border>(window, "StatusCommands").BorderThickness);
            Screenshot(window, "customization-drop-target");
        }
        window.MouseUp(target, MouseButton.Left);
        Pump();
    }
    private static void Arrow(MainWindow window, string handle, Key key) { Find<Control>(window, handle).Focus(); PressKey(window, key); }
    private static void PressKey(MainWindow window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Screenshot(MainWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
    private sealed class FailingStore : IUiPreferencesStore
    {
        public bool Fail { get; set; } = true;
        public UiPreferences? Value { get; private set; }
        public PreferencesLoadResult Load() => new(null, "Не удалось прочитать настройки экрана.");
        public void Save(UiPreferences preferences)
        {
            if (Fail) throw new IOException("Проверка отказа записи профиля");
            Value = preferences;
        }
    }
}
