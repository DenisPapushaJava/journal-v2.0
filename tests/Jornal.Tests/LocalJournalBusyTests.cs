using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Diagnostics;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class LocalJournalBusyTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldLockReportsBusyWithoutChangingTheFileAndAllowsExplicitRetry(bool write)
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path, TimeSpan.FromMilliseconds(100));
        var original = (await store.ReadRecordAsync(42, Token))!;
        var change = new OrderEditChange(original, original with { Customer = "Изменённый заказчик" });
        var before = SHA256.HashData(await File.ReadAllBytesAsync(fixture.Path, Token));
        using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Exception error;
            if (write)
            {
                var failure = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateAsync(change, Token));
                Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure); Assert.False(failure.RequiresReload);
                error = failure;
            }
            else error = await Assert.ThrowsAsync<OrderReadException>(() => store.ReadRecordAsync(42, Token));
            Assert.True(LocalJournalBusyException.Contains(error));
            Assert.IsType<IOException>(error.InnerException!.InnerException);
            Assert.Contains("занята", error.Message);
            Assert.DoesNotContain("права", error.Message);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(fixture.Path, Token)));
            Assert.Empty(Directory.GetFiles(fixture.Directory, "*.tmp"));
        }
        if (write) Assert.Equal(change.Values.Customer, (await store.UpdateAsync(change, Token)).Customer);
        else Assert.Equal(original, await store.ReadRecordAsync(42, Token));
    }

    [Fact]
    public async Task EditorRetainsInputAndShowsAnActionableMessageBeforeAnExplicitSuccessfulRetry()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path, TimeSpan.FromMilliseconds(100));
        var original = (await store.ReadRecordAsync(42, Token))!;
        var form = new EditOrderViewModel(original, store, fixture.Path, token => store.ReadRecordAsync(42, token));
        try
        {
            await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
            form.Customer.Value = "Изменённый заказчик";
            form.Annotations.Comment = "Правки после отказа";
            var saved = 0; form.Saved += (_, _) => saved++;
            using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await form.SaveCommand.ExecuteAsync(null);
                Assert.Equal("База данных сейчас занята. Правки остались в форме. Повторите сохранение через несколько секунд.", form.Error);
                Assert.Equal("Изменённый заказчик", form.Customer.Value);
                Assert.Equal("Правки после отказа", form.Annotations.Comment);
                Assert.True(form.HasChanges); Assert.True(form.CanSave); Assert.False(form.IsBusy);
                Assert.False(form.RequiresReload); Assert.Null(form.SavedRecord); Assert.Equal(0, saved);
            }
            // Освобождение файла само по себе не должно повторять запись.
            Assert.Equal(original, await store.ReadRecordAsync(42, Token));
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Null(form.Error); Assert.NotNull(form.SavedRecord); Assert.Equal(1, saved);
            var actual = (await store.ReadRecordAsync(42, Token))!;
            Assert.Equal(form.Customer.Value, actual.Customer);
            Assert.Equal(form.Annotations.Comment, actual.Annotations!.Comment);
        }
        finally { form.StopThicknessOptions(); }
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), true)]
    [InlineData(unchecked((int)0x80070021), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(unchecked((int)0x80070002), false)]
    [InlineData(unchecked((int)0x80070070), false)]
    [InlineData(unchecked((int)0x80131620), false)]
    public void OnlySharingAndLockViolationsAreClassifiedAsBusy(int hresult, bool expected) =>
        Assert.Equal(expected, LocalJournalBusyException.IsSharingViolation(new IOException("Тест", hresult)));

    [Fact]
    public async Task NewOrderRetainsItsDraftAfterBusyTimeoutAndIsCreatedOnlyOnceAfterExplicitRetry()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path, TimeSpan.FromMilliseconds(100));
        var form = ManualOrderTests.ValidForm(store);
        try
        {
            await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
            var before = await File.ReadAllBytesAsync(fixture.Path, Token);
            var customer = form.Customer.Value;
            form.Annotations.Comment = "Правки новой заявки";
            using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                await form.SaveCommand.ExecuteAsync(null);
                Assert.True(form.HasSaveError); Assert.Contains("занята", form.SaveError!);
                Assert.Equal(customer, form.Customer.Value); Assert.Equal("Правки новой заявки", form.Annotations.Comment);
                Assert.Null(form.CreatedOrder); Assert.False(form.IsSaved); Assert.False(form.IsBusy);
                Assert.False(form.RequiresReload); Assert.True(form.SaveCommand.CanExecute(null));
                Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
            }
            Assert.Equal(LocalJournalFixture.Orders.Length, (await store.ReadAsync(Token)).Count);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.False(form.HasSaveError, form.SaveError); Assert.NotNull(form.CreatedOrder);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Equal(LocalJournalFixture.Orders.Length + 1, (await store.ReadAsync(Token)).Count);
        }
        finally { form.StopThicknessOptions(); }
    }

    [AvaloniaFact]
    public async Task PriceEditorRetainsDirtyPricesAfterBusyTimeoutAndSavesAfterExplicitRetry()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path, TimeSpan.FromMilliseconds(100));
        var form = new PriceEditorViewModel(store, store, "База", fixture.Path);
        await form.RefreshCommand.ExecuteAsync(null);
        form.BeginEditCommand.Execute(null);
        var row = form.Rows[0]; row.Value = "45";
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await form.SaveCommand.ExecuteAsync(null);
            Assert.True(form.HasError); Assert.Contains("занята", form.Error!);
            Assert.True(form.IsEditing); Assert.True(form.CanSave); Assert.False(form.IsBusy);
            Assert.False(form.RequiresReload); Assert.Equal("45", row.Value);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        }
        Assert.Equal(17, (await store.ReadAsync(form.SelectedProfile.TableName, Token)).Rates[0].RublesPerMeter);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.HasError, form.Error); Assert.False(form.IsEditing);
        Assert.Equal(45, (await store.ReadAsync(form.SelectedProfile.TableName, Token)).Rates[0].RublesPerMeter);
        Assert.Equal(LocalJournalFixture.Orders.OrderBy(value => value.Id), (await store.ReadAsync(Token)).OrderBy(value => value.Id));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ConcurrentEditorsWaitAndEitherSaveDifferentRowsOrDetectAChangedSnapshot(bool sameRow)
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var secondId = sameRow ? 42 : (await store.CreateAsync(LocalJournalFixture.Draft, Token)).Id;
        var forms = new List<EditOrderViewModel>();
        try
        {
            foreach (var id in new[] { 42, secondId })
            {
                var record = (await store.ReadRecordAsync(id, Token))!;
                var form = new EditOrderViewModel(record, store, fixture.Path, token => store.ReadRecordAsync(id, token));
                forms.Add(form);
                await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
                form.Customer.Value = "Правки технолога " + forms.Count;
            }
            using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var pending = forms.Select(form => form.SaveCommand.ExecuteAsync(null)).ToArray();
                try
                {
                    await Task.Delay(100, Token);
                    Assert.All(pending, task => Assert.False(task.IsCompleted));
                }
                finally { gate.Dispose(); await Task.WhenAll(pending).WaitAsync(Token); }
            }
            var saved = forms.Where(form => form.SavedRecord is not null).ToArray();
            Assert.Equal(sameRow ? 1 : 2, saved.Length);
            foreach (var form in saved) Assert.Equal(form.Customer.Value, (await store.ReadRecordAsync(form.Id, Token))!.Customer);
            if (sameRow)
            {
                var conflict = Assert.Single(forms, form => form.SavedRecord is null);
                Assert.True(conflict.RequiresReload); Assert.True(conflict.HasChanges); Assert.False(conflict.CanSave);
                Assert.NotNull(conflict.Error);
            }
        }
        finally { foreach (var form in forms) form.StopThicknessOptions(); }
    }

    [Fact]
    public async Task UnavailableFileDoesNotLoseTheFormAndCanBeSavedAfterTheSourceReturns()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var original = (await store.ReadRecordAsync(42, Token))!;
        var form = new EditOrderViewModel(original, store, fixture.Path, token => store.ReadRecordAsync(42, token));
        var offline = fixture.Path + ".offline";
        try
        {
            await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
            form.Customer.Value = "Правки до восстановления доступа";
            File.Move(fixture.Path, offline);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Contains("недоступна", form.Error!); Assert.Contains(Path.GetFileName(fixture.Path), form.Error!);
            Assert.DoesNotContain("общая папка", form.Error!);
            Assert.Equal("Правки до восстановления доступа", form.Customer.Value);
            Assert.Null(form.SavedRecord); Assert.True(form.CanSave); Assert.False(form.RequiresReload);
            File.Move(offline, fixture.Path);
            Assert.Equal(original, await store.ReadRecordAsync(42, Token));
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Null(form.Error); Assert.NotNull(form.SavedRecord);
        }
        finally { form.StopThicknessOptions(); }
    }

    [Fact]
    public async Task MissingLockDirectoryDoesNotWaitForTheBusyTimeout()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        Directory.Delete(fixture.Directory, recursive: true);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = store.ReadAsync(cancellation.Token);
        try
        {
            var error = await Assert.ThrowsAsync<OrderReadException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3), Token));
            Assert.IsType<DirectoryNotFoundException>(error.InnerException);
            Assert.False(LocalJournalBusyException.Contains(error));
            Assert.Contains("недоступна", error.Message);
            Assert.False(Directory.Exists(fixture.Directory));
        }
        finally
        {
            cancellation.Cancel();
            try { await pending; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task CancellationInterruptsAnAlreadyWaitingReaderWithoutChangingData()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        using var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = store.ReadAsync(cancellation.Token);
        try
        {
            await Task.Delay(100, Token);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3), Token));
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        }
        finally
        {
            cancellation.Cancel();
            try { await pending; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task MissingFileAndCancellationAreNotReportedAsBusy()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync(cancellation.Token));
        }
        File.Delete(fixture.Path);
        var missing = await Assert.ThrowsAsync<OrderReadException>(() => store.ReadAsync(Token));
        Assert.False(LocalJournalBusyException.Contains(missing));
        Assert.Contains(Path.GetFileName(fixture.Path), missing.Message);
        Assert.Contains("недоступна", missing.Message);
    }
}
