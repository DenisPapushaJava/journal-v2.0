using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Jornal.Core.Results;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class CompactPanelsMigrationTests
{
    [Fact]
    public void PreviousProfileGetsPresetOnceWithoutResettingOtherPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "jr-preset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            var previous = new UiPreferences { DarkTheme = true, ShowTodayOnStartup = false,
                ResultFormatting = new() { FontSizePoints = 14, ColorHex = "#123456" },
                ResultPricing = new() { UseMinimumPrice = false, MinimumPrice = 900 },
                NewOrderColor = new() { Enabled = true, ColorArgb = -25089 },
                EntrySuggestions = EntrySuggestions.FromText("Личный менеджер", "Личный заказчик", "Сплав_X77"),
                Layout = new() { RowHeight = 24, ColumnSizingVersion = 1,
                    Columns = [new("customer", 2, false, false, 240)], Workspace = new() { Height = 400 } } };
            // Файл прежнего выпуска без отметки о выполненном переходе.
            File.WriteAllText(path, JsonSerializer.Serialize(previous));
            var loaded = new JsonUiPreferencesStore(path).Load();
            Assert.Null(loaded.Error); var applied = loaded.Preferences!;
            Assert.True(applied.Layout.CompactPanelsPresetApplied);
            Assert.Equal(new[] { "orders", "clipboard", "result" }, applied.Layout.Workspace!.Panels);
            Assert.Equal(138, applied.Layout.Workspace.Height);
            Assert.Equal(previous.Layout.Columns, applied.Layout.Columns); Assert.Equal(24, applied.Layout.RowHeight);
            Assert.Equal(previous.DarkTheme, applied.DarkTheme); Assert.False(applied.ShowTodayOnStartup);
            Assert.Equal(previous.ResultFormatting, applied.ResultFormatting);
            Assert.Equal(previous.ResultPricing, applied.ResultPricing); Assert.Equal(previous.NewOrderColor, applied.NewOrderColor);
            Assert.Equal(previous.EntrySuggestions.Managers, applied.EntrySuggestions.Managers);
            Assert.Equal(previous.EntrySuggestions.Customers, applied.EntrySuggestions.Customers);
            Assert.Equal(previous.EntrySuggestions.Materials, applied.EntrySuggestions.Materials);
            var firstFile = File.ReadAllText(path);
            new JsonUiPreferencesStore(path).Load(); Assert.Equal(firstFile, File.ReadAllText(path));
            // Следующие запуски/обновления сохраняют последующую личную компоновку и высоту.
            var personal = applied with { Layout = applied.Layout with { Workspace = new WorkspaceLayout { Height = 420 }.Normalize() } };
            new JsonUiPreferencesStore(path).Save(personal);
            var personalFile = File.ReadAllText(path);
            for (var launch = 0; launch < 3; launch++)
            {
                var next = new JsonUiPreferencesStore(path).Load();
                Assert.Null(next.Error); Assert.Equal(420, next.Preferences!.Layout.Workspace!.Height);
                Assert.Equal(new[] { "result", "orders", "clipboard" }, next.Preferences.Layout.Workspace.Panels);
                Assert.Equal(personalFile, File.ReadAllText(path));
            }
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void FailedMigrationKeepsOldLayoutAndDoesNotRetryInTheSameLoad(int failure)
    {
        var original = new UiPreferences { DarkTheme = true, Layout = new() { Workspace = new() { Height = 400 } } };
        var writes = 0;
        var result = CompactPanelsMigration.Apply(original, _ =>
        {
            writes++;
            throw failure switch { 0 => new IOException(), 1 => new UnauthorizedAccessException(), _ => new NotSupportedException() };
        });
        Assert.Same(original, result.Preferences); Assert.False(result.Preferences!.Layout.CompactPanelsPresetApplied);
        Assert.NotNull(result.Error); Assert.Equal(1, writes);
        var explicitChoice = original with { Layout = original.Layout with { CompactPanelsPresetApplied = true } };
        result = CompactPanelsMigration.Apply(explicitChoice, _ => throw new InvalidOperationException("Повторный сброс"));
        Assert.Same(explicitChoice, result.Preferences); Assert.Null(result.Error);
    }

    [AvaloniaTheory]
    [InlineData(false, 1280, 680, 1)] [InlineData(true, 1280, 680, 1.5)]
    [InlineData(false, 1920, 1080, 1)] [InlineData(true, 1920, 1080, 1.5)]
    public void NewAndResetPresetMatchesScreenshotAndResetCanBeCanceled(bool dark, int width, int height, double scale)
    {
        var backend = new DemoOrderStore(); var model = new MainWindowViewModel(backend, backend) { IsDarkTheme = dark };
        var preferences = new MemoryUiPreferencesStore();
        var window = new MainWindow(preferences) { DataContext = model, Width = width, Height = height };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); Pump();
            var orders = Bounds(window, "OrdersCommands"); var clipboard = Bounds(window, "ClipboardCommands"); var result = Bounds(window, "ResultSlot");
            Assert.True(orders.Right < clipboard.Left && clipboard.Right < result.Left);
            Assert.InRange(orders.Width, 303, 305); Assert.InRange(clipboard.Width, 303, 305);
            Assert.InRange(result.Height, 173, 175);
            Assert.InRange(result.Width, width == 1920 ? 1239 : 599, width == 1920 ? 1241 : 601);
            SaveScreenshot(window, "compact-preset-" + dark + "-" + width);
            // Личная высота отличается от макета; сброс до Готово остаётся отменяемым.
            var resize = Bounds(window, "ResultResizeHandle").Center;
            window.MouseDown(resize, MouseButton.Left);
            window.MouseMove(resize + new Vector(0, 70), RawInputModifiers.LeftMouseButton);
            window.MouseUp(resize + new Vector(0, 70), MouseButton.Left); Pump();
            var personalHeight = Bounds(window, "ResultSlot").Height;
            Assert.True(personalHeight > 200);
            Click(window, "PersonalizeButton"); Click(window, "ResetLayoutButton");
            Assert.InRange(Bounds(window, "ResultSlot").Height, 173, 175);
            Click(window, "CancelLayoutButton");
            Assert.Equal(personalHeight, Bounds(window, "ResultSlot").Height);
            Click(window, "PersonalizeButton"); Click(window, "ResetLayoutButton"); Click(window, "SaveLayoutButton");
            Assert.True(preferences.Value!.Layout.CompactPanelsPresetApplied);
            Assert.Equal(new[] { "orders", "clipboard", "result" }, preferences.Value.Layout.Workspace!.Panels);
            Assert.InRange(Bounds(window, "ResultSlot").Height, 173, 175);
        }
        finally { window.Close(); }
    }

    private static Rect Bounds(Window window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        return new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
    }
    private static void Click(Window window, string name)
    { var point = Bounds(window, name).Center; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump(); }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void SaveScreenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
