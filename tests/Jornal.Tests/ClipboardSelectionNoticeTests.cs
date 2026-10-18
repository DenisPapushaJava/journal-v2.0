using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ClipboardSelectionNoticeTests
{
    [Theory]
    [InlineData("9285", "9286")]
    [InlineData("РП-00012345", "ДРУГОЙ-12345")]
    [InlineData("9285", "09285")]
    [InlineData("00000", "0")]
    [InlineData("РП-12345", "рп-12345")]
    public void DifferentFullDeliveryNumbersBlockBothActionsIncludingMatchingDisplaySuffixes(string first, string second)
    {
        var rows = new[] { JournalClipboardTests.Order(100) with { SpecificationNumber = "Спека-100", DeliveryNumber = first },
            JournalClipboardTests.Order(101) with { SpecificationNumber = "Спека-101", DeliveryNumber = second } };
        var error = Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram(rows));
        Assert.Equal(error.Message, Assert.Throws<ArgumentException>(() => OrderFolderPlan.Create(rows)).Message);
        Assert.Contains("Разные БЛЦБ (2)", error.Message); Assert.Contains(first, error.Message); Assert.Contains(second, error.Message);
        Assert.Contains("Спека-100", error.Message); Assert.Contains("Спека-101", error.Message);
        Assert.Equal(error.Message, Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram(rows.Reverse().ToArray())).Message);
    }

    [Fact]
    public void DeliveryMismatchMessageIsBoundedAndWhitespaceNormalizationDoesNotChangeStoredValues()
    {
        var rows = Enumerable.Range(100, 8).Select(id => JournalClipboardTests.Order(id) with
        { SpecificationNumber = string.Concat(Enumerable.Repeat("😀", 100)), DeliveryNumber = new string('Я', 250) + id }).ToArray();
        var message = Assert.Throws<ArgumentException>(() => OrderCommentFormatter.Telegram(rows)).Message;
        Assert.Contains("Разные БЛЦБ (8)", message); Assert.Contains("ещё БЛЦБ: 5", message);
        Assert.Contains("код 100", message); Assert.True(message.Length < 800);
        _ = new System.Text.UTF8Encoding(false, true).GetBytes(message);
        var first = JournalClipboardTests.Order(); var padded = first with { Id = 101, DeliveryNumber = "  " + first.DeliveryNumber + "\t " };
        Assert.Contains("12345", OrderCommentFormatter.Telegram([first, padded]));
        Assert.Equal(2, OrderFolderPlan.Create([first, padded]).Items.Count);
        Assert.Equal("  " + first.DeliveryNumber + "\t ", padded.DeliveryNumber);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MismatchKeepsClipboardAndLetterUntilSelectionOrDeliveryIsExplicitlyFixed(bool folders)
    {
        var (model, rows) = await MixedModel();
        var oldPlan = OrderFolderPlan.Create([JournalClipboardTests.Order(999)]);
        var clipboard = new RecordingJournalClipboard { Text = "Прежний буфер", Plan = oldPlan };
        model.ResultText = "Письмо";
        async Task Run()
        {
            if (folders) Assert.Null(model.CreateFolderForm(clipboard));
            else await model.CopyCommentAsync(clipboard, true);
        }
        await Run();
        Assert.True(model.HasSelectionNotice); Assert.Contains("Разные БЛЦБ (2)", model.ErrorMessage);
        Assert.Contains("9285", model.ErrorMessage); Assert.Contains("9286", model.ErrorMessage);
        Assert.Equal("Прежний буфер", clipboard.Text); Assert.Same(oldPlan, clipboard.Plan); Assert.Equal("Письмо", model.ResultText);
        var message = model.ErrorMessage;
        model.UpdateSelection(rows.Reverse(), rows[1]); Assert.Equal(message, model.ErrorMessage);
        await model.SetOrderColorCommand.ExecuteAsync(OrderColors.Work);
        Assert.Equal(message, model.ErrorMessage); Assert.True(model.HasSelectionNotice);
        model.UpdateSelection(model.Orders.Where(row => row.DeliveryNumber == "9285"), null);
        Assert.False(model.HasSelectionNotice); Assert.Null(model.ErrorMessage);
        Assert.Equal("Прежний буфер", clipboard.Text); Assert.Same(oldPlan, clipboard.Plan);
        var ids = rows.Select(row => row.Id).ToHashSet();
        model.UpdateSelection(model.Orders.Where(row => ids.Contains(row.Id)), null);
        await Run(); Assert.True(model.HasSelectionNotice);
        var delivery = model.CreateDeliveryNumberForm()!; delivery.Number = "9285";
        await delivery.SaveCommand.ExecuteAsync(null);
        Assert.True(delivery.IsSaved); Assert.False(model.HasSelectionNotice); Assert.Null(model.ErrorMessage);
        Assert.Equal("Прежний буфер", clipboard.Text); Assert.Same(oldPlan, clipboard.Plan);
        if (folders)
        {
            var form = model.CreateFolderForm(clipboard)!; Assert.Equal(3, form.Items.Count);
            await form.CopyCommand.ExecuteAsync(null); Assert.NotSame(oldPlan, clipboard.Plan);
        }
        else { await model.CopyCommentAsync(clipboard, true); Assert.StartsWith("9285", clipboard.Text); }
        Assert.False(model.HasError); Assert.Equal("Письмо", model.ResultText);
    }

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

    internal static async Task<(MainWindowViewModel Model, OrderRowViewModel[] Rows)> MixedModel()
    {
        var (model, rows) = await Model(); var ids = rows.Select(row => row.Id).ToHashSet();
        var common = model.CreateDeliveryNumberForm()!; common.Number = "9285"; await common.SaveCommand.ExecuteAsync(null);
        var last = model.Orders.Single(row => row.Id == rows[^1].Id);
        model.UpdateSelection([last], last);
        var different = model.CreateDeliveryNumberForm()!; different.Number = "9286"; await different.SaveCommand.ExecuteAsync(null);
        var current = model.Orders.Where(row => ids.Contains(row.Id)).OrderBy(row => row.Id).ToArray();
        model.UpdateSelection(current, current[0]);
        return (model, current);
    }
}
