using Avalonia.Controls;
using System.ComponentModel;
using Jornal.Core.Orders;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
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
    public async Task MainBatchDropOpensCardsImmediatelyAndSavesEverySelectedRow(bool dark)
    {
        using var sample = new SpecificationImportTests.Sample();
        var second = Path.Combine(Path.GetDirectoryName(sample.Path)!, "вторая.html"); File.Copy(sample.Path, second);
        var unsupported = Path.ChangeExtension(sample.Path, ".txt"); File.WriteAllText(unsupported, "Другой документ");
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { IsDarkTheme = dark, ResultText = "Письмо" };
        var reader = new SpecificationImportTests.BlockingReader();
        var window = new MainWindow { DataContext = model, SpecificationImporter = reader };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var grid = window.FindControl<DataGrid>("OrdersGrid")!;
            grid.Columns.Single(column => column.Header is "Дата").Sort(ListSortDirection.Descending);
            using var files = await Transfer(window, sample.Path, second, unsupported);
            Assert.Equal(DragDropEffects.Copy, Drag(window, DragDrop.DragOverEvent, files).DragEffects);
            Drag(window, DragDrop.DropEvent, files);
            await Until(() => window.OwnedWindows.OfType<NewOrderWindow>().Any());
            var dialog = Assert.Single(window.OwnedWindows.OfType<NewOrderWindow>());
            var form = Assert.IsType<NewOrderViewModel>(dialog.DataContext);
            Assert.True(form.IsBatchMode); Assert.Equal(3, form.BatchItems.Count); Assert.True(form.IsBatchReading);
            Assert.True(dialog.FindControl<JournalButton>("BrowseSpecificationButton")!.IsEnabled);
            AssertVisibleActions(dialog); Screenshot(dialog, "batch-reading", dark);
            reader.Result.SetResult(await new SpecificationImportService().ReadAsync(sample.Path, TestContext.Current.CancellationToken));
            await Until(() => !form.IsBatchReading);
            Assert.True(form.BatchItems[2].HasFailure); Assert.False(form.SaveCommand.CanExecute(null));
            form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
            form.BatchItems[1].Form.OrderDate.Value = "15.05.2026";
            form.BatchItems[2].IsIncluded = false;
            Dispatcher.UIThread.RunJobs(); AssertVisibleActions(dialog); Screenshot(dialog, "batch-ready", dark);
            dialog.Width = 1060; dialog.Height = 600; Dispatcher.UIThread.RunJobs();
            AssertVisibleActions(dialog); Screenshot(dialog, "batch-compact", dark);
            await form.SaveCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.True(form.IsSaved); Assert.Empty(window.OwnedWindows);
            Assert.Equal(5, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
            Assert.Equal(2, grid.SelectedItems.Count); Assert.Equal("Письмо", model.ResultText);
            await model.ReloadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.All(form.CreatedOrders, row => Assert.Contains(model.Orders, visible => visible.Id == row.Id && visible.SpecificationNumber == row.SpecificationNumber));
            Assert.All(form.CreatedOrders, row => Assert.Contains(grid.CollectionView.Cast<OrderRowViewModel>(), visible => visible.Id == row.Id));
            Assert.Equal(new DateOnly?[] { new(2026, 5, 14), new(2026, 5, 15) }, form.CreatedOrders.Select(row => row.OrderDate));
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyNewOrderAcceptsOneFileAndThenAppendsBatchWithoutLosingEdits(bool dark)
    {
        using var sample = new SpecificationImportTests.Sample();
        var second = Path.Combine(Path.GetDirectoryName(sample.Path)!, "вторая.html"); File.Copy(sample.Path, second);
        var third = Path.Combine(Path.GetDirectoryName(sample.Path)!, "третья.html"); File.Copy(sample.Path, third);
        var form = new NewOrderViewModel(new DemoOrderStore());
        var window = new NewOrderWindow { DataContext = form, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            using var file = await Transfer(window, sample.Path);
            Assert.Equal(DragDropEffects.Copy, Drag(window, DragDrop.DragOverEvent, file).DragEffects);
            Assert.True(window.FindControl<Border>("NewOrderSpecificationOverlay")!.IsVisible);
            Drag(window, DragDrop.DropEvent, file);
            await Until(() => form.HasImport);
            Assert.False(form.IsBatchMode); Assert.Equal("0514-0078", form.SpecificationNumber.Value);
            form.Weight.Value = "999"; form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
            await window.ImportSpecificationsAsync([second]); Dispatcher.UIThread.RunJobs();
            Assert.True(form.IsBatchMode); Assert.Equal(2, form.BatchItems.Count);
            await window.ImportSpecificationsAsync([third]); Dispatcher.UIThread.RunJobs();
            Assert.True(form.IsBatchMode); Assert.Equal(3, form.BatchItems.Count);
            Assert.Equal("999", form.BatchItems[0].Form.Weight.Value); AssertVisibleActions(window);
            Assert.True(form.SaveCommand.CanExecute(null));
            Screenshot(window, "batch-from-single", dark);
            var txt = Path.ChangeExtension(sample.Path, ".txt"); File.WriteAllText(txt, "Текст");
            using var bad = await Transfer(window, txt);
            Assert.Equal(DragDropEffects.None, Drag(window, DragDrop.DragOverEvent, bad).DragEffects);
            Assert.True(form.HasDropRejection); Screenshot(window, "batch-rejected", dark);
            Drag(window, DragDrop.DragLeaveEvent, bad); Assert.False(form.IsDraggingFiles);
            await window.ImportSpecificationsAsync([txt]); Assert.True(form.HasImportError);
            Assert.Equal(3, form.BatchItems.Count); Assert.Equal("999", form.BatchItems[0].Form.Weight.Value);
        }
        finally { window.Close(); }
    }
}
