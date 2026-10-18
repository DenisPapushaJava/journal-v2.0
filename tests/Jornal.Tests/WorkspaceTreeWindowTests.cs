using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class WorkspaceTreeWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, 1, 1366, 768)] [InlineData(true, 1, 1366, 768)]
    [InlineData(false, 1.5, 1366, 768)] [InlineData(true, 1.5, 1366, 768)]
    [InlineData(false, 1, 1280, 680)] [InlineData(true, 1, 1280, 680)]
    [InlineData(false, 1.5, 1280, 680)] [InlineData(true, 1.5, 1280, 680)]
    public void PanelsCanBePlacedBelowResultAndSideBySideThenSavedAndRestored(bool dark, double scale, int width, int height)
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { DarkTheme = dark, Layout = ScreenLayout.CreateDefault() });
        var (window, model) = Open(preferences, scale: scale, width: width, height: height);
        try
        {
            model.ResultText = "Текст с ручной правкой";
            var newOrder = Find<JournalButton>(window, "NewOrderButton");
            Click(window, "PersonalizeButton");
            var result = Bounds(window, "ResultSlot");
            DragWithPreview(window, "OrdersMoveHandle", new Point(result.Center.X, result.Bottom - 20), "OrdersCommands");
            var orders = Bounds(window, "OrdersCommands");
            DragWithPreview(window, "ClipboardMoveHandle", new Point(orders.Right - 24, orders.Center.Y), "ClipboardCommands");
            AssertTwoBelowResult(window);
            Assert.Same(newOrder, Find<JournalButton>(window, "NewOrderButton"));
            Assert.Equal("Текст с ручной правкой", model.ResultText);
            AssertReadable(window); Screenshot(window, "tree-edit-" + dark + "-" + scale + "-" + width);
            Click(window, "SaveLayoutButton");
            Assert.False(model.IsCustomizing); AssertTwoBelowResult(window);
            Assert.NotNull(preferences.Value!.Layout.Workspace!.Root);
            var saved = JsonSerializer.Serialize(preferences.Value.Layout.Workspace.Root);
            Click(window, "PersonalizeButton"); Click(window, "ResetLayoutButton");
            Assert.True(Bounds(window, "OrdersCommands").Right < Bounds(window, "ResultSlot").Left);
            Click(window, "CancelLayoutButton");
            AssertTwoBelowResult(window);
            Assert.Equal(saved, JsonSerializer.Serialize(preferences.Value.Layout.Workspace.Root));
            Screenshot(window, "tree-normal-" + dark + "-" + scale + "-" + width);
        }
        finally { window.Close(); }
        (window, _) = Open(preferences, scale: scale, width: width, height: height);
        try { AssertTwoBelowResult(window); AssertReadable(window); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void TooTallDropIsRedAndDoesNotChangeLayoutOrLoseCapture(bool dark)
    {
        var (window, model) = Open(dark: dark, height: 680);
        try
        {
            model.ResultText = "Письмо"; Click(window, "PersonalizeButton");
            var orders = Bounds(window, "OrdersCommands");
            Drag(window, "ClipboardMoveHandle", new Point(orders.Center.X, orders.Bottom - 20));
            var before = Bounds(window, "ResultSlot");
            var board = Bounds(window, "DockBoard");
            var point = new Point(board.Center.X, board.Top + 2);
            StartDrag(window, "ResultMoveHandle", point);
            Assert.Contains("forbidden", Find<Border>(window, "DropGuide").Classes);
            Assert.Equal("Недостаточно места", Find<TextBlock>(window, "DropGuideText").Text);
            Assert.Contains("Недостаточно места", Find<TextBlock>(window, "DragPreviewText").Text);
            Screenshot(window, "tree-forbidden-" + dark);
            window.MouseUp(point, MouseButton.Left); Pump();
            Assert.Equal(before, Bounds(window, "ResultSlot"));
            Assert.False(Find<Border>(window, "DragPreview").IsVisible);
            Assert.Equal(1, Find<Grid>(window, "ResultSlot").Opacity);
            Assert.True(model.IsCustomizing); Assert.Equal("Письмо", model.ResultText);
            Click(window, "CancelLayoutButton"); Assert.False(model.IsCustomizing);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WidthAndHeightCanBeChangedWithoutCustomizationAndPersistAfterRestart()
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { Layout = ScreenLayout.CreateDefault() with { Workspace = new() { Height = 400,
            Root = WorkspaceNode.Split(WorkspaceAxis.Columns, [WorkspaceNode.Panel("result"),
                WorkspaceNode.Split(WorkspaceAxis.Rows, [WorkspaceNode.Panel("orders"), WorkspaceNode.Panel("clipboard")], 1, 1)], 2, 1) } } });
        var (window, model) = Open(preferences, width: 1600, height: 1000);
        double width, topHeight;
        try
        {
            Assert.False(model.IsCustomizing);
            var before = Bounds(window, "ResultSlot").Width;
            var point = Center(window, "ResultResizeHandle");
            Drag(window, "ResultResizeHandle", point + new Vector(0, 80));
            point = Center(window, "WorkspaceFirstSplitter");
            Drag(window, "WorkspaceFirstSplitter", point + new Vector(50, 0));
            width = Bounds(window, "ResultSlot").Width;
            Assert.True(width > before + 30);
            point = Center(window, "WorkspaceSecondSplitter");
            var beforeTop = Bounds(window, "OrdersCommands").Height;
            Drag(window, "WorkspaceSecondSplitter", point + new Vector(0, 20));
            topHeight = Bounds(window, "OrdersCommands").Height;
            Assert.True(topHeight > beforeTop + 10);
            Assert.False(model.IsCustomizing); AssertReadable(window);
        }
        finally { window.Close(); }
        Assert.NotNull(preferences.Value);
        (window, model) = Open(preferences, width: 1600, height: 1000);
        try
        {
            Assert.InRange(Math.Abs(width - Bounds(window, "ResultSlot").Width), 0, 1);
            Assert.InRange(Math.Abs(topHeight - Bounds(window, "OrdersCommands").Height), 0, 1);
            Assert.False(model.IsCustomizing);
            // Клавиатурная настройка границ также доступна без входа в конструктор.
            Find<GridSplitter>(window, "WorkspaceFirstSplitter").Focus();
            Press(window, Key.Left); Assert.True(Bounds(window, "ResultSlot").Width < width - 4);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SmallerWindowUsesTemporaryLayoutWithoutOverwritingPreferredTree()
    {
        var root = WorkspaceNode.Split(WorkspaceAxis.Rows, WorkspaceLayout.PanelIds.Select(WorkspaceNode.Panel).ToArray(), 2, 1, 1);
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { Layout = ScreenLayout.CreateDefault() with { Workspace = new() { Root = root, Height = 480 } } });
        var (window, model) = Open(preferences, width: 1920, height: 1080);
        try
        {
            var preferred = JsonSerializer.Serialize(preferences.Value!.Layout.Workspace!.Root);
            Assert.True(Bounds(window, "ClipboardCommands").Top > Bounds(window, "OrdersCommands").Bottom);
            window.Width = 1280; window.Height = 680; Pump();
            Assert.Equal(Bounds(window, "ClipboardCommands").Top, Bounds(window, "OrdersCommands").Top);
            AssertReadable(window);
            model.IsDarkTheme = true; Pump();
            Assert.Equal(preferred, JsonSerializer.Serialize(preferences.Value!.Layout.Workspace!.Root));
            window.Width = 1920; window.Height = 1080; Pump();
            Assert.True(Bounds(window, "ClipboardCommands").Top > Bounds(window, "OrdersCommands").Bottom);
            AssertReadable(window);
        }
        finally { window.Close(); }
        Assert.Equal(JsonSerializer.Serialize(root), JsonSerializer.Serialize(preferences.Value!.Layout.Workspace!.Root));
    }

    [AvaloniaFact]
    public void KeyboardPlacementAndCancelPreserveTextAndPreviousPreferences()
    {
        var preferences = new MemoryUiPreferencesStore();
        var (window, model) = Open(preferences);
        try
        {
            model.ResultText = "Сохранить";
            var original = Bounds(window, "ResultSlot");
            Click(window, "PersonalizeButton");
            Find<LayoutHandle>(window, "ResultMoveHandle").Focus();
            Press(window, Key.Left, RawInputModifiers.Shift);
            Assert.True(Bounds(window, "ResultSlot").Right < Bounds(window, "ClipboardCommands").Left);
            AssertReadable(window);
            Click(window, "CancelLayoutButton");
            Assert.Equal(original, Bounds(window, "ResultSlot"));
            Assert.Equal("Сохранить", model.ResultText); Assert.Null(preferences.Value);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OuterEdgePlacesResultAboveTheEntireRemainingGroup()
    {
        var (window, _) = Open(width: 1600, height: 1000);
        try
        {
            Click(window, "PersonalizeButton");
            var board = Bounds(window, "DockBoard");
            DragWithPreview(window, "ResultMoveHandle", new Point(board.Center.X, board.Top + 2), "ResultSlot");
            var result = Bounds(window, "ResultSlot");
            foreach (var name in new[] { "OrdersCommands", "ClipboardCommands" })
                Assert.True(Bounds(window, name).Top > result.Bottom);
            Assert.InRange(Math.Abs(result.Width - board.Width), 0, 1.1);
            AssertReadable(window);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void EscapeCancelsCurrentResizeAndReleasesPointerWithoutLeavingCustomization(bool editing)
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { Layout = ScreenLayout.CreateDefault() with { Workspace = new() } });
        var originalPreferences = preferences.Value;
        var (window, model) = Open(preferences, width: 1600, height: 1000);
        try
        {
            if (editing) Click(window, "PersonalizeButton");
            var original = Bounds(window, "ResultSlot");
            var target = Center(window, "WorkspaceFirstSplitter") + new Vector(48, 0);
            StartDrag(window, "WorkspaceFirstSplitter", target);
            Assert.True(Bounds(window, "ResultSlot").Width > original.Width + 30);
            Press(window, Key.Escape);
            window.MouseUp(target, MouseButton.Left); Pump();
            Assert.Equal(original, Bounds(window, "ResultSlot"));
            Assert.Equal(editing, model.IsCustomizing);
            Assert.Same(originalPreferences, preferences.Value);
            // Новый жест после отмены действует: прежний захват указателя не завис.
            Drag(window, "WorkspaceFirstSplitter", Center(window, "WorkspaceFirstSplitter") + new Vector(24, 0));
            Assert.True(Bounds(window, "ResultSlot").Width > original.Width + 10);
            if (editing) { Click(window, "CancelLayoutButton"); Assert.Equal(original.Size, Bounds(window, "ResultSlot").Size); }
        }
        finally { window.Close(); }
    }

    private static void AssertTwoBelowResult(MainWindow window)
    {
        var result = Bounds(window, "ResultSlot"); var orders = Bounds(window, "OrdersCommands");
        var clipboard = Bounds(window, "ClipboardCommands");
        Assert.InRange(Math.Abs(result.Left - orders.Left), 0, 1.1); Assert.InRange(Math.Abs(result.Right - clipboard.Right), 0, 1.1);
        Assert.True(orders.Top > result.Bottom); Assert.Equal(orders.Top, clipboard.Top);
        Assert.True(orders.Right < clipboard.Left);
    }

    private static void AssertReadable(MainWindow window)
    {
        var panels = new[] { "ResultSlot", "OrdersCommands", "ClipboardCommands" }.Select(id => Bounds(window, id)).ToArray();
        foreach (var area in panels)
        {
            Assert.True(area.Left >= 0 && area.Right <= window.Width && area.Top >= 0 && area.Bottom <= window.Height);
            foreach (var other in panels.Where(candidate => candidate != area)) Assert.False(area.Intersects(other));
        }
        foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
        {
            var content = Assert.IsAssignableFrom<Control>(button.Content);
            Assert.True(content.Bounds.Width + button.Padding.Left + button.Padding.Right <= button.Bounds.Width + 1, "Обрезана команда " + button.Label);
            if (button.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("command-panel")) is { } panel)
                Assert.True(Bounds(window, button).Bottom <= Bounds(window, panel).Bottom + 1, "Команда вне панели " + button.Label);
        }
        Assert.True(Find<DataGrid>(window, "OrdersGrid").Bounds.Height >= (((MainWindowViewModel)window.DataContext!).IsCustomizing ? 96 : 128),
            $"Таблица {Find<DataGrid>(window, "OrdersGrid").Bounds.Height}; блок {Find<Grid>(window, "DockArea").Bounds.Height}; область {Find<Grid>(window, "BodyGrid").Bounds.Height}");
    }

    private static void DragWithPreview(MainWindow window, string handle, Point target, string panel)
    {
        StartDrag(window, handle, target);
        Assert.DoesNotContain("forbidden", Find<Border>(window, "DropGuide").Classes);
        Assert.True(Find<Border>(window, "DropGuide").IsVisible);
        var expected = Bounds(window, "DropGuide");
        window.MouseUp(target, MouseButton.Left); Pump();
        var actual = Bounds(window, panel);
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, 1.1);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, 1.1);
        Assert.InRange(Math.Abs(actual.Width - expected.Width), 0, 1.1);
        Assert.InRange(Math.Abs(actual.Height - expected.Height), 0, 1.1);
    }
    private static (MainWindow, MainWindowViewModel) Open(MemoryUiPreferencesStore? preferences = null, int width = 1366, int height = 768, bool dark = false, double scale = 1)
    {
        var backend = new DemoOrderStore(); var model = new MainWindowViewModel(backend, backend) { IsDarkTheme = dark };
        var window = new MainWindow(preferences ?? new MemoryUiPreferencesStore()) { DataContext = model, Width = width, Height = height };
        window.SetRenderScaling(scale); window.Show(); Pump(); return (window, model);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    private static Rect Bounds(MainWindow window, string name) => Bounds(window, Find<Control>(window, name));
    private static Rect Bounds(MainWindow window, Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    private static Point Center(MainWindow window, string name) => Bounds(window, name).Center;
    private static void Click(MainWindow window, string name) { var point = Center(window, name); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(); }
    private static void StartDrag(MainWindow window, string name, Point point) { window.MouseDown(Center(window, name), MouseButton.Left); window.MouseMove(point, RawInputModifiers.LeftMouseButton); Pump(); }
    private static void Drag(MainWindow window, string name, Point point) { StartDrag(window, name, point); window.MouseUp(point, MouseButton.Left); Pump(); }
    private static void Press(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) { window.KeyPress(key, modifiers, PhysicalKey.None, null); window.KeyRelease(key, modifiers, PhysicalKey.None, null); Pump(); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Screenshot(MainWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
