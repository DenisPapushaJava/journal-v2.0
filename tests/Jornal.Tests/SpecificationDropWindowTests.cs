using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class SpecificationDropWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoutedDragDropShowsOverlayThenPopulatesFormWithoutSaving(bool dark)
    {
        using var sample = new SpecificationImportTests.Sample();
        var preview = await new SpecificationImportService().ReadAsync(sample.Path, TestContext.Current.CancellationToken);
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark, ResultText = "Письмо" };
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader, Width = 1366, Height = 768 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var transfer = await Transfer(window, sample.Path);
            var enter = Drag(window, DragDrop.DragEnterEvent, transfer);
            Assert.Equal(DragDropEffects.Copy, enter.DragEffects); Assert.True(model.IsDraggingSpecification);
            var overlay = window.FindControl<Border>("SpecificationOverlay")!;
            Assert.True(overlay.IsVisible); Screenshot(window, "drop-hover", dark);
            window.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, transfer, window, new Point(50, 50), KeyModifiers.None));
            overlay.RaiseEvent(new DragEventArgs(DragDrop.DragEnterEvent, transfer, overlay, new Point(50, 50), KeyModifiers.None));
            Dispatcher.UIThread.RunJobs(); Assert.True(overlay.IsVisible);
            Drag(window, DragDrop.DragLeaveEvent, transfer);
            Assert.False(overlay.IsVisible);
            Drag(window, DragDrop.DragOverEvent, transfer);
            transfer.Add(DataTransferItem.CreateText("Путь файла не должен попасть в результат"));
            Drag(window, DragDrop.DropEvent, transfer, overlay);
            Assert.True(model.IsImportingSpecification); Assert.False(model.CanCreateOrder); Assert.Empty(window.OwnedWindows);
            Assert.True(window.FindControl<JournalButton>("CancelSpecificationImportButton")!.IsFocused);
            Screenshot(window, "drop-reading", dark);
            // A repeated drop cannot start another read or create another dialog.
            Drag(window, DragDrop.DropEvent, transfer); Assert.Equal(1, reader.Calls);
            reader.Result.SetResult(preview);
            await Until(() => window.OwnedWindows.OfType<NewOrderWindow>().Any());
            var dialog = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            var form = (NewOrderViewModel)dialog.DataContext!;
            Assert.False(overlay.IsVisible); Assert.Equal("0514-0078", form.SpecificationNumber.Value);
            Assert.Equal("1500x4000, 1000x3000", form.SheetSize.Value); Assert.Equal("1.1", form.Coefficient.Value);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, dialog.ActualThemeVariant);
            Assert.Equal("Письмо", model.ResultText);
            Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
            AssertVisibleActions(dialog);
            Screenshot(dialog, "import-form", dark);
            dialog.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task EscapeAndClosingCancelReadingAndNeverOpenALateForm()
    {
        using var sample = new SpecificationImportTests.Sample();
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store);
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.FindControl<TextBox>("SearchBox")!.Focus();
            var reading = window.ImportSpecificationsAsync([sample.Path]);
            Assert.True(model.IsImportingSpecification);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await reading; Dispatcher.UIThread.RunJobs();
            Assert.False(model.ShowSpecificationOverlay); Assert.Empty(window.OwnedWindows);
            Assert.True(window.FindControl<TextBox>("SearchBox")!.IsFocused);
            var closing = window.ImportSpecificationsAsync([sample.Path]);
            Assert.True(model.IsImportingSpecification); window.Close(); await closing;
            Assert.False(model.IsImportingSpecification); Assert.Empty(window.OwnedWindows);
            Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MultipleUnsupportedAndTextDropsNeverSilentlyImportTheFirstFile()
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { ResultText = "Письмо" };
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var sample = new SpecificationImportTests.Sample();
            using var files = await Transfer(window, sample.Path, sample.Path);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, files).DragEffects);
            Assert.Contains("одну", model.SpecificationOverlayDescription);
            Drag(window, DragDrop.DropEvent, files); Assert.Contains("одну", model.ErrorMessage);
            var pdfPath = Path.ChangeExtension(sample.Path, ".pdf"); File.WriteAllText(pdfPath, "Not a real PDF");
            using var pdf = await Transfer(window, pdfPath);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, pdf).DragEffects);
            Assert.Contains("PDF", model.SpecificationOverlayDescription);
            Drag(window, DragDrop.DropEvent, pdf); Assert.Equal(0, reader.Calls);
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText("Текст"));
            Assert.False(Drag(window, DragDrop.DropEvent, text).Handled);
            Assert.Equal("Письмо", model.ResultText); Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialReportAndLongFileNameFitTheCompactForm(bool dark)
    {
        using var sample = new SpecificationImportTests.Sample(SpecificationImportTests.Sample.Html.Replace("<td>Вес:(кг)</td><td>48.2</td>", ""));
        var preview = await new SpecificationImportService().ReadAsync(sample.Path, TestContext.Current.CancellationToken);
        var form = new NewOrderViewModel(new DemoOrderStore()); form.ApplyImport(preview with { FileName = new string('Ж', 800) + ".html" });
        var window = new NewOrderWindow { DataContext = form, Width = 1060, Height = 600, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(form.Weight.Value); Assert.True(form.HasImportDiagnostics); AssertVisibleActions(window);
            var name = window.FindControl<TextBlock>("ImportFileName")!;
            Assert.True(name.Bounds.Width <= 212);
            Screenshot(window, "import-partial-compact", dark);
        }
        finally { window.Close(); }
    }

    private static void AssertVisibleActions(Window window)
    {
        foreach (var name in new[] { "SaveButton", "CancelButton" })
        {
            var button = window.FindControl<JournalButton>(name)!;
            var bounds = new Rect(button.Bounds.Size).TransformToAABB(button.TransformToVisual(window)!.Value);
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= window.Bounds.Height && bounds.Right <= window.Bounds.Width);
        }
    }
    private static DragEventArgs Drag(Window window, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer transfer, Control? target = null)
    {
        var source = target ?? window;
        var args = new DragEventArgs(routedEvent, transfer, source, new Point(50, 50), KeyModifiers.None);
        source.RaiseEvent(args); Dispatcher.UIThread.RunJobs(); return args;
    }
    private static async Task<DataTransfer> Transfer(Window window, params string[] paths)
    {
        var transfer = new DataTransfer();
        foreach (var path in paths)
        {
            var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
            Assert.NotNull(file);
            transfer.Add(DataTransferItem.CreateFile(file));
        }
        return transfer;
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Dispatcher.UIThread.RunJobs(); Assert.True(condition());
    }
    private static void Screenshot(Window window, string name, bool dark)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } root) return;
        Directory.CreateDirectory(root);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height)); bitmap.Render(window);
        bitmap.Save(System.IO.Path.Combine(root, name + (dark ? "-dark.png" : "-light.png")), new PngBitmapEncoderOptions());
    }
}
