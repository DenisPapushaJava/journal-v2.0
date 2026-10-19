using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Customization;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Local;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class MaintenanceWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedBatchStopsOnConflictOrUnknownCommitAndReportsEveryRow(bool unknown)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-trash-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "orders.journal-test.json");
            await LocalJournalStore.InitializeAsync(path, [], PriceTables.All.Select(name => new PriceTable(name, true, [new(2.5m, "2,5", 17)])).ToArray(),
                "synthetic.mdb", new string('a', 64), TestContext.Current.CancellationToken);
            var store = new LocalJournalStore(path);
            var records = Enumerable.Range(42, 3).Select(id => OrderMaintenanceTests.Record() with { Id = id, Specification = "0102-" + id, Customer = "Клиент " + id }).ToArray();
            foreach (var record in records) await store.RestoreAsync(record, TestContext.Current.CancellationToken);
            var backend = new InterruptedMaintenance(store, 43, unknown);
            var model = new MainWindowViewModel(store, trashStore: new JsonTrashStore(Path.Combine(root, "Trash")));
            model.InitializeSource(new(path, store, store, store, IsLocalCopy: true) { Maintenance = backend });
            await model.ReloadCommand.ExecuteAsync(null);
            model.UpdateSelection(model.Orders, model.Orders[0]);
            var selected = model.Orders.Select(row => row.Id).ToArray();
            Assert.Equal(new[] { 44, 43, 42 }, selected);
            model.AddOrdersToResult(model.Orders); var text = model.ResultText;
            var form = (await model.CreateTrashFormAsync(TestContext.Current.CancellationToken))!;
            foreach (var record in records) Assert.Contains(record.Customer!, form.Details);
            await form.ConfirmCommand.ExecuteAsync(null);
            Assert.False(form.IsConfirmed); Assert.True(form.HasOutcome); Assert.False(form.CanConfirm); Assert.Equal("Закрыть", form.CancelLabel);
            Assert.Contains("В корзине: 1 из 3", form.Outcome); Assert.Contains("Не обрабатывалась", form.Outcome);
            Assert.Contains(unknown ? "Нужно проверить" : "Не удалена", form.Outcome);
            Assert.Equal(2, backend.Attempts); Assert.True(model.RequiresReload);
            await form.ConfirmCommand.ExecuteAsync(null); Assert.Equal(2, backend.Attempts);
            Assert.Equal(text, model.ResultText); Assert.True(model.HasResultError);
            Assert.Null(await store.ReadRecordAsync(44, TestContext.Current.CancellationToken));
            Assert.NotNull(await store.ReadRecordAsync(42, TestContext.Current.CancellationToken));
            Assert.Equal(unknown, await store.ReadRecordAsync(43, TestContext.Current.CancellationToken) is null);
            Assert.Equal(unknown ? "2" : "1", model.TrashBadgeText);
            if (unknown) Assert.Contains("Требуют проверки: 1", model.TrashHint);
            var trash = model.CreateTrashViewModel()!;
            await trash.RefreshCommand.ExecuteAsync(null);
            Assert.False(model.RequiresReload);
            Assert.Equal(unknown ? 2 : 1, trash.Items.Count);
            Assert.All(trash.Items, item => Assert.Equal(TrashState.Deleted, item.Entry.State));
            Assert.DoesNotContain("Требуют проверки", model.TrashHint);
            var restore = trash.CreateRestoreConfirmation()!;
            await restore.ConfirmCommand.ExecuteAsync(null); Assert.True(restore.IsConfirmed);
            await trash.RefreshCommand.ExecuteAsync(null);
            Assert.Equal(unknown ? "1" : "0", model.TrashBadgeText);
            Assert.Equal(unknown, model.HasTrashItems);
        }
        finally { Directory.Delete(root, true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LargeBinHasAFixedBadgeWithAnExactCountInTheTooltip(bool dark)
    {
        var store = new Jornal.Infrastructure.Demo.DemoOrderStore();
        var archive = new MemoryTrashStore(); var source = new TrashSource("mdb", Path.Combine(Path.GetTempPath(), "synthetic-badge.mdb"));
        var model = new MainWindowViewModel(store, trashStore: archive) { IsDarkTheme = dark };
        model.InitializeSource(new(source.Path, store, store, store) { Maintenance = store.Maintenance });
        await model.ReloadCommand.ExecuteAsync(null);
        using (var lease = await archive.OpenAsync(source, TestContext.Current.CancellationToken))
            for (var id = 1; id <= 120; id++)
                await lease.SaveAsync(new(1, Guid.NewGuid(), source, OrderMaintenanceTests.Record() with { Id = id }, DateTimeOffset.UtcNow, TrashState.Deleted));
        await model.CleanupTrashAsync();
        Assert.True(model.HasTrashItems); Assert.Equal("99+", model.TrashBadgeText); Assert.Contains("120", model.TrashHint);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 680 };
        window.Show(); await window.LoadInitialOrdersAsync(); Dispatcher.UIThread.RunJobs();
        try
        {
            var badge = window.FindControl<Border>("TrashBadge")!;
            Assert.True(badge.IsVisible); Assert.InRange(badge.Bounds.Width, 16, 28);
            AssertVisible(window, "TrashButton", "SettingsButton", "UpdatesButton", "ThemeButton");
            Save(window, $"trash-badge-{dark}");
        }
        finally { window.Close(); }
    }

    private sealed class InterruptedMaintenance(IOrderMaintenance inner, int failId, bool unknown) : IOrderMaintenance
    {
        public int Attempts;
        public Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken cancellationToken = default) => inner.ReadRecordAsync(id, cancellationToken);
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default) => inner.ReadAsync(name, cancellationToken);
        public Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken cancellationToken = default) => inner.UpdateAsync(change, cancellationToken);
        public Task<OrderRecord> RestoreAsync(OrderRecord row, CancellationToken cancellationToken = default) => inner.RestoreAsync(row, cancellationToken);
        public async Task DeleteAsync(OrderRecord row, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (row.Id != failId) { await inner.DeleteAsync(row, cancellationToken); return; }
            if (unknown) await inner.DeleteAsync(row, cancellationToken);
            throw new OrderWriteException(unknown ? OrderWriteFailure.OutcomeUnknown : OrderWriteFailure.Conflict, unknown ? "Результат не подтверждён, обновите список" : "Заявка изменена другим пользователем");
        }
    }

    [AvaloniaFact]
    public async Task EditPreservesNullsTimeMetadataAndHistoricalPriceOnFirstSave()
    {
        var backend = new OrderMaintenanceTests.Backend();
        var original = backend.Current!;
        var form = Form(backend);
        form.Customer.Value = "Исправлено 'А'";
        var change = form.BuildChange();
        Assert.Equal(original with { Customer = "Исправлено 'А'" }, change.Values);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.HasError);
        Assert.Equal(original with { Customer = "Исправлено 'А'" }, form.SavedRecord);
        Assert.Equal(1, backend.Updates);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(1, backend.Updates);
    }

    [AvaloniaFact]
    public async Task ChangedTariffRequiresAnotherReviewButDoesNotLoseInput()
    {
        var backend = new OrderMaintenanceTests.Backend(); var form = Form(backend);
        form.Customer.Value = "Правка";
        await form.CalculateCommand.ExecuteAsync(null); backend.Rate = 18;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Null(form.SavedRecord); Assert.Equal(0, backend.Updates); Assert.False(form.RequiresReload);
        Assert.Contains("Прайс изменился", form.Error); Assert.Equal("Правка", form.Customer.Value);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(698m, form.SavedRecord!.Price);
    }

    [AvaloniaFact]
    public async Task ConflictKeepsInputAndRequiresExplicitReviewBeforeRebasing()
    {
        var backend = new OrderMaintenanceTests.Backend(); var form = Form(backend);
        Assert.False(form.HasChanges);
        form.Customer.Value = "Моя правка";
        Assert.Equal(backend.Current!.Customer, Assert.Single(form.Changes).Before);
        Assert.Equal("Моя правка", Assert.Single(form.Changes).After);
        await form.CalculateCommand.ExecuteAsync(null);
        backend.Current = backend.Current! with { Customer = "Чужая правка", Manager = "Другой менеджер" };
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.RequiresReload); Assert.Equal("Моя правка", form.Customer.Value);
        await form.RefreshCommand.ExecuteAsync(null);
        Assert.True(form.HasCurrentVersion); Assert.True(form.RequiresReload);
        Assert.Contains("Чужая правка", form.Comparison); Assert.Contains("Моя правка", form.Comparison);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(0, backend.Updates);
        form.RebaseCommand.Execute(null);
        Assert.Equal("Другой менеджер", form.Manager.Value); Assert.Equal("Моя правка", form.Customer.Value);
        Assert.Equal("Чужая правка", Assert.Single(form.Changes).Before);
        Assert.Equal("Моя правка", Assert.Single(form.Changes).After);
        Assert.False(form.RequiresReload);
        await form.SaveCommand.ExecuteAsync(null); await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Моя правка", backend.Current!.Customer); Assert.Equal("Другой менеджер", backend.Current.Manager);
    }

    [AvaloniaFact]
    public void CoefficientChangesPreserveCountsAndUnknownMetadataAndNeverGuessMissingValues()
    {
        var backend = new OrderMaintenanceTests.Backend(); var form = Form(backend);
        form.Coefficient.Value = "2";
        Assert.Equal("2| 20 | 340 | 2 | N2 ", form.BuildChange().Values.Coefficient);
        backend.Current = backend.Current! with { Coefficient = null };
        form = Form(backend);
        Assert.Equal("", form.Coefficient.Value);
        Assert.Throws<ArgumentException>(() => form.BuildChange());
        Assert.True(form.Coefficient.HasError);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealLocalStoreTrashAndRestoreKeepResultAndRenderAllWindows(bool dark)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-maintenance-window-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        MainWindow? main = null; EditOrderWindow? editWindow = null; OrderConfirmationWindow? confirmWindow = null; TrashWindow? trashWindow = null;
        try
        {
            var path = Path.Combine(root, "OrderDB.journal-test.json");
            await LocalJournalStore.InitializeAsync(path, [], PriceTables.All.Select(name => new PriceTable(name, true, [new(2.5m, "2,5", 17)])).ToArray(),
                "synthetic.mdb", new string('a', 64), TestContext.Current.CancellationToken);
            var store = new LocalJournalStore(path); var original = OrderMaintenanceTests.Record() with { SheetCount = "2" };
            await store.RestoreAsync(original, TestContext.Current.CancellationToken);
            var source = new OrderSource(path, store, store, store, IsLocalCopy: true) { Maintenance = store };
            var model = new MainWindowViewModel(store, store, store, trashStore: new JsonTrashStore(Path.Combine(root, "Trash"))) { IsDarkTheme = dark };
            model.InitializeSource(source);
            main = new MainWindow { DataContext = model, Width = 1366, Height = 768 };
            main.Show(); await main.LoadInitialOrdersAsync(); Dispatcher.UIThread.RunJobs();
            model.UpdateSelection([model.Orders[0]], model.Orders[0]); model.AddOrdersToResult(model.Orders);
            var result = model.ResultText;
            Assert.True(model.CanEditOrder); Assert.True(model.CanTrashOrder);
            var edit = (await model.CreateEditFormAsync(TestContext.Current.CancellationToken))!;
            editWindow = new EditOrderWindow { DataContext = edit, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = 1060, Height = 600 };
            editWindow.Show(); Dispatcher.UIThread.RunJobs();
            Save(editWindow, $"edit-{dark}");
            AssertVisible(editWindow, "SaveButton", "CancelButton", "CalculatePriceButton");
            var fields = editWindow.FindControl<ScrollViewer>("FormScroll")!;
            Assert.True(fields.Extent.Height <= fields.Viewport.Height + 1);
            edit.Customer.Value = "Новый заказчик"; edit.Weight.Value = "45,2"; edit.CuttingTime.Value = "36:47";
            Dispatcher.UIThread.RunJobs(); Assert.Equal(3, edit.Changes.Count);
            AssertVisible(editWindow, "OrderChangesPanel", "SaveButton", "CancelButton", "CalculatePriceButton");
            Save(editWindow, $"edit-changes-{dark}");
            edit.Customer.Value = original.Customer!;
            Assert.DoesNotContain(edit.Changes, change => change.Label == edit.Customer.Label);
            editWindow.Close(); editWindow = null;
            Assert.Equal(result, model.ResultText); Assert.Equal(original, await store.ReadRecordAsync(original.Id, TestContext.Current.CancellationToken));
            var confirm = (await model.CreateTrashFormAsync(TestContext.Current.CancellationToken))!;
            confirmWindow = new OrderConfirmationWindow { DataContext = confirm, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            confirmWindow.Show(); Dispatcher.UIThread.RunJobs(); Save(confirmWindow, $"delete-confirm-{dark}");
            AssertVisible(confirmWindow, "ConfirmButton");
            await confirm.ConfirmCommand.ExecuteAsync(null); confirmWindow = null;
            Assert.True(confirm.IsConfirmed); Assert.Empty(model.Orders); Assert.Equal(result, model.ResultText); Assert.True(model.HasResultError);
            var trash = model.CreateTrashViewModel()!;
            trashWindow = new TrashWindow { DataContext = trash, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Width = 860, Height = 540 };
            trashWindow.Show(); await trash.RefreshCommand.ExecutionTask!; Dispatcher.UIThread.RunJobs();
            Assert.Single(trash.Items); Assert.True(trash.CanAct); Save(trashWindow, $"trash-{dark}");
            AssertVisible(trashWindow, "RestoreButton", "PurgeButton", "ArchivedDetails");
            var restore = trash.CreateRestoreConfirmation()!;
            await restore.ConfirmCommand.ExecuteAsync(null);
            Assert.True(restore.IsConfirmed); Assert.Equal(original, await store.ReadRecordAsync(original.Id, TestContext.Current.CancellationToken));
            Assert.Single(model.Orders); Assert.Equal(result, model.ResultText);
            await trash.RefreshCommand.ExecuteAsync(null); Assert.Empty(trash.Items);
            Save(main, $"main-maintenance-{dark}");
            AssertVisible(main, "EditOrderButton", "TrashOrderButton", "TrashButton");
        }
        finally
        { editWindow?.Close(); confirmWindow?.Close(); trashWindow?.Close(); main?.Close(); Directory.Delete(root, true); }
    }

    private static EditOrderViewModel Form(OrderMaintenanceTests.Backend backend) => new(backend.Current!, backend, "Тестовый источник",
        _ => Task.FromResult(backend.Current));
    private static void AssertVisible(Window window, params string[] names)
    {
        foreach (var name in names)
        {
            var control = window.FindControl<Control>(name)!;
            Assert.True(control.IsEffectivelyVisible, name);
            var bounds = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
            Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= window.ClientSize.Width + 1 && bounds.Bottom <= window.ClientSize.Height + 1, $"{name}: {bounds}");
        }
        Assert.All(window.GetVisualDescendants().OfType<Avalonia.Svg.Svg>().Where(icon => icon.IsEffectivelyVisible), icon => Assert.NotNull(icon.Model));
    }
    private static void Save(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
