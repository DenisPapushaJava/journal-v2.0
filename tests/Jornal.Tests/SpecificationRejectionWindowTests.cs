using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed partial class SpecificationDropWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedHoverShowsBanAndReasonNeutralHoverStaysEmptyAndNeverReads(bool dark)
    {
        using var sample = new SpecificationImportTests.Sample();
        var other = Path.ChangeExtension(sample.Path, ".exe"); File.WriteAllText(other, "not executable");
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark };
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader, Width = 1280, Height = 680 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var unsupported = await Transfer(window, other);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragEnterEvent, unsupported).DragEffects);
            var overlay = window.FindControl<Border>("SpecificationOverlay")!;
            var card = window.FindControl<Border>("SpecificationRejectionPanel")!;
            Assert.True(card.IsEffectivelyVisible); Assert.Contains("rejected", overlay.Classes);
            Assert.Contains("PDF", window.FindControl<TextBlock>("SpecificationRejectionMessage")!.Text);
            Assert.False(window.FindControl<JournalButton>("DismissSpecificationImportErrorButton")!.IsVisible);
            var red = Assert.IsAssignableFrom<ISolidColorBrush>(overlay.Background).Color;
            Assert.True(red.R > red.G && red.A < 180);
            var icon = Assert.Single(card.GetVisualDescendants().OfType<Avalonia.Svg.Svg>());
            Assert.NotNull(icon.Model);
            var bounds = new Rect(card.Bounds.Size).TransformToAABB(card.TransformToVisual(window)!.Value);
            Assert.True(bounds.Right <= 1280 && bounds.Bottom <= 680);
            Screenshot(window, "reject-hover", dark);
            Drag(window, DragDrop.DragLeaveEvent, unsupported);
            Assert.False(overlay.IsVisible);
            using var supported = await Transfer(window, sample.Path);
            Assert.Equal(DragDropEffects.Copy, Drag(window, DragDrop.DragOverEvent, supported).DragEffects);
            Assert.True(overlay.IsVisible); Assert.False(card.IsVisible); Assert.False(model.ShowSpecificationReading);
            Assert.DoesNotContain(overlay.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible);
            Assert.Equal(0, reader.Calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownDocumentDoesNotOpenFormDismissRestoresFocusAndRetryWorks(bool dark)
    {
        using var bad = new SpecificationImportTests.Sample("<html><body>Счёт на оплату. Материал. Вес. HANSLASER.</body></html>");
        using var good = new SpecificationImportTests.Sample();
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark, ResultText = "Письмо" };
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 680 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<TextBox>("SearchBox")!; search.Focus();
            await window.ImportSpecificationsAsync([bad.Path]); Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.OwnedWindows); Assert.True(model.ShowSpecificationRejection);
            var close = window.FindControl<JournalButton>("DismissSpecificationImportErrorButton")!;
            Assert.True(close.IsFocused); Assert.NotNull(Avalonia.Automation.AutomationProperties.GetName(close));
            Assert.Contains("не распознан", window.FindControl<TextBlock>("SpecificationRejectionMessage")!.Text);
            Assert.Null(model.ErrorMessage); Assert.Equal("Письмо", model.ResultText);
            Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
            Screenshot(window, "reject-content", dark);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Dispatcher.UIThread.RunJobs();
            Assert.False(model.ShowSpecificationOverlay); Assert.True(search.IsFocused);
            var retry = window.ImportSpecificationsAsync([good.Path]);
            await Until(() => window.OwnedWindows.OfType<NewOrderWindow>().Any());
            var form = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            Assert.False(model.HasSpecificationImportError); form.Close(); await retry;
            Assert.Equal("Письмо", model.ResultText);
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task FolderAndReadOnlyOrCustomizingStateShowReasonWithoutReading()
    {
        using var sample = new SpecificationImportTests.Sample();
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store);
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var supported = await Transfer(window, sample.Path);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, supported).DragEffects);
            Assert.Contains("только для чтения", model.SpecificationRejectionText);
            Drag(window, DragDrop.DropEvent, supported);
            Assert.True(model.HasSpecificationImportError); Assert.Equal(0, reader.Calls);
            model.DismissSpecificationImportErrorCommand.Execute(null);
            var editable = new MainWindowViewModel(store, store) { IsCustomizing = true }; window.DataContext = editable;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, supported).DragEffects);
            Assert.Contains("настройку экрана", editable.SpecificationRejectionText);
            Drag(window, DragDrop.DragLeaveEvent, supported); editable.IsCustomizing = false;
            using var folderTransfer = new DataTransfer();
            var folder = await window.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(sample.Path)!);
            Assert.NotNull(folder); folderTransfer.Add(DataTransferItem.CreateFile(folder));
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, folderTransfer).DragEffects);
            Assert.Contains("Папки", editable.SpecificationRejectionText);
            Assert.Equal(0, reader.Calls);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BusyReadRejectsAdditionalDragAndLeavingRestoresProgress()
    {
        using var sample = new SpecificationImportTests.Sample();
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store);
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var reading = window.ImportSpecificationsAsync([sample.Path]);
            using var files = await Transfer(window, sample.Path);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, files).DragEffects);
            Assert.True(model.ShowSpecificationRejection); Assert.False(model.ShowSpecificationReading);
            Assert.Contains("завершения импорта", model.SpecificationRejectionText);
            Drag(window, DragDrop.DragLeaveEvent, files);
            Assert.False(model.ShowSpecificationRejection); Assert.True(model.ShowSpecificationReading);
            Assert.Equal(1, reader.Calls);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await reading;
            Assert.Empty(window.OwnedWindows);
        }
        finally { window.Close(); }
    }
}
