using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Trash;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class TrashClearTests
{
    private static readonly TrashSource Source = new("mdb", Path.Combine(Path.GetTempPath(), "trash-clear-source.mdb"));
    private static TrashEntry Entry(int id, TrashState state = TrashState.Deleted) => new(1, Guid.NewGuid(), Source,
        OrderMaintenanceTests.Record() with { Id = id, Specification = $"1018-{id}", Customer = "Архивный заказчик" }, DateTimeOffset.UtcNow, state);
    private static async Task Save(ITrashStore store, params TrashEntry[] entries)
    {
        foreach (var group in entries.GroupBy(entry => entry.Source))
        {
            using var lease = await store.OpenAsync(group.Key, TestContext.Current.CancellationToken);
            foreach (var entry in group) await lease.SaveAsync(entry);
        }
    }

    [Fact]
    public async Task ClearRemovesOnlyConfirmedSnapshotWithoutTouchingDatabaseOtherSourcesOrNewCopies()
    {
        var root = Path.Combine(Path.GetTempPath(), "trash-clear-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonTrashStore(root); var backend = new OrderMaintenanceTests.Backend();
            var first = Entry(1); var second = Entry(2); var pending = Entry(42, TrashState.DeletePending);
            var restorePending = Entry(42, TrashState.RestorePending);
            var other = Entry(5) with { Source = Source with { Path = Path.Combine(Path.GetTempPath(), "other-trash-clear.mdb") } };
            await Save(store, first, second, pending, restorePending, other);
            var service = new OrderTrashService(backend, Source, store);
            var addedLater = Entry(6); await Save(store, addedLater);
            var result = await service.PurgeManyAsync([first, second], TestContext.Current.CancellationToken);
            Assert.Null(result.Error); Assert.Equal([first.OperationId, second.OperationId], result.RemovedIds);
            Assert.Equal(new[] { pending.OperationId, restorePending.OperationId, addedLater.OperationId }.Order(), result.Remaining!.Select(entry => entry.OperationId).Order());
            using var otherLease = await store.OpenAsync(other.Source, TestContext.Current.CancellationToken);
            Assert.Equal(other, Assert.Single(await otherLease.ReadAsync()));
            Assert.Equal(0, backend.Deletes); Assert.Equal(0, backend.Restores); Assert.Equal(0, backend.Updates);
            Assert.Equal(OrderMaintenanceTests.Record(), backend.Current);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ChangedSnapshotIsCheckedForTheWholeGroupBeforeRemovingAnything()
    {
        var store = new MemoryTrashStore(); var service = new OrderTrashService(new OrderMaintenanceTests.Backend(), Source, store);
        var first = Entry(1); var second = Entry(2); await Save(store, first, second);
        await Save(store, second with { State = TrashState.RestorePending });
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => service.PurgeManyAsync([first, second], TestContext.Current.CancellationToken));
        Assert.Contains("Корзина изменилась", error.Message);
        using var lease = await store.OpenAsync(Source, TestContext.Current.CancellationToken);
        var remaining = await lease.ReadAsync(); Assert.Equal(2, remaining.Count); Assert.Contains(first, remaining);
    }

    [Fact]
    public async Task PendingCopiesAndForeignSourcesCannotBePassedToBulkPurge()
    {
        var store = new MemoryTrashStore(); var service = new OrderTrashService(new OrderMaintenanceTests.Backend(), Source, store);
        var confirmed = Entry(1); var pending = Entry(42, TrashState.DeletePending); await Save(store, confirmed, pending);
        await Assert.ThrowsAsync<ArgumentException>(() => service.PurgeManyAsync([confirmed, pending], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PurgeManyAsync([confirmed, confirmed], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PurgeManyAsync([confirmed with { Source = Source with { Kind = "local" } }], TestContext.Current.CancellationToken));
        using var lease = await store.OpenAsync(Source, TestContext.Current.CancellationToken);
        Assert.Equal(2, (await lease.ReadAsync()).Count);
    }

    [Fact]
    public async Task DiskFailureStopsWithoutRepeatingAndReportsTheConfirmedPartialResult()
    {
        var store = new FaultStore(new MemoryTrashStore()) { FailAt = 2 };
        var entries = new[] { Entry(1), Entry(2), Entry(3) }; await Save(store, entries);
        var result = await new OrderTrashService(new OrderMaintenanceTests.Backend(), Source, store)
            .PurgeManyAsync(entries, TestContext.Current.CancellationToken);
        Assert.IsType<IOException>(result.Error); Assert.Equal([entries[0].OperationId], result.RemovedIds);
        Assert.Equal(2, result.Remaining!.Count); Assert.Equal(2, store.RemoveCalls);
    }

    [Fact]
    public async Task CorruptArchivePreventsAnyBulkRemoval()
    {
        var root = Path.Combine(Path.GetTempPath(), "trash-clear-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonTrashStore(root); var entry = Entry(1); await Save(store, entry);
            var file = Assert.Single(Directory.GetFiles(root, "*.journal-trash.json", SearchOption.AllDirectories));
            var before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(file)!, Guid.NewGuid().ToString("N") + ".journal-trash.json"), "broken", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => new OrderTrashService(new OrderMaintenanceTests.Backend(), Source, store)
                .PurgeManyAsync([entry], TestContext.Current.CancellationToken));
            Assert.Equal(before, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public async Task PartialClearRequiresRefreshAndConfirmationCannotBeRepeated()
    {
        var store = new FaultStore(new MemoryTrashStore()) { FailAt = 2 };
        await Save(store, Entry(1), Entry(2), Entry(3));
        var model = Model(store);
        await model.RefreshCommand.ExecuteAsync(null);
        var form = model.CreateClearConfirmation()!;
        Assert.Equal(3, model.Items.Count); Assert.False(model.NeedsRefresh);
        await form.ConfirmCommand.ExecuteAsync(null);
        Assert.True(form.HasOutcome); Assert.Contains("Удалено копий: 1 из 3", form.Outcome);
        Assert.False(form.CanConfirm); Assert.True(model.NeedsRefresh); Assert.False(model.CanClear); Assert.False(model.CanAct);
        await form.ConfirmCommand.ExecuteAsync(null); Assert.Equal(2, store.RemoveCalls);
        Assert.Equal(2, model.Items.Count);
        await model.RefreshCommand.ExecuteAsync(null); Assert.False(model.NeedsRefresh); Assert.True(model.CanClear);
    }

    [AvaloniaFact]
    public async Task ArchiveFailureDoesNotInvalidateJournalDataAndEmptyBinDisablesClear()
    {
        var archive = new FaultStore(new MemoryTrashStore()) { FailAt = 2 };
        await Save(archive, Entry(1), Entry(2));
        var store = new DemoOrderStore(); var journal = new MainWindowViewModel(store, store, store, trashStore: archive);
        journal.InitializeSource(new(Source.Path, store, store, store) { Maintenance = store.Maintenance });
        await journal.ReloadCommand.ExecuteAsync(null); var rows = journal.Orders.Select(row => row.Id).ToArray();
        var model = journal.CreateTrashViewModel()!; await model.RefreshCommand.ExecuteAsync(null);
        await model.CreateClearConfirmation()!.ConfirmCommand.ExecuteAsync(null);
        Assert.False(journal.RequiresReload); Assert.False(journal.HasError); Assert.Equal(rows, journal.Orders.Select(row => row.Id));
        Assert.Equal("1", journal.TrashBadgeText);
        await model.RefreshCommand.ExecuteAsync(null);
        await model.CreateClearConfirmation()!.ConfirmCommand.ExecuteAsync(null);
        Assert.Empty(model.Items); Assert.Null(model.Selected); Assert.False(model.CanClear); Assert.False(journal.HasTrashItems);
    }

    [AvaloniaFact]
    public async Task UnreadableRemainderKeepsLastKnownBadgeAndRequiresSuccessfulRefresh()
    {
        var store = new FaultStore(new MemoryTrashStore()) { FailReadAfterRemoval = true };
        await Save(store, Entry(1), Entry(2));
        IReadOnlyList<TrashEntry> badge = [];
        var model = Model(store, entries => badge = entries);
        await model.RefreshCommand.ExecuteAsync(null); Assert.Equal(2, badge.Count);
        var form = model.CreateClearConfirmation()!; await form.ConfirmCommand.ExecuteAsync(null);
        Assert.Contains("Удалено копий: 2 из 2", form.Outcome); Assert.Empty(model.Items);
        Assert.Equal(2, badge.Count); Assert.True(model.NeedsRefresh); Assert.False(form.CanConfirm);
        await model.RefreshCommand.ExecuteAsync(null); Assert.True(model.NeedsRefresh); Assert.True(model.HasError);
        store.FailReadAfterRemoval = false;
        await model.RefreshCommand.ExecuteAsync(null); Assert.False(model.NeedsRefresh); Assert.Empty(badge);
    }

    [AvaloniaTheory]
    [InlineData(false, 1)] [InlineData(true, 1)] [InlineData(false, 1.5)] [InlineData(true, 1.5)]
    public async Task ClearButtonFitsMinimumWindowAndRequiresExplicitConfirmation(bool dark, double scale)
    {
        var store = new MemoryTrashStore(); await Save(store, Entry(1), Entry(2), Entry(42, TrashState.DeletePending));
        IReadOnlyList<TrashEntry> badge = [];
        var model = Model(store, entries => badge = entries);
        var window = new TrashWindow { DataContext = model, Width = 860, Height = 540, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.SetRenderScaling(scale);
        try
        {
            window.Show(); await model.RefreshCommand.ExecutionTask!; Pump();
            var clear = window.FindControl<JournalButton>("ClearTrashButton")!;
            Assert.True(clear.IsEffectivelyEnabled); InBounds(window, clear);
            InBounds(window, window.FindControl<JournalButton>("RestoreButton")!);
            InBounds(window, window.FindControl<JournalButton>("PurgeButton")!);
            Click(window, clear);
            var confirmWindow = Assert.Single(window.OwnedWindows.OfType<OrderConfirmationWindow>());
            var form = Assert.IsType<OrderConfirmationViewModel>(confirmWindow.DataContext);
            Assert.Equal("Архивных копий: 2", form.Details); Assert.Contains("Требуют проверки: 1", form.Explanation);
            Assert.Contains("Тестовая база", form.Explanation); Assert.True(form.IsDestructive);
            InBounds(confirmWindow, confirmWindow.FindControl<JournalButton>("ConfirmButton")!);
            SaveWindow(window, $"trash-clear-{dark}-{scale}"); SaveWindow(confirmWindow, $"trash-clear-confirm-{dark}-{scale}");
            confirmWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Pump();
            Assert.False(confirmWindow.IsVisible); Assert.Equal(3, model.Items.Count);
            Click(window, clear); confirmWindow = Assert.Single(window.OwnedWindows.OfType<OrderConfirmationWindow>());
            form = (OrderConfirmationViewModel)confirmWindow.DataContext!;
            await form.ConfirmCommand.ExecuteAsync(null); Pump();
            if (model.RefreshCommand.ExecutionTask is { } refresh) await refresh; Pump();
            Assert.True(form.IsConfirmed); Assert.False(confirmWindow.IsVisible);
            Assert.Equal(TrashState.DeletePending, Assert.Single(model.Items).Entry.State);
            Assert.Single(badge); Assert.False(model.CanClear); Assert.Null(model.CreateClearConfirmation());
            var pending = Assert.Single(await new OrderTrashService(new OrderMaintenanceTests.Backend(), Source, store).RefreshAsync(false, TestContext.Current.CancellationToken));
            Assert.Equal(TrashState.DeletePending, pending.State);
        }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); }
    }

    private static TrashViewModel Model(ITrashStore store, Action<IReadOnlyList<TrashEntry>>? changed = null) =>
        new(new(new OrderMaintenanceTests.Backend(), Source, store), "Тестовая база",
            (_, _) => throw new InvalidOperationException("Очистка не должна менять заявки."), _ => Task.CompletedTask,
            action => action(), _ => { }, changed);
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Click(TopLevel root, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    }
    private static void InBounds(TopLevel root, Control control)
    {
        var point = control.TranslatePoint(default, root)!.Value;
        Assert.InRange(point.X, 0, root.ClientSize.Width - control.Bounds.Width + 1);
        Assert.InRange(point.Y, 0, root.ClientSize.Height - control.Bounds.Height + 1);
    }
    private static void SaveWindow(TopLevel window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
    private sealed class FaultStore(ITrashStore inner) : ITrashStore
    {
        public int FailAt; public int RemoveCalls; public bool FailReadAfterRemoval;
        public async Task<ITrashLease> OpenAsync(TrashSource source, CancellationToken cancellationToken = default) => new Lease(this, await inner.OpenAsync(source, cancellationToken));
        private sealed class Lease(FaultStore owner, ITrashLease inner) : ITrashLease
        {
            public Task<IReadOnlyList<TrashEntry>> ReadAsync() => owner.FailReadAfterRemoval && owner.RemoveCalls > 0
                ? Task.FromException<IReadOnlyList<TrashEntry>>(new IOException("Остаток корзины недоступен")) : inner.ReadAsync();
            public Task SaveAsync(TrashEntry entry) => inner.SaveAsync(entry);
            public Task RemoveAsync(Guid id)
            {
                if (++owner.RemoveCalls == owner.FailAt) throw new IOException("Диск недоступен");
                return inner.RemoveAsync(id);
            }
            public void Dispose() => inner.Dispose();
        }
    }
}
