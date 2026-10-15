using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Jornal.Desktop.Clipboard;
using Jornal.Core.Results;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultWindowTests
{
    [AvaloniaTheory]
    [InlineData(1280, 720, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, false)]
    [InlineData(1366, 768, true)]
    public async Task ResultButtonsCopyAllFormatsAndClearWithoutLosingSelection(int width, int height, bool dark)
    {
        var model = new MainWindowViewModel(new DemoOrderStore()) { IsDarkTheme = dark };
        var window = new MainWindow { DataContext = model, Width = width, Height = height };
        try
        {
            window.Show();
            Pump();
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            var text = window.FindControl<TextBox>("ResultTextBox")!;
            Assert.False(text.IsReadOnly);
            Assert.False(window.FindControl<JournalButton>("CopyResultButton")!.IsEnabled);
            table.SelectAll();
            Pump();
            Click(window, "AddResultButton");
            Assert.Equal(3, model.ResultText.Count(character => character == '\n'));
            Assert.Equal(model.ResultText, text.Text);
            Click(window, "AddResultButton");
            Assert.Equal(3, model.ResultText.Count(character => character == '\n'));
            text.Text = "Комментарий для письма\n" + text.Text;
            Pump();
            Click(window, "CopyResultButton");
            Assert.False(model.HasResultError);
            Assert.Contains("Скопировано", model.ResultMessage);
            using (var copied = await window.Clipboard!.TryGetDataAsync())
            {
                Assert.NotNull(copied);
                Assert.Equal(text.Text, await copied.TryGetTextAsync());
                Assert.NotEmpty((await copied.TryGetValueAsync(ResultClipboardPayload.HtmlFormat))!);
                Assert.NotEmpty((await copied.TryGetValueAsync(ResultClipboardPayload.RtfFormat))!);
            }
            foreach (var name in new[] { "AddResultButton", "ClearResultButton", "CopyResultButton" })
            {
                var button = window.FindControl<JournalButton>(name)!;
                Assert.True(((Control)button.Content!).Bounds.Width <= button.Bounds.Width + 1);
                Assert.NotNull(ToolTip.GetTip(button));
            }
            SaveScreenshot(window, $"result-{width}-{(dark ? "dark" : "light")}");
            var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(directory))
                File.WriteAllText(Path.Combine(directory, "result-email.html"), ResultClipboardPayload.HtmlDocument(ResultClipboardContent.FromText(model.ResultText).Html));
            Click(window, "PersonalizeButton");
            Assert.True(text.IsReadOnly);
            Assert.False(window.FindControl<JournalButton>("CopyResultButton")!.IsEnabled);
            Click(window, "ResetLayoutButton");
            Click(window, "CancelLayoutButton");
            Assert.Equal(model.ResultText, text.Text);
            Click(window, "ClearResultButton");
            Assert.Empty(model.ResultText);
            Assert.Equal(3, table.SelectedItems.Count);
            Click(window, "AddResultButton");
            Assert.Equal(3, model.ResultText.Count(character => character == '\n'));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ResultUsesVisibleSortOrderAndCanStillBeSelectedWithKeyboard()
    {
        var model = new MainWindowViewModel(new DemoOrderStore());
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show();
            Pump();
            var table = window.FindControl<DataGrid>("OrdersGrid")!;
            table.CollectionView.SortDescriptions.Add(Avalonia.Collections.DataGridSortDescription.FromPath(nameof(OrderRowViewModel.ThicknessMillimeters), System.ComponentModel.ListSortDirection.Descending));
            table.SelectAll();
            Pump();
            Click(window, "AddResultButton");
            var expected = table.CollectionView.Cast<OrderRowViewModel>().First();
            Assert.Equal(3, expected.Id);
            Assert.StartsWith("Лист   16x1500x6000", model.ResultText);
            var text = window.FindControl<TextBox>("ResultTextBox")!;
            text.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, null);
            Pump();
            Assert.Equal(model.ResultText, text.SelectedText);
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Pump();
            Assert.Equal("", model.ResultText);
            Click(window, "AddResultButton");
            Assert.Equal(3, model.ResultText.Count(character => character == '\n'));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ClipboardAdapterRejectsUnavailableClipboardWithoutChangingResult()
    {
        var model = new MainWindowViewModel(new DemoOrderStore());
        await model.ReloadCommand.ExecuteAsync(null);
        model.AddOrdersToResult(model.Orders);
        var original = model.ResultText;
        await model.CopyResultAsync(new ResultClipboard(() => null));
        Assert.True(model.HasResultError);
        Assert.Equal(original, model.ResultText);
        Assert.True(model.CanCopyResult);
    }

    private static void Click(MainWindow window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Pump();
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void SaveScreenshot(MainWindow window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
