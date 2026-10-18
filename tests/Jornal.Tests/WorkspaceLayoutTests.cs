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
        Assert.Equal(new[] { "orders", "result", "clipboard" }, layout.Panels);
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
    [InlineData("{\"Root\":\"bad\"}")]
    [InlineData("{\"Root\":{\"PanelId\":\"result\"}}")]
    [InlineData("{\"Root\":{\"Axis\":999,\"Children\":[]}}")]
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
            Assert.Equal(WorkspaceLayout.CreateDefault().Panels, preferences.Layout.Workspace.Panels);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(1280, 680, false)] [InlineData(1280, 720, true)]
    [InlineData(1366, 768, false)] [InlineData(1366, 768, true)]
    [InlineData(1600, 1000, false)] [InlineData(1600, 1000, true)]
    public void ThreePanelsFitWindowAndKeepColorCommandsInOrders(int width, int height, bool dark)
    {
        var (window, model) = Open(width: width, height: height, dark: dark);
        try
        {
            var result = Bounds(window, "ResultSlot"); var orders = Bounds(window, "OrdersCommands");
            var clipboard = Bounds(window, "ClipboardCommands");
            Assert.True(orders.Right < clipboard.Left && clipboard.Right < result.Left);
            Assert.Null(window.FindControl<Border>("StatusCommands"));
            foreach (var name in new[] { "SetWorkButton", "SetReadyButton", "SetShippedButton" })
                Assert.Null(window.FindControl<JournalButton>(name));
            foreach (var name in new[] { "SetCustomColorButton", "ClearColorButton" })
                Assert.Contains(Find<Border>(window, "OrdersCommands"), Find<JournalButton>(window, name).GetVisualAncestors());
            Click(window, "PersonalizeButton");
            foreach (var name in new[] { "WorkspaceFirstSplitter", "WorkspaceSecondSplitter", "ResultMoveHandle" })
                Assert.True(Find<Control>(window, name).IsEffectivelyVisible);
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
            {
                var content = Assert.IsAssignableFrom<Control>(button.Content);
                Assert.True(content.Bounds.Width + button.Padding.Left + button.Padding.Right <= button.Bounds.Width + 1, button.Label);
                if (button.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("command-panel")) is { } panel)
                    Assert.True(Bounds(window, button).Bottom <= Bounds(window, panel).Bottom + 1, button.Label);
            }
            Assert.True(Find<DataGrid>(window, "OrdersGrid").Bounds.Height >= 96);
            Click(window, "CancelLayoutButton"); Assert.False(model.IsCustomizing);
        }
        finally { window.Close(); }
    }

    private static (MainWindow, MainWindowViewModel) Open(IUiPreferencesStore? preferences = null, int width = 1366, int height = 768, bool dark = false)
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark };
        var window = new MainWindow(preferences ?? new MemoryUiPreferencesStore()) { DataContext = model, Width = width, Height = height };
        window.Show(); Pump(); return (window, model);
    }
    private static T Find<T>(MainWindow window, string name) where T : Control => window.FindControl<T>(name)!;
    private static Rect Bounds(MainWindow window, string name) => Bounds(window, Find<Control>(window, name));
    private static Rect Bounds(MainWindow window, Control control) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    private static void Click(MainWindow window, string name)
    { var point = Bounds(window, name).Center; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
