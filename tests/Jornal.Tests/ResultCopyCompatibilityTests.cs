using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultCopyCompatibilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"ResultCopyFormat\":\"Table\"")]
    [InlineData(",\"ResultCopyFormat\":\"EditableText\"")]
    [InlineData(",\"ResultCopyFormat\":null")]
    [InlineData(",\"ResultCopyFormat\":true")]
    [InlineData(",\"ResultCopyFormat\":999")]
    [InlineData(",\"ResultCopyFormat\":{\"bad\":[]}")]
    public void RetiredCopyFormatDoesNotResetOtherSettingsOrRestoreAnUneditableTable(string fragment)
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-copy-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "ui-settings.json");
            File.WriteAllText(path, "{\"Version\":1,\"DarkTheme\":true,\"Layout\":{\"RowHeight\":24}," +
                "\"ResultFormatting\":{\"FontSizePoints\":12,\"ColorHex\":\"#2F5496\"}" + fragment + "}");
            var store = new JsonUiPreferencesStore(path);
            var loaded = store.Load();
            Assert.Null(loaded.Error); Assert.NotNull(loaded.Preferences);
            Assert.True(loaded.Preferences.DarkTheme); Assert.Equal(24, loaded.Preferences.Layout.RowHeight);
            Assert.Equal(12m, loaded.Preferences.ResultFormatting.FontSizePoints);
            Assert.Equal("#2F5496", loaded.Preferences.ResultFormatting.ColorHex);
            store.Save(loaded.Preferences);
            Assert.DoesNotContain("ResultCopyFormat", File.ReadAllText(path));
            Assert.Equal(loaded.Preferences.ResultFormatting, store.Load().Preferences!.ResultFormatting);
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopyAlwaysUsesEditableTextAndCurrentEditsWithoutAFormatSelector(bool dark)
    {
        var preferences = new MemoryUiPreferencesStore();
        preferences.Save(new() { DarkTheme = dark, Layout = ScreenLayout.CreateDefault() });
        var model = new MainWindowViewModel(new DemoOrderStore());
        var owner = new MainWindow(preferences) { DataContext = model };
        const string draft = "Ручная правка <угол> & ₽ 😀\nЛист 2x1250x2500,\t   Материал: Ст3,\t   2 шт.\n";
        try
        {
            owner.Show(); Pump();
            model.ResultText = draft;
            model.ResultFontSizePoints = 12; model.ResultTextColor = "#2F5496";
            Click(owner, "SettingsButton");
            var settings = Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            settings.Width = 800; settings.Height = 580; Pump();
            Assert.Null(settings.FindDescendant<ComboBox>("ResultCopyFormatSelector"));
            settings.FindDescendant<CheckBox>("UseResultMinimumPriceCheckBox")!.BringIntoView(); Pump();
            Screenshot(settings, $"result-copy-settings-{dark}");
            Click(settings, "CloseSettingsButton");
            var clipboard = new ResultClipboard(() => owner.Clipboard);
            await model.CopyResultAsync(clipboard);
            using (var copied = await owner.Clipboard!.TryGetDataAsync())
            {
                Assert.NotNull(copied); Assert.Equal(draft, await copied.TryGetTextAsync());
                Assert.Null(await copied.TryGetBitmapAsync());
                var html = Encoding.UTF8.GetString((await copied.TryGetValueAsync(ResultClipboardPayload.HtmlFormat))!);
                Assert.Contains("<div", html); Assert.DoesNotContain("<table", html); Assert.DoesNotContain("<img", html);
                Assert.Contains("font-size:12pt;color:#2F5496", html); Assert.DoesNotContain("background", html);
                Assert.Contains(@"\fs24\cf1", Encoding.ASCII.GetString((await copied.TryGetValueAsync(ResultClipboardPayload.RtfFormat))!));
            }
            model.ResultText += "Дополнено в редакторе";
            await model.CopyResultAsync(clipboard);
            using (var copied = await owner.Clipboard!.TryGetDataAsync())
            {
                Assert.NotNull(copied);
                Assert.Equal(model.ResultText, await copied.TryGetTextAsync());
                Assert.Contains("Дополнено в редакторе", Encoding.UTF8.GetString((await copied.TryGetValueAsync(ResultClipboardPayload.HtmlFormat))!));
            }
            Click(owner, "PersonalizeButton"); Click(owner, "ResetLayoutButton"); Click(owner, "CancelLayoutButton");
            Assert.Equal(draft + "Дополнено в редакторе", model.ResultText);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
        var nextModel = new MainWindowViewModel(new DemoOrderStore());
        var next = new MainWindow(preferences) { DataContext = nextModel };
        try
        {
            next.Show(); Pump();
            Assert.Equal(dark, nextModel.IsDarkTheme);
            Assert.Equal(12m, nextModel.ResultFontSizePoints); Assert.Equal("#2F5496", nextModel.ResultTextColor);
        }
        finally { next.Close(); }
    }

    private static void Click(Window window, string name)
    {
        var control = window.FindDescendant<Control>(name)!;
        control.BringIntoView(); Pump();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Screenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
