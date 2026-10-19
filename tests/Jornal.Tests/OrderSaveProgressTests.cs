using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderSaveProgressTests
{
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task SavingShowsProgressAndContinuesOnceWhenTheOtherClientReleasesTheLock(bool editing, bool dark)
    {
        var token = TestContext.Current.CancellationToken;
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        EditOrderViewModel? edit = null;
        NewOrderViewModel? create = null;
        Window window;
        if (editing)
        {
            var record = (await store.ReadRecordAsync(42, token))!;
            edit = new(record, store, fixture.Path, ct => store.ReadRecordAsync(42, ct));
            await edit.LoadMachinesAsync(token); await edit.RefreshThicknessOptionsAsync(cancellationToken: token);
            edit.Customer.Value = "Правки во время ожидания";
            window = new EditOrderWindow();
        }
        else
        {
            create = ManualOrderTests.ValidForm(store);
            await create.LoadMachinesAsync(token); await create.RefreshThicknessOptionsAsync(cancellationToken: token);
            window = new NewOrderWindow();
        }
        try
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            // Данные готовы до подключения окна; тестирует именно сохранение, без начальной загрузки.
            window.Show(); window.DataContext = (object?)edit ?? create;
            Dispatcher.UIThread.RunJobs();
            var progress = window.FindControl<StackPanel>("SaveProgress")!;
            var button = window.FindControl<JournalButton>("SaveButton")!;
            Assert.False(progress.IsVisible); Assert.True(button.IsEnabled);
            var buttonY = button.Bounds.Y;
            using (var gate = new FileStream(fixture.Path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var pending = editing ? edit!.SaveCommand.ExecuteAsync(null) : create!.SaveCommand.ExecuteAsync(null);
                try
                {
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(progress.IsVisible); Assert.False(button.IsEnabled);
                    Assert.Equal(buttonY, button.Bounds.Y);
                    // Настоящая короткая коллизия двух файловых клиентов, без ожидания таймаута 10 секунд.
                    await Task.Delay(100, token);
                    Assert.False(pending.IsCompleted);
                }
                finally { gate.Dispose(); await pending.WaitAsync(token); }
            }
            Dispatcher.UIThread.RunJobs();
            Assert.False(progress.IsVisible);
            if (editing)
            {
                Assert.Null(edit!.Error); Assert.NotNull(edit.SavedRecord);
                Assert.Equal(edit.Customer.Value, (await store.ReadRecordAsync(42, token))!.Customer);
            }
            else
            {
                Assert.Null(create!.SaveError); Assert.NotNull(create.CreatedOrder);
                Assert.Equal(LocalJournalFixture.Orders.Length + 1, (await store.ReadAsync(token)).Count);
            }
        }
        finally { window.Close(); edit?.StopThicknessOptions(); create?.StopThicknessOptions(); }
    }
}
