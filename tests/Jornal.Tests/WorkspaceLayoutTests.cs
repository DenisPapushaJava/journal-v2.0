using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void WorkspaceNormalizesBadZonesAndDimensionsWithoutChangingLegacyDefaults()
    {
        Assert.Null(new ScreenLayout().Normalize().Workspace);
        var layout = new WorkspaceLayout { Panels = ["orders", "unknown", "orders", "result"],
            ColumnWidths = [double.NaN, -1, 3], TopFraction = double.PositiveInfinity, Height = double.NaN }.Normalize();
        Assert.Equal(new[] { "orders", "result", "status", "clipboard" }, layout.Panels);
        Assert.Equal(new double[] { 2, 1, 3 }, layout.ColumnWidths);
        Assert.Equal(.43, layout.TopFraction);
        Assert.Equal(272, layout.Height);
    }

    [AvaloniaFact]
    public void ViewingNewDefaultDoesNotCreateOrReplacePreferences()
    {
        var preferences = new MemoryUiPreferencesStore();
        var (window, _) = Open(preferences);
        Assert.True(Find<Grid>(window, "DockArea").IsVisible);
        window.Close();
        Assert.Null(preferences.Value);
    }

    [Theory]
    [InlineData("\"invalid\"")]
    [InlineData("{\"Panels\":42}")]
    [InlineData("{\"Height\":\"bad\"}")]
    public void CorruptWorkspacePreservesThemeAndOtherPreferences(string json)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24,\"Workspace\":" + json + "}}");
            var preferences = new JsonUiPreferencesStore(path).Load().Preferences!;
            Assert.True(preferences.DarkTheme);
            Assert.Equal(24, preferences.Layout.RowHeight);
            Assert.NotNull(preferences.Layout.Workspace);
            Assert.Equal(new WorkspaceLayout().Panels, preferences.Layout.Workspace.Panels);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(1280, 680, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, false)]
    [InlineData(1366, 768, true)]
    [InlineData(1600, 1000, false)]
    [InlineData(1600, 1000, true)]
    public void FourZonesFitWindowAndCommandsStayVisible(int width, int height, bool dark)
    {
        var (window, _) = Open(width: width, height: height, dark: dark);
        try
        {
            var result = Bounds(window, "ResultSlot");
            var orders = Bounds(window, "OrdersCommands");
            var status = Bounds(window, "StatusCommands");
            var clipboard = Bounds(window, "ClipboardCommands");
            Assert.InRange(result.Width / (width - 72), .48, .51);
            Assert.True(result.Right < orders.Left && orders.Right < clipboard.Left);
            Assert.Equal(orders.Left, status.Left);
            Assert.True(orders.Bottom < status.Top);
            Assert.Equal(result.Top, clipboard.Top);
            Assert.Equal(result.Bottom, clipboard.Bottom);
            AssertReadable(window);
            Screenshot(window, $"workspace-{width}-{height}-{dark}");
            Click(window, "PersonalizeButton");
            AssertReadable(window);
            foreach (var name in new[] { "WorkspaceFirstSplitter", "WorkspaceSecondSplitter", "WorkspaceRowsSplitter", "ResultMoveHandle" })
                Assert.True(Find<Control>(window, name).IsEffectivelyVisible);
            Screenshot(window, $"workspace-edit-{width}-{height}-{dark}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragShowsActualPanelAndDestinationEscapeAndOutsideDropKeepLayout(bool dark)
    {
        var (window, model) = Open(dark: dark);
        try
        {
            model.ResultText = "Текст для письма";
            Click(window, "PersonalizeButton");
            var original = Bounds(window, "ResultSlot");
            var target = Center(window, "ClipboardCommands");
            StartDrag(window, "ResultMoveHandle", target);
            Assert.NotNull(Find<Image>(window, "DragPreviewImage").Source);
            Assert.Equal(.35, Find<Grid>(window, "ResultSlot").Opacity);
            Assert.True(Find<Border>(window, "DropGuide").IsVisible);
            Assert.True(Bounds(window, "DropGuide").Width >= 480);
            Screenshot(window, $"workspace-drag-{dark}");
            PressKey(window, Key.Escape);
            window.MouseUp(target, MouseButton.Left); Pump();
            Assert.True(model.IsCustomizing);
            Assert.False(Find<Border>(window, "DragPreview").IsVisible);
            Assert.Null(Find<Image>(window, "DragPreviewImage").Source);
            Assert.Equal(1, Find<Grid>(window, "ResultSlot").Opacity);
            Assert.Equal(original, Bounds(window, "ResultSlot"));
            target = new Point(100, 600);
            StartDrag(window, "ResultMoveHandle", target);
            Assert.False(Find<Border>(window, "DropGuide").IsVisible);
            window.MouseUp(target, MouseButton.Left); Pump();
            Assert.Equal(original, Bounds(window, "ResultSlot"));
            Assert.Equal("Текст для письма", model.ResultText);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AllPanelsSwapAndResizingSavesAndCancelRestoresSnapshot()
    {
        var preferences = new MemoryUiPreferencesStore();
        var (window, _) = Open(preferences, width: 1600, height: 1000);
        try
        {
            Click(window, "PersonalizeButton");
            var target = Center(window, "ClipboardCommands");
            StartDrag(window, "ResultMoveHandle", target);
            var projected = Bounds(window, "DropGuide");
            window.MouseUp(target, MouseButton.Left); Pump();
            var actual = Bounds(window, "ResultSlot");
            Assert.InRange(Math.Abs(actual.X - projected.X), 0, 1);
            Assert.InRange(Math.Abs(actual.Width - projected.Width), 0, 1);
            Assert.InRange(Math.Abs(actual.Height - projected.Height), 0, 1);
            Assert.Equal(4, Grid.GetColumn(Find<Grid>(window, "ResultSlot")));
            Drag(window, "OrdersMoveHandle", Center(window, "ClipboardCommands"));
            Assert.Equal(0, Grid.GetColumn(Find<Border>(window, "OrdersCommands")));
            Arrow(window, "StatusMoveHandle", Key.Up);
            Assert.Equal(0, Grid.GetRow(Find<Border>(window, "StatusCommands")));
            Arrow(window, "WorkspaceFirstSplitter", Key.Right);
            Arrow(window, "WorkspaceRowsSplitter", Key.Down);
            var point = Center(window, "ResultResizeHandle");
            Drag(window, "ResultResizeHandle", point + new Vector(0, 40));
            AssertReadable(window);
            Click(window, "SaveLayoutButton");
            Assert.Equal(new[] { "orders", "status", "clipboard", "result" }, preferences.Value!.Layout.Workspace!.Panels);
            var saved = preferences.Value.Layout.Workspace;
            Assert.True(saved.Height > 272);
            Click(window, "PersonalizeButton");
            Click(window, "ResetLayoutButton");
            Assert.Equal(0, Grid.GetColumn(Find<Grid>(window, "ResultSlot")));
            Click(window, "CancelLayoutButton");
            Assert.Equal(4, Grid.GetColumn(Find<Grid>(window, "ResultSlot")));
            Assert.Equal(saved.Panels, preferences.Value.Layout.Workspace!.Panels);
        }
        finally { window.Close(); }
        (window, _) = Open(preferences);
        try
        {
            Assert.Equal(4, Grid.GetColumn(Find<Grid>(window, "ResultSlot")));
            AssertReadable(window);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResultInShortZoneKeepsMinimumAndAllCommandsFitAfterExtremeResize()
    {
        var (window, _) = Open(width: 1280, height: 720);
        try
        {
            Click(window, "PersonalizeButton");
            Drag(window, "ResultMoveHandle", Center(window, "StatusCommands"));
            Assert.Equal(2, Grid.GetColumn(Find<Grid>(window, "ResultSlot")));
            Assert.True(Find<Grid>(window, "ResultSlot").Bounds.Width >= 480);
            for (var i = 0; i < 80; i++) Arrow(window, "WorkspaceFirstSplitter", Key.Right);
            for (var i = 0; i < 80; i++) Arrow(window, "WorkspaceRowsSplitter", Key.Down);
            Assert.True(Find<Grid>(window, "ResultSlot").Bounds.Height >= 106);
            AssertReadable(window);
            Screenshot(window, "workspace-swapped-small");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("ClipboardMoveHandle", "OrdersCommands", "ClipboardCommands", 720)]
    [InlineData("ClipboardMoveHandle", "OrdersCommands", "ClipboardCommands", 680)]
    [InlineData("StatusMoveHandle", "ClipboardCommands", "StatusCommands", 720)]
    [InlineData("StatusMoveHandle", "ClipboardCommands", "StatusCommands", 680)]
    public void ProjectedDestinationMatchesFutureTallAndStackedZones(string handle, string targetName, string sourceName, int height)
    {
        var (window, _) = Open(width: 1280, height: height);
        try
        {
            Click(window, "PersonalizeButton");
            var target = Center(window, targetName);
            StartDrag(window, handle, target);
            var projected = Bounds(window, "DropGuide");
            window.MouseUp(target, MouseButton.Left); Pump();
            var actual = Bounds(window, sourceName);
            Assert.InRange(Math.Abs(actual.X - projected.X), 0, 1);
            Assert.InRange(Math.Abs(actual.Y - projected.Y), 0, 1);
            Assert.InRange(Math.Abs(actual.Width - projected.Width), 0, 1);
            Assert.InRange(Math.Abs(actual.Height - projected.Height), 0, 1);
            AssertReadable(window);
            Click(window, "SaveLayoutButton");
            AssertReadable(window);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SplittersAndHeightAreRestoredAfterReopenAndCaptureCancelKeepsPreferredHeight()
    {
        var preferences = new MemoryUiPreferencesStore();
        var (window, _) = Open(preferences, width: 1600, height: 1000);
        double leftWidth, topHeight;
        try
        {
            Click(window, "PersonalizeButton");
            var resize = Center(window, "ResultResizeHandle");
            Drag(window, "ResultResizeHandle", resize + new Vector(0, 80));
            Arrow(window, "WorkspaceFirstSplitter", Key.Right);
            Arrow(window, "WorkspaceRowsSplitter", Key.Down);
            leftWidth = Bounds(window, "ResultSlot").Width;
            topHeight = Bounds(window, "OrdersCommands").Height;
            Click(window, "SaveLayoutButton");
            var requested = preferences.Value!.Layout.Workspace!.Height;
            window.Height = 680; Pump();
            Click(window, "PersonalizeButton");
            resize = Center(window, "ResultResizeHandle");
            StartDrag(window, "ResultResizeHandle", resize + new Vector(0, 8));
            PressKey(window, Key.Escape);
            window.MouseUp(resize, MouseButton.Left); Pump();
            Click(window, "SaveLayoutButton");
            Assert.Equal(requested, preferences.Value.Layout.Workspace!.Height);
        }
        finally { window.Close(); }
        (window, _) = Open(preferences, width: 1600, height: 1000);
        try
        {
            Assert.InRange(Math.Abs(leftWidth - Bounds(window, "ResultSlot").Width), 0, 1);
            // В уменьшенном окне сохраняется относительная пропорция, а не прежняя абсолютная высота.
            Assert.True(Bounds(window, "OrdersCommands").Height >= 106);
            Assert.True(topHeight >= 106);
            AssertReadable(window);
        }
        finally { window.Close(); }
    }

    private static void AssertReadable(MainWindow window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
        {
            var content = Assert.IsAssignableFrom<Control>(button.Content);
            Assert.True(content.Bounds.Width + button.Padding.Left + button.Padding.Right <= button.Bounds.Width + 1, $"Обрезана команда {button.Label}: {content.Bounds.Width}/{button.Bounds.Width}");
            Assert.True(content.Bounds.Height <= button.Bounds.Height + 1, $"Обрезана высота команды {button.Label}");
            if (button.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("command-panel")) is { } border)
                Assert.True(Bounds(window, button).Bottom <= Bounds(window, border).Bottom, $"Команда вне панели: {button.Label}");
        }
        // В режиме настройки таблица неактивна: при двух высоких блоках в центре
        // и окне 680 px оставляем видимыми её заголовок/строки и все команды.
        var minimum = ((MainWindowViewModel)window.DataContext!).IsCustomizing ? 96 : 128;
        Assert.True(Find<DataGrid>(window, "OrdersGrid").Bounds.Height >= minimum);
    }

    private static (MainWindow, MainWindowViewModel) Open(IUiPreferencesStore? preferences = null, int width = 1366, int height = 768, bool dark = false)
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark };
        var window = new MainWindow(preferences ?? new MemoryUiPreferencesStore()) { DataContext = model, Width = width, Height = height };
        window.Show(); Pump(); return (window, model);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    private static Rect Bounds(MainWindow window, string name) => Bounds(window, Find<Control>(window, name));
    private static Rect Bounds(MainWindow window, Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    private static Point Center(MainWindow window, string name) => Bounds(window, name).Center;
    private static void Click(MainWindow window, string name)
    { var point = Center(window, name); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(); }
    private static void StartDrag(MainWindow window, string name, Point target)
    { window.MouseDown(Center(window, name), MouseButton.Left); window.MouseMove(target, RawInputModifiers.LeftMouseButton); Pump(); }
    private static void Drag(MainWindow window, string name, Point target)
    { StartDrag(window, name, target); window.MouseUp(target, MouseButton.Left); Pump(); }
    private static void Arrow(MainWindow window, string name, Key key) { Find<Control>(window, name).Focus(); PressKey(window, key); }
    private static void PressKey(MainWindow window, Key key)
    { window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); Pump(); }
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
}
