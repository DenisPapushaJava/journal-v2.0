using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Documents;
using Xunit;

namespace Jornal.Tests;

public sealed partial class SpecificationDropWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task DropIntoAnOpenNewOrderRestoresManagerInputAfterReading(bool dark, bool suggestions)
    {
        using var sample = new SpecificationImportTests.Sample();
        var reader = new SpecificationImportTests.BlockingReader();
        var model = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        if (suggestions) model.Manager.Suggestions = ["Иван", "Пётр"];
        var window = new NewOrderWindow { DataContext = model, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var manager = ManagerEntry(window, model);
            Assert.True(manager.IsKeyboardFocusWithin);
            using var file = await Transfer(window, sample.Path);
            Drag(window, DragDrop.DropEvent, file);
            await Until(() => reader.Calls == 1);
            Assert.False(manager.IsEffectivelyEnabled);
            reader.Result.SetResult(await BatchImportTests.Preview());
            await Until(() => model.HasImport && !model.IsImporting);
            Dispatcher.UIThread.RunJobs();
            Assert.True(manager.IsEffectivelyEnabled);
            Assert.True(manager.IsKeyboardFocusWithin);
            window.KeyTextInput("Р"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Р", model.Manager.Value);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaTheory]
    [InlineData("other-control")] [InlineData("hidden")] [InlineData("closed")]
    [InlineData("child-window")] [InlineData("changed-model")]
    [InlineData("cancelled")] [InlineData("rejected")]
    public async Task CompletedDropDoesNotOverrideLaterUserActionsOrFocusARejectedImport(string action)
    {
        using var sample = new SpecificationImportTests.Sample();
        var reader = new SpecificationImportTests.BlockingReader();
        var model = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        var window = new NewOrderWindow { DataContext = model };
        Window? child = null;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var file = await Transfer(window, sample.Path);
            Drag(window, DragDrop.DropEvent, file);
            await Until(() => reader.Calls == 1);
            var cancel = window.FindControl<JournalButton>("CancelButton")!;
            switch (action)
            {
                case "other-control": Assert.True(cancel.Focus()); break;
                case "hidden": window.Hide(); break;
                case "closed": window.Close(); break;
                case "child-window": child = new Window(); child.Show(window); break;
                case "changed-model": window.DataContext = new NewOrderViewModel(new DemoOrderStore()); break;
                case "cancelled": model.CancelImportRead(); break;
            }
            Dispatcher.UIThread.RunJobs();
            if (action == "rejected") reader.Result.SetException(new DocumentRejectedException(DocumentRejection.UnsupportedFileType, "Не спецификация."));
            else reader.Result.SetResult(await BatchImportTests.Preview());
            await Until(() => !model.IsImporting);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<OrderEntryControl>(), control =>
                control.DataContext == model.Manager && control.IsKeyboardFocusWithin);
            if (action == "other-control") Assert.True(cancel.IsKeyboardFocusWithin);
            if (action is "cancelled" or "rejected" or "closed") Assert.False(model.HasImport);
        }
        finally { child?.Close(); window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task BatchDropFocusesTheSharedManagerUnlessTheUserStartedEditingAnotherField(bool editCustomer)
    {
        using var sample = new SpecificationImportTests.Sample();
        var second = Path.Combine(Path.GetDirectoryName(sample.Path)!, "second.html"); File.Copy(sample.Path, second);
        var reader = new SpecificationImportTests.BlockingReader();
        var model = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        var window = new NewOrderWindow { DataContext = model };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var file = await Transfer(window, sample.Path, second);
            Drag(window, DragDrop.DropEvent, file);
            await Until(() => reader.Calls == 2);
            var customer = window.GetVisualDescendants().OfType<OrderEntryControl>().Single(control =>
                control.IsEffectivelyVisible && control.DataContext == model.Customer);
            if (editCustomer) { customer.FocusInput(); window.KeyTextInput("Клиент"); }
            reader.Result.SetResult(await BatchImportTests.Preview());
            await Until(() => !model.IsReadingSpecifications);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBatchMode); Assert.Equal(2, model.BatchItems.Count);
            Assert.All(model.BatchItems, item => Assert.True(item.HasParameters));
            Assert.Equal(!editCustomer, ManagerEntry(window, model).IsKeyboardFocusWithin);
            if (editCustomer) { Assert.True(customer.IsKeyboardFocusWithin); Assert.Equal("Клиент", model.Customer.Value); }
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    }

    private static OrderEntryControl ManagerEntry(NewOrderWindow window, NewOrderViewModel model) => window.GetVisualDescendants()
        .OfType<OrderEntryControl>().Single(control => control.IsEffectivelyVisible && control.DataContext == model.Manager);
}
