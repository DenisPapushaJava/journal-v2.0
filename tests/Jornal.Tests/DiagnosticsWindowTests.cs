using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

public sealed class DiagnosticsWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticsShowUnavailableReceiverAndSaveTheUsersChoice(bool dark)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-diagnostic-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var http = new HttpClient();
        using var service = new DiagnosticService(new(root), http, null, "2026.1.0-dev");
        var model = new DiagnosticsViewModel(service);
        var window = new DiagnosticsWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.FindDescendant<CheckBox>("AutomaticReportsCheckBox")!.IsChecked);
            Assert.Contains("ещё не подключён", model.Status);
            foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible && control is TextBlock or CheckBox or JournalButton))
            {
                var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
                Assert.True(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= window.ClientSize.Width + 1 && bounds.Bottom <= window.ClientSize.Height + 1);
            }
            if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
                bitmap.Render(window); bitmap.Save(Path.Combine(directory, $"diagnostics-{dark}.png"), new PngBitmapEncoderOptions());
            }
            model.SendAutomatically = false; Dispatcher.UIThread.RunJobs();
            Assert.False(new DiagnosticStore(root).LoadAutomatic());
            Assert.Equal("Автоотправка отключена", model.Status);
            model.ExportStatus = "Отчёт сохранён"; model.Refresh();
            Assert.Equal("Отчёт сохранён", model.ExportStatus);
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
}
