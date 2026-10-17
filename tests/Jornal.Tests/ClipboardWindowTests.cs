using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Results;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ClipboardWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task SelectionNoticeFitsAndSurvivesSortingAndStatusUntilDeliveryIsFixed(bool dark)
    {
        var (model, _) = await ClipboardSelectionNoticeTests.Model();
        var window = new MainWindow(new MemoryUiPreferencesStore()) { DataContext = model, Width = 1280, Height = 720,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); await window.LoadInitialOrdersAsync(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            var rows = model.Orders.Where(row => row.Id > 3).OrderBy(row => row.Id).ToArray();
            foreach (var row in rows) grid.SelectedItems.Add(row);
            var clipboard = new RecordingJournalClipboard { Text = "Прежний буфер" };
            await model.CopyCommentAsync(clipboard, true); Dispatcher.UIThread.RunJobs();
            Assert.True(model.HasSelectionNotice);
            var text = window.FindControl<TextBlock>("JournalErrorText")!;
            Assert.Contains(rows[0].SpecificationNumber, text.Text); Assert.Contains(rows[1].SpecificationNumber, text.Text);
            Assert.True(window.FindControl<Border>("LoadErrorPanel")!.Bounds.Height < 100);
            if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory); using var image = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); image.Render(window);
                image.Save(Path.Combine(directory, $"clipboard-notice-{dark}.png"), new PngBitmapEncoderOptions());
            }
            grid.Columns.Single(c => Equals(c.Header, "Спецификация")).Sort(System.ComponentModel.ListSortDirection.Ascending);
            Dispatcher.UIThread.RunJobs(); Assert.True(model.HasSelectionNotice); Assert.Equal(rows.Length, grid.SelectedItems.Count);
            await model.SetOrderColorCommand.ExecuteAsync(Jornal.Core.Orders.OrderColors.Ready);
            Dispatcher.UIThread.RunJobs(); Assert.True(model.HasSelectionNotice); Assert.Equal(rows.Length, grid.SelectedItems.Count);
            var dismiss = window.FindControl<JournalButton>("DismissSelectionNoticeButton")!;
            Assert.True(dismiss.IsEffectivelyVisible);
            var point = dismiss.TranslatePoint(new Point(dismiss.Bounds.Width / 2, dismiss.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.False(model.HasError); Assert.False(dismiss.IsVisible);
            await model.CopyCommentAsync(clipboard, true); Assert.True(model.HasSelectionNotice);
            var delivery = model.CreateDeliveryNumberForm()!; delivery.Number = "9285";
            await delivery.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.True(delivery.IsSaved); Assert.False(model.HasError); Assert.False(dismiss.IsVisible);
            Assert.Equal("Прежний буфер", clipboard.Text);
            await model.CopyCommentAsync(clipboard, true); Assert.Contains("9285", clipboard.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task FolderPreviewIsCompactAndBusyCopyProtectsBothWindows(bool dark)
    {
        var model = new MainWindowViewModel(new DemoOrderStore()); await model.ReloadCommand.ExecuteAsync(null);
        var owner = new MainWindow(new MemoryUiPreferencesStore()) { DataContext = model, Width = 1366, Height = 768, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        var clipboard = new BlockingClipboard();
        var window = new FolderClipboardWindow { Width = 620, Height = 400, RequestedThemeVariant = owner.RequestedThemeVariant };
        try
        {
            owner.Show(); await owner.LoadInitialOrdersAsync(); Dispatcher.UIThread.RunJobs();
            model.AcceptCreatedOrders([JournalClipboardTests.Order(), JournalClipboardTests.Order(101) with { CustomerName = "ООО \"Заказчик:/\"" }]);
            var table = owner.FindControl<DataGrid>("OrdersGrid")!;
            foreach (var row in model.Orders.Where(row => row.Id >= 100)) table.SelectedItems.Add(row);
            Dispatcher.UIThread.RunJobs();
            var form = model.CreateFolderForm(clipboard)!; window.DataContext = form;
            Assert.True(owner.FindControl<JournalButton>("CopyTelegramCommentButton")!.IsEnabled);
            Assert.False(owner.FindControl<JournalButton>("CopyRowCommentButton")!.IsEnabled);
            var shown = window.ShowDialog(owner); Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, form.Items.Count); Assert.True(form.HasRenamed);
            foreach (var button in window.GetVisualDescendants().OfType<JournalButton>().Where(button => button.IsEffectivelyVisible))
            {
                var point = button.TranslatePoint(default, window)!.Value;
                Assert.True(point.X >= 0 && point.Y >= 0 && point.X + button.Bounds.Width <= window.ClientSize.Width + 0.1 && point.Y + button.Bounds.Height <= window.ClientSize.Height + 0.1);
            }
            if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory); using var image = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); image.Render(window);
                image.Save(Path.Combine(directory, $"clipboard-folders-{dark}.png"), new PngBitmapEncoderOptions());
            }
            var copying = form.CopyCommand.ExecuteAsync(null); Assert.True(form.IsBusy); Assert.True(model.IsCopyingSelection);
            window.Close(); owner.Close(); Assert.True(window.IsVisible); Assert.True(owner.IsVisible);
            clipboard.Done.SetResult(); await copying; await shown; Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible); Assert.False(model.IsCopyingSelection); Assert.Contains("Папки готовы", model.UpdatedText);
        }
        finally { clipboard.Done.TrySetResult(); window.Close(); owner.Close(); }
    }
    private sealed class BlockingClipboard : Jornal.Desktop.Clipboard.IJournalClipboard
    {
        public int MaximumFolderNameLength => 140;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WriteTextAsync(string text) => Done.Task;
        public Task WriteFoldersAsync(OrderFolderPlan plan) => Done.Task;
    }
}
