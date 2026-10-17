using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;
namespace Jornal.Tests;
public sealed partial class SpecificationDropWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task EditorReplacesHtmlAndPdfAcrossMachinesAndShowsDiffWithoutCreatingRows(bool dark, bool pdf)
    {
        using var html = new SpecificationImportTests.Sample(); using var hans = pdf ? new HansPdfFile() : null;
        var path = hans?.Path ?? html.Path;
        var store = new EditSpecificationImportTests.Store();
        store.Current = pdf ? store.Current! with { Coefficient = null } : store.Current! with { CuttingType = "HansLaser" };
        var original = store.Current;
        var form = EditSpecificationImportTests.Form(store);
        var window = new EditOrderWindow { DataContext = form, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light, Width = 1060, Height = 600 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); using var file = await Transfer(window, path);
            Assert.Equal(DragDropEffects.Copy, Drag(window, DragDrop.DragOverEvent, file).DragEffects);
            var overlay = window.FindControl<Border>("EditSpecificationOverlay")!;
            Assert.True(overlay.IsEffectivelyVisible);
            Assert.DoesNotContain(overlay.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible);
            Drag(window, DragDrop.DropEvent, file); await Until(() => form.HasPendingImport);
            Assert.Equal(original, store.Current); Assert.Equal(original!.Specification, form.SpecificationNumber.Value);
            Assert.False(window.FindControl<JournalButton>("SaveButton")!.IsEnabled);
            Assert.True(window.FindControl<JournalButton>("ReplaceImportButton")!.IsFocused);
            AssertVisibleActions(window);
            foreach (var name in new[] { "ReplaceImportButton", "KeepImportButton" })
            {
                var button = window.FindControl<JournalButton>(name)!;
                var bounds = new Rect(button.Bounds.Size).TransformToAABB(button.TransformToVisual(window)!.Value);
                Assert.True(button.IsEffectivelyVisible); Assert.True(bounds.Bottom <= 600 && bounds.Right <= 1060);
            }
            Screenshot(window, pdf ? "edit-hans-confirm" : "edit-block-confirm", dark);
            var replace = window.FindControl<JournalButton>("ReplaceImportButton")!;
            var p = replace.TranslatePoint(new Point(replace.Bounds.Width / 2, replace.Bounds.Height / 2), window)!.Value;
            window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(form.HasImport); Assert.False(form.HasPendingImport); Assert.Equal(0, store.Updates);
            Assert.Equal(pdf ? "HansLaser" : "Лазер", form.CuttingType.Value); Assert.Equal(pdf ? "N2" : "", form.Gas.Value);
            Assert.Equal(original.Id, form.Id); Assert.Contains(form.Changes, change => change.Label == "Вид резки / станок");
            Assert.True(window.FindControl<StackPanel>("OrderChangesPanel")!.IsEffectivelyVisible);
            AssertVisibleActions(window); Screenshot(window, pdf ? "edit-hans-diff" : "edit-block-diff", dark);
            await form.CalculateCommand.ExecuteAsync(null); await form.SaveCommand.ExecuteAsync(null);
            Assert.Equal(1, store.Updates); Assert.Equal(original.Id, store.Current!.Id);
            Assert.Equal(original.DeliveryNumber, store.Current.DeliveryNumber); Assert.Equal(original.Color, store.Current.Color);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task EditorRejectsMultipleFilesCancelsByEscapeAndNeverAcceptsLateResults()
    {
        using var sample = new SpecificationImportTests.Sample(); var store = new EditSpecificationImportTests.Store();
        var form = EditSpecificationImportTests.Form(store); var reader = new SpecificationImportTests.BlockingReader(); form.ImportReader = reader;
        var window = new EditOrderWindow { DataContext = form };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); using var files = await Transfer(window, sample.Path, sample.Path);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, files).DragEffects);
            Assert.True(form.HasDropRejection); Assert.Contains("одну", form.DropRejection);
            Drag(window, DragDrop.DragLeaveEvent, files); Assert.False(form.IsDraggingFiles);
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText("обычный текст")); Assert.False(Drag(window, DragDrop.DropEvent, text).Handled);
            var values = form.Fields.Select(entry => entry.Value).ToArray();
            var reading = window.ImportSpecificationsAsync([sample.Path]); Assert.True(form.IsImporting);
            Assert.False(window.FindControl<JournalButton>("BrowseSpecificationButton")!.IsEnabled);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await reading; Assert.True(window.IsVisible); Assert.False(form.HasPendingImport);
            Assert.Equal(values, form.Fields.Select(entry => entry.Value));
            reading = window.ImportSpecificationsAsync([sample.Path]); window.Close(); await reading;
            reader.Result.SetResult(await BatchImportTests.Preview());
            Assert.False(form.HasPendingImport); Assert.Equal(0, store.Updates);
        }
        finally { window.Close(); }
    }
}
