using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class UpdateWindowTests
{
    [AvaloniaFact]
    public async Task ClosingTheMainWindowCancelsItsAutomaticCheck()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updater = new AppUpdatesTests.FakeUpdater
        {
            CheckHandler = async token =>
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled.SetResult(); throw; }
                return null;
            }
        };
        var updates = new AppUpdatesViewModel(updater) { CheckAutomatically = true };
        var owner = new MainWindow { DataContext = new MainWindowViewModel(new DemoOrderStore(), appUpdates: updates) };
        try
        {
            owner.Show(); Pump();
            Assert.Equal(1, updater.Checks);
            Assert.True(updates.IsBusy);
            owner.Close(); Pump();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Pump();
            Assert.False(updates.IsBusy);
            Assert.False(updates.HasError);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BellOpensUpdateWindowAndLongNotesDoNotHideActions(bool dark)
    {
        var updater = new AppUpdatesTests.FakeUpdater();
        var updates = new AppUpdatesViewModel(updater);
        var model = new MainWindowViewModel(new DemoOrderStore(), appUpdates: updates) { IsDarkTheme = dark };
        var owner = new MainWindow { DataContext = model, Width = 1280, Height = 720 };
        try
        {
            owner.Show(); Pump();
            Assert.Equal(0, updater.Checks);
            Assert.False(owner.FindControl<Control>("UpdateBadge")!.IsVisible);
            await updates.CheckCommand.ExecuteAsync(null); Pump();
            Assert.True(owner.FindControl<Control>("UpdateBadge")!.IsVisible);
            Click(owner, "UpdatesButton");
            var dialog = Assert.Single(owner.OwnedWindows.OfType<AppUpdatesWindow>());
            Assert.Equal(owner.ActualThemeVariant, dialog.ActualThemeVariant);
            dialog.Width = 600; dialog.Height = 540;
            updates.ReleaseNotes = string.Join('\n', Enumerable.Repeat("Длинное описание новой версии с подробностями исправлений", 80));
            updates.Error = "Проверка сообщения: соединение недоступно. Повторите попытку или продолжите работу."; Pump();
            updates.PreferencesWarning = "Не удалось сохранить канал обновлений. Выбор действует до закрытия программы. Проверьте доступ к папке профиля."; Pump();
            Assert.Equal("Получать тестовые версии", dialog.FindControl<CheckBox>("PreviewUpdatesCheckBox")!.Content);
            Assert.False(dialog.FindControl<JournalButton>("InstallUpdateButton")!.IsVisible);
            Assert.True(dialog.FindControl<JournalButton>("DownloadUpdateButton")!.IsEffectivelyEnabled);
            foreach (var name in new[] { "CheckUpdatesButton", "DownloadUpdateButton", "CloseUpdatesButton", "UpdateError", "AutomaticUpdatesCheckBox", "PreviewUpdatesCheckBox", "UpdatePreferencesWarning" }) AssertInside(dialog, name);
            Screenshot(dialog, "update-available-" + (dark ? "dark" : "light"));
            Click(dialog, "DownloadUpdateButton"); Pump();
            Assert.True(dialog.FindControl<JournalButton>("InstallUpdateButton")!.IsEffectivelyEnabled);
            Assert.Equal(0, updater.Restarts);
            AssertInside(dialog, "InstallUpdateButton"); AssertInside(dialog, "CloseUpdatesButton");
            Screenshot(dialog, "update-downloaded-" + (dark ? "dark" : "light"));
            Click(dialog, "CloseUpdatesButton"); Assert.False(dialog.IsVisible);
            model.IsUpdatingOrders = true; Assert.False(model.CanCheckUpdates);
            model.IsUpdatingOrders = false; model.IsCustomizing = true; Assert.False(model.CanCheckUpdates);
            model.IsCustomizing = false; Assert.True(model.CanCheckUpdates);
        }
        finally { foreach (var child in owner.OwnedWindows.ToArray()) child.Close(); owner.Close(); }
    }

    private static void AssertInside(Window window, string name)
    {
        var control = window.FindControl<Control>(name)!;
        var rect = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
        Assert.True(rect.Top >= 0 && rect.Bottom <= window.Height + 1 && rect.Right <= window.Width + 1, $"{name}: {rect}");
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(Window window, string name)
    {
        var c = window.FindControl<Control>(name)!; var point = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void Screenshot(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window); bitmap.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
    }
}
