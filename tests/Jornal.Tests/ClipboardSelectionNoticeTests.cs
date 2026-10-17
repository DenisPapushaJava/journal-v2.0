using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ClipboardSelectionNoticeTests
{
    [Fact]
    public void MissingDeliveryMessageNamesEveryMissingOrderAndBoundsLargeOrUnusualSets()
    {
        var rows = Enumerable.Range(100, 8).Select(id => JournalClipboardTests.Order(id) with { SpecificationNumber = "Спека-" + id, DeliveryNumber = null }).ToArray();
        var error = Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram([rows[0], JournalClipboardTests.Order(999), rows[1], rows[2]]));
        Assert.Equal("Укажите БЛЦБ для заявок (3): Спека-100, Спека-101, Спека-102.", error.Message);
        Assert.DoesNotContain("999", error.Message);
        var many = Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram(rows));
        Assert.Contains("(8)", many.Message); Assert.Contains("Спека-104, ещё 3", many.Message); Assert.DoesNotContain("Спека-105", many.Message);
        var unusual = rows.Select(row => row with { SpecificationNumber = string.Concat(Enumerable.Repeat("😀", 100)) }).ToArray();
        var longMessage = Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram(unusual)).Message;
        Assert.True(longMessage.Length < 600); Assert.Contains("код 100", longMessage);
        _ = new System.Text.UTF8Encoding(false, true).GetBytes(longMessage);
        Assert.Contains("код 100", Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Row(rows[0] with { SpecificationNumber = "\r\n" })).Message);
        Assert.Contains("(8)", Assert.Throws<ArgumentException>(() => OrderFolderPlan.Create(rows)).Message);
    }

    [Fact]
    public async Task ValidationKeepsTheClipboardAndLetterAndOnlyChangesWithTheSelection()
    {
        var (model, rows) = await Model();
        var clipboard = new RecordingJournalClipboard { Text = "Прежний буфер" };
        model.ResultText = "Письмо";
        await model.CopyCommentAsync(clipboard, true);
        Assert.True(model.HasSelectionNotice); Assert.Contains("(2)", model.ErrorMessage);
        Assert.Contains(rows[0].SpecificationNumber, model.ErrorMessage); Assert.Contains(rows[1].SpecificationNumber, model.ErrorMessage);
        Assert.DoesNotContain(rows[2].SpecificationNumber, model.ErrorMessage);
        Assert.Equal("Прежний буфер", clipboard.Text); Assert.Equal("Письмо", model.ResultText);
        var message = model.ErrorMessage;
        model.UpdateSelection(rows.Reverse(), rows[1]); // Порядок/активная строка — тот же набор.
        Assert.Equal(message, model.ErrorMessage);
        model.UpdateSelection([rows[0]], rows[0]); Assert.False(model.HasError); Assert.False(model.HasSelectionNotice);
        await model.CopyCommentAsync(clipboard, true); Assert.True(model.HasSelectionNotice);
        model.UpdateSelection([], null); Assert.False(model.HasError);
        model.UpdateSelection(rows, rows[0]); await model.CopyCommentAsync(clipboard, true);
        model.SearchText = "не найдётся"; Assert.False(model.HasError); Assert.False(model.HasSelectionNotice);
    }

    [Fact]
    public async Task DismissingOrChangingSelectionNeverDismissesAStorageOrClipboardFailure()
    {
        var (model, rows) = await Model(); var clipboard = new RecordingJournalClipboard();
        await model.CopyCommentAsync(clipboard, true);
        model.DismissSelectionNoticeCommand.Execute(null); Assert.False(model.HasError);
        await model.CopyCommentAsync(clipboard, true); Assert.True(model.HasSelectionNotice);
        model.ErrorMessage = "Ошибка записи базы";
        Assert.False(model.HasSelectionNotice);
        model.UpdateSelection([], null); model.DismissSelectionNoticeCommand.Execute(null);
        Assert.Equal("Ошибка записи базы", model.ErrorMessage);
        model.UpdateSelection([rows[2]], rows[2]); clipboard.Failure = true;
        await model.CopyCommentAsync(clipboard, true); Assert.True(model.HasError); Assert.False(model.HasSelectionNotice);
        var error = model.ErrorMessage;
        model.UpdateSelection([], null); Assert.Equal(error, model.ErrorMessage);
    }

    [Fact]
    public async Task StatusChangesKeepNoticeAndSuccessfulDeliveryAssignmentDismissesIt()
    {
        var (model, rows) = await Model(); var clipboard = new RecordingJournalClipboard();
        await model.CopyCommentAsync(clipboard, true);
        var message = model.ErrorMessage;
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Work);
        Assert.True(model.HasSelectionNotice); Assert.Equal(message, model.ErrorMessage);
        var delivery = model.CreateDeliveryNumberForm()!;
        delivery.Number = "9285";
        await delivery.SaveCommand.ExecuteAsync(null);
        Assert.True(delivery.IsSaved); Assert.False(model.HasError); Assert.False(model.HasSelectionNotice);
        Assert.Equal($"Выбрано: {rows.Length}", model.SelectionText);
        await model.CopyCommentAsync(clipboard, true);
        Assert.Contains("9285", clipboard.Text); Assert.False(model.HasError);
    }

    internal static async Task<(MainWindowViewModel Model, OrderRowViewModel[] Rows)> Model()
    {
        var store = new DemoOrderStore(); var created = new List<OrderSummary>();
        for (var i = 0; i < 3; i++)
            created.Add(await store.CreateAsync(LocalJournalFixture.Draft with { SpecificationNumber = "Спека-" + (100 + i) }, TestContext.Current.CancellationToken));
        await store.SetDeliveryNumberAsync([created[2]], "12345", TestContext.Current.CancellationToken);
        var model = new MainWindowViewModel(store, store, store);
        await model.ReloadCommand.ExecuteAsync(null);
        var ids = created.Select(row => row.Id).ToHashSet();
        var rows = model.Orders.Where(row => ids.Contains(row.Id)).OrderBy(row => row.Id).ToArray();
        model.UpdateSelection(rows, rows[0]);
        return (model, rows);
    }
}
