using System.Text.Json;
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

public sealed class ResultButtonsCustomizationTests
{
    [Theory]
    [InlineData("null", 36)] [InlineData("true", 36)] [InlineData("[]", 36)]
    [InlineData("{\"Size\":\"44\",\"CopyColor\":17}", 36)]
    [InlineData("{\"Size\":999,\"ResultColor\":\"#abc123\",\"ClearColor\":\"broken\"}", 44)]
    [InlineData("{\"Size\":-999,\"CopyColor\":null}", 32)]
    public void InvalidButtonFieldsDoNotResetTheRestOfTheProfile(string appearance, int size)
    {
        var profile = JsonSerializer.Deserialize<UiPreferences>("{\"DarkTheme\":true,\"Layout\":{\"RowHeight\":40,\"ResultButtons\":" + appearance + "}}")!;
        var layout = profile.Layout.Normalize();
        Assert.True(profile.DarkTheme); Assert.Equal(40, layout.RowHeight);
        Assert.Equal(size, layout.ResultButtons.Size);
        Assert.Equal(ResultButtonAppearance.DefaultCopyColor, layout.ResultButtons.CopyColor);
        Assert.Equal(ResultButtonAppearance.DefaultResultColor, layout.ResultButtons.ClearColor);
        Assert.True(layout.ResultHeight >= layout.ResultMinimumHeight);
        var restored = JsonSerializer.Deserialize<UiPreferences>(JsonSerializer.Serialize(profile with { Layout = layout }))!;
        Assert.Equal(layout.ResultButtons, restored.Layout.ResultButtons);
    }

    [Fact]
    public void OldProfilesGetTheDefaultButtonsAndOpaqueColorsStayReadableInEveryState()
    {
        var old = JsonSerializer.Deserialize<UiPreferences>("{\"Layout\":{\"ResultHeight\":216}}")!;
        Assert.Equal(new ResultButtonAppearance(), old.Layout.Normalize().ResultButtons);
        Assert.Equal(216, old.Layout.Normalize().ResultHeight);
        for (var red = 0; red <= 255; red += 17)
        for (var green = 0; green <= 255; green += 17)
        for (var blue = 0; blue <= 255; blue += 17)
        {
            var palette = ResultButtonPalette.Create($"#{red:X2}{green:X2}{blue:X2}");
            Assert.True(ResultButtonPalette.Contrast(palette.Foreground, palette.Background) >= 4.5);
            Assert.True(ResultButtonPalette.Contrast(palette.Foreground, palette.Hover) >= 4.5);
            Assert.True(ResultButtonPalette.Contrast(palette.Foreground, palette.Pressed) >= 4.5);
            Assert.True(ResultButtonPalette.Contrast(palette.Border, Color.Parse("#F1F2F4")) >= 3);
            static int Difference(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            Assert.True(Difference(palette.Background, palette.Pressed) >= Difference(palette.Background, palette.Hover));
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 680, 1d)] [InlineData(true, 1280, 720, 1.5)]
    [InlineData(false, 1366, 768, 1.5)] [InlineData(true, 1366, 768, 1d)]
    public void EveryPixelStepKeepsTheGroupAndIconsCenteredAndThePopupOpen(bool dark, int width, int height, double scale)
    {
        var store = Store(new(), dark);
        var (window, _) = Open(store, width, height, scale);
        try
        {
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            var editor = Find<ResultButtonsEditor>(window, "ResultButtonsCustomization");
            Click(Find<JournalButton>(editor, "ResultButtonsSettingsButton"));
            var minus = Find<Button>(editor, "DecreaseResultButtonsButton");
            var plus = Find<Button>(editor, "IncreaseResultButtonsButton");
            for (var count = 0; count < 4; count++) Click(minus);
            Assert.False(minus.IsEnabled);
            var panel = Find<Border>(window, "ResultPanel");
            double? axis = null;
            for (var size = 32; size <= 44; size++)
            {
                Assert.Equal(size, editor.Value.Size);
                Assert.True(Find<JournalButton>(editor, "ResultButtonsSettingsButton").Flyout!.IsOpen);
                var group = Bounds(Find<StackPanel>(window, "ResultActions"), panel);
                Assert.InRange(Math.Abs(group.Center.Y - panel.Bounds.Height / 2), 0, 1);
                axis ??= group.Center.X;
                Assert.InRange(Math.Abs(group.Center.X - axis.Value), 0, 1);
                foreach (var name in new[] { "AddResultButton", "CopyResultButton", "ClearResultButton" })
                {
                    var button = Find<JournalButton>(window, name);
                    Assert.Equal(size, button.Width); Assert.Equal(size, button.Height);
                    var bounds = Bounds(button, panel);
                    Assert.InRange(Math.Abs(bounds.Center.X - axis.Value), 0, 1);
                    Assert.True(bounds.Top >= 0 && bounds.Bottom <= panel.Bounds.Height);
                    var icon = button.GetVisualDescendants().OfType<Avalonia.Svg.Svg>().Single();
                    var iconBounds = Bounds(icon, button);
                    Assert.InRange(Math.Abs(iconBounds.Center.X - button.Bounds.Width / 2), 0, 1);
                    Assert.InRange(Math.Abs(iconBounds.Center.Y - button.Bounds.Height / 2), 0, 1);
                }
                if (size < 44) Click(plus);
            }
            Assert.False(plus.IsEnabled);
            Assert.Equal(36, store.Value!.Layout.ResultButtons.Size);
            Screenshot(window, $"buttons-44-{dark}-{width}-{scale}");
            editor.ClosePopups(); Click(Find<JournalButton>(window, "CancelLayoutButton"));
            Assert.Equal(36, Find<JournalButton>(window, "CopyResultButton").Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void ColorsAndSizeAreAPreviewUntilSaveAndCancelAlsoWorksAfterChangingTheTheme(bool dark)
    {
        var original = new ResultButtonAppearance { Size = 39, ResultColor = "#CC0000", ClearColor = "#228B22" };
        var store = Store(original, dark);
        var (window, model) = Open(store);
        try
        {
            var actionButtons = new[] { "AddResultButton", "CopyResultButton", "ClearResultButton" }
                .Select(name => Find<JournalButton>(window, name)).ToArray();
            var colors = new[] { original.ResultColor, original.CopyColor, original.ClearColor };
            for (var index = 0; index < actionButtons.Length; index++)
            {
                Assert.False(actionButtons[index].IsEnabled);
                Assert.Equal(Color.Parse(colors[index]), Brush(actionButtons[index].Background));
                Assert.Equal(.45, actionButtons[index].Opacity);
            }
            Screenshot(window, $"buttons-disabled-{dark}");
            model.ResultText = "Ручной текст";
            Pump();
            Assert.True(actionButtons[1].IsEnabled); Assert.Equal(1, actionButtons[1].Opacity);
            Assert.True(actionButtons[2].IsEnabled); Assert.Equal(1, actionButtons[2].Opacity);
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            foreach (var button in actionButtons)
            {
                Assert.False(button.IsEnabled);
                Assert.Equal(1, button.Opacity);
            }
            var editor = Find<ResultButtonsEditor>(window, "ResultButtonsCustomization");
            Click(Find<JournalButton>(editor, "ResultButtonsSettingsButton"));
            Click(Find<Button>(editor, "IncreaseResultButtonsButton"));
            ChooseColor(editor, "CopyActionColorButton", "#00FF00");
            Assert.Equal(40, Find<JournalButton>(window, "CopyResultButton").Width);
            Assert.Equal(Color.Parse("#00FF00"), Brush(Find<JournalButton>(window, "CopyResultButton").Background));
            Assert.Equal(Color.Parse("#CC0000"), Brush(Find<JournalButton>(window, "AddResultButton").Background));
            Assert.Equal(original, store.Value!.Layout.ResultButtons);
            model.IsDarkTheme = !dark; Pump();
            Assert.Equal(original, store.Value!.Layout.ResultButtons);
            Assert.Equal(!dark, store.Value.DarkTheme);
            Screenshot(window, $"buttons-colors-{dark}");
            editor.ClosePopups(); Click(Find<JournalButton>(window, "CancelLayoutButton"));
            Assert.Equal(original, editor.Value);
            Assert.Equal(39, Find<JournalButton>(window, "CopyResultButton").Width);
            Assert.Equal("Ручной текст", model.ResultText);
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            Click(Find<JournalButton>(editor, "ResultButtonsSettingsButton"));
            Click(Find<Button>(editor, "IncreaseResultButtonsButton"));
            ChooseColor(editor, "CopyActionColorButton", "#00FF00");
            editor.ClosePopups(); Click(Find<JournalButton>(window, "SaveLayoutButton"));
            Assert.False(model.IsCustomizing);
            Assert.Equal(40, store.Value!.Layout.ResultButtons.Size);
            Assert.Equal("#00FF00", store.Value.Layout.ResultButtons.CopyColor);
            Assert.Equal(original.ResultColor, store.Value.Layout.ResultButtons.ResultColor);
        }
        finally { window.Close(); }
        (window, _) = Open(store);
        try { Assert.Equal(40, Find<JournalButton>(window, "CopyResultButton").Width); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void IndividualResetAndGlobalResetAreReversibleAndPopupsCloseWithTheWindow()
    {
        var original = new ResultButtonAppearance { Size = 44, ClearColor = "#000000" };
        var store = Store(original);
        var (window, model) = Open(store);
        try
        {
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            var editor = Find<ResultButtonsEditor>(window, "ResultButtonsCustomization");
            Click(Find<JournalButton>(editor, "ResultButtonsSettingsButton"));
            Click(Find<Button>(editor, "ResetResultButtonsButton"));
            Assert.Equal(new ResultButtonAppearance(), editor.Value);
            editor.ClosePopups(); Click(Find<JournalButton>(window, "CancelLayoutButton"));
            Assert.Equal(original, editor.Value);
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            Click(Find<JournalButton>(window, "ResetLayoutButton"));
            Assert.Equal(new ResultButtonAppearance(), editor.Value);
            Click(Find<JournalButton>(window, "CancelLayoutButton"));
            Assert.Equal(original, editor.Value);
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            Click(Find<JournalButton>(editor, "ResultButtonsSettingsButton"));
            var color = Find<Button>(editor, "ClearActionColorButton"); Click(color);
            var palette = (Flyout)FlyoutBase.GetAttachedFlyout(color)!;
            Assert.True(palette.IsOpen);
            window.Close(); Pump();
            Assert.False(palette.IsOpen); Assert.False(editor.HasOpenPopup);
            Assert.Equal(original, store.Value!.Layout.ResultButtons);
            Assert.False(model.IsCustomizing);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MaximumButtonsFitInAThreeRowWorkspaceAndTheLegacyResultPanel()
    {
        foreach (var legacy in new[] { false, true })
        {
            var root = WorkspaceNode.Split(WorkspaceAxis.Rows, WorkspaceLayout.PanelIds.Select(WorkspaceNode.Panel).ToArray(), 2, 1, 1);
            var store = Store(new() { Size = 44 });
            store.Save(store.Value! with { Layout = store.Value!.Layout with { Workspace = legacy ? null : new() { Root = root, Height = 480 } } });
            var (window, _) = Open(store, 1920, 1080);
            try
            {
                var panel = Find<Border>(window, "ResultPanel");
                var actions = Bounds(Find<StackPanel>(window, "ResultActions"), panel);
                Assert.True(actions.Top >= 0 && actions.Bottom <= panel.Bounds.Height);
                if (!legacy) Assert.True(Bounds(Find<Border>(window, "ClipboardCommands"), window).Top >
                    Bounds(Find<Border>(window, "OrdersCommands"), window).Bottom);
            }
            finally { window.Close(); }
        }
    }

    [Fact]
    public void TheRealProfileStorePreservesButtonAppearanceAndUnrelatedPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-button-profile-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ui-settings.json");
        try
        {
            var store = new JsonUiPreferencesStore(path);
            var appearance = new ResultButtonAppearance { Size = 43, ResultColor = "#CC0033", CopyColor = "#006699", ClearColor = "#112233" };
            store.Save(new() { DarkTheme = true, ShowTodayOnStartup = false,
                Layout = ScreenLayout.CreateDefault() with { ResultButtons = appearance, RowHeight = 40 } });
            var loaded = store.Load();
            Assert.Null(loaded.Error); Assert.NotNull(loaded.Preferences);
            Assert.Equal(appearance, loaded.Preferences.Layout.ResultButtons);
            Assert.True(loaded.Preferences.DarkTheme); Assert.False(loaded.Preferences.ShowTodayOnStartup);
            Assert.Equal(40, loaded.Preferences.Layout.RowHeight);
            Assert.Single(Directory.GetFiles(directory));
            Assert.DoesNotContain("ResultMinimumHeight", File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaFact]
    public void FailedSaveKeepsTheDraftAvailableAndCancelRestoresThePreviousButtons()
    {
        var store = new FailingStore();
        var (window, model) = Open(store);
        try
        {
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            var editor = Find<ResultButtonsEditor>(window, "ResultButtonsCustomization");
            Click(Find<JournalButton>(editor, "ResultButtonsSettingsButton"));
            Click(Find<Button>(editor, "IncreaseResultButtonsButton"));
            editor.ClosePopups(); Click(Find<JournalButton>(window, "SaveLayoutButton"));
            Assert.True(model.IsCustomizing); Assert.True(model.HasPreferencesError);
            Assert.Equal(37, editor.Value.Size);
            Assert.Equal(36, store.Value.Layout.ResultButtons.Size);
            Click(Find<JournalButton>(window, "CancelLayoutButton"));
            Assert.False(model.IsCustomizing); Assert.Equal(36, editor.Value.Size);
        }
        finally { window.Close(); }
    }

    private sealed class FailingStore : IUiPreferencesStore
    {
        public UiPreferences Value { get; } = new() { Layout = ScreenLayout.CreateDefault() };
        public PreferencesLoadResult Load() => new(Value);
        public void Save(UiPreferences preferences) => throw new IOException("synthetic profile failure");
    }

    [AvaloniaFact]
    public void EscapeClosesThePaletteThenTheEditorBeforeCancelingTheLayout()
    {
        var (window, model) = Open(Store(new()));
        try
        {
            Click(Find<JournalButton>(window, "PersonalizeButton"));
            var editor = Find<ResultButtonsEditor>(window, "ResultButtonsCustomization");
            var settings = Find<JournalButton>(editor, "ResultButtonsSettingsButton");
            Click(settings); Click(Find<Button>(editor, "IncreaseResultButtonsButton"));
            var color = Find<Button>(editor, "CopyActionColorButton"); Click(color);
            var palette = (Flyout)FlyoutBase.GetAttachedFlyout(color)!;
            var wheel = ((Control)palette.Content!).GetVisualDescendants().OfType<ColorWheel>().Single();
            PressEscape(wheel);
            Assert.False(palette.IsOpen); Assert.True(settings.Flyout!.IsOpen); Assert.True(model.IsCustomizing);
            PressEscape(Find<Button>(editor, "DecreaseResultButtonsButton"));
            Assert.False(settings.Flyout.IsOpen); Assert.True(model.IsCustomizing);
            PressEscape(settings);
            Assert.False(model.IsCustomizing); Assert.Equal(36, editor.Value.Size);
        }
        finally { window.Close(); }
    }

    private static void PressEscape(Control control)
    {
        control.Focus(); Pump();
        var root = TopLevel.GetTopLevel(control)!;
        root.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        root.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
    }

    private static MemoryUiPreferencesStore Store(ResultButtonAppearance appearance, bool dark = false)
    {
        var store = new MemoryUiPreferencesStore();
        store.Save(new() { DarkTheme = dark, Layout = ScreenLayout.CreateDefault() with { ResultButtons = appearance } });
        return store;
    }
    private static (MainWindow Window, MainWindowViewModel Model) Open(IUiPreferencesStore store, int width = 1366, int height = 768, double scale = 1)
    {
        var model = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow(store) { DataContext = model, Width = width, Height = height };
        window.SetRenderScaling(scale); window.Show(); Pump(); return (window, model);
    }
    private static void ChooseColor(ResultButtonsEditor editor, string name, string hex)
    {
        var button = Find<Button>(editor, name); Click(button);
        var palette = (Flyout)FlyoutBase.GetAttachedFlyout(button)!;
        var wheel = ((Control)palette.Content!).GetVisualDescendants().OfType<ColorWheel>().Single();
        wheel.HsvColor = new HsvColor(Color.Parse(hex)); Pump();
        Assert.True(palette.IsOpen); palette.Hide(); Pump();
    }
    private static T Find<T>(Control parent, string name) where T : Control => parent.FindControl<T>(name)!;
    private static Color Brush(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static Rect Bounds(Control control, Control relative) => new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(relative)!.Value);
    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
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
