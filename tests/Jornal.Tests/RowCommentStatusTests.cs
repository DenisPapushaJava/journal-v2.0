using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class RowCommentStatusTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(OrderColors.Ready)]
    [InlineData(-25089)]
    [InlineData(OrderColors.Work)]
    public async Task SuccessfulCopyMarksOnlyTheCopiedOrderAndDoesNotRewriteWork(int color)
    {
        var store = new DemoOrderStore(); var before = await store.ReadAsync(Token);
        await store.SetColorAsync([before.Single(row => row.Id == 2)], color, Token);
        var editor = new Editor(store); var model = await BatchOrderTests.Model(store, editor);
        var row = model.Orders.Single(row => row.Id == 2); var original = row.Source;
        model.UpdateSelection([row], row); model.ResultText = "Ручной текст письма";
        var clipboard = new Clipboard();
        await model.CopyCommentAsync(clipboard);
        Assert.Equal(OrderCommentFormatter.Row(original), clipboard.Text);
        Assert.Equal(color == OrderColors.Work ? 0 : 1, editor.Writes);
        Assert.Equal(OrderColors.Work, model.Orders.Single(row => row.Id == 2).Source.Color);
        var saved = await store.ReadAsync(Token);
        Assert.Equal(original with { Color = OrderColors.Work }, saved.Single(row => row.Id == 2));
        Assert.Equal(before.Single(row => row.Id == 1), saved.Single(row => row.Id == 1));
        Assert.Equal(before.Single(row => row.Id == 3), saved.Single(row => row.Id == 3));
        Assert.False(model.HasError); Assert.False(model.IsCopyingSelection); Assert.False(model.IsUpdatingOrders);
        Assert.Contains("Комментарий скопирован", model.UpdatedText); Assert.Contains("В работе", model.UpdatedText);
        Assert.Equal("Ручной текст письма", model.ResultText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidDeliveryOrFailedClipboardNeverStartsAWrite(bool invalidDelivery)
    {
        var store = new DemoOrderStore(); var editor = new Editor(store);
        var model = await BatchOrderTests.Model(store, editor);
        var row = model.Orders.Single(row => row.Id == (invalidDelivery ? 3 : 2));
        model.UpdateSelection([row], row); var before = await store.ReadAsync(Token);
        var clipboard = new Clipboard { BeforeCopy = () => throw new IOException("Буфер недоступен") };
        await model.CopyCommentAsync(clipboard);
        Assert.Null(clipboard.Text); Assert.Equal(0, editor.Writes);
        Assert.Equal(before, await store.ReadAsync(Token)); Assert.True(model.HasError);
        Assert.False(model.IsCopyingSelection); Assert.False(model.IsUpdatingOrders);
    }

    [Theory]
    [InlineData(OrderWriteFailure.NotSaved, false)]
    [InlineData(OrderWriteFailure.Conflict, true)]
    [InlineData(OrderWriteFailure.OutcomeUnknown, true)]
    public async Task WriteFailureKeepsCopiedTextAndExistingRefreshProtection(OrderWriteFailure failure, bool requiresReload)
    {
        var store = new DemoOrderStore();
        var editor = new Editor(store) { Failure = new OrderWriteException(failure, "Проверьте заявку и обновите список (F5).") };
        var model = await BatchOrderTests.Model(store, editor); var row = model.Orders.Single(row => row.Id == 2);
        model.UpdateSelection([row], row); var before = await store.ReadAsync(Token); var clipboard = new Clipboard();
        await model.CopyCommentAsync(clipboard);
        Assert.Equal(OrderCommentFormatter.Row(row.Source), clipboard.Text);
        Assert.Equal(1, editor.Writes); Assert.Equal(before, await store.ReadAsync(Token));
        Assert.Contains("Комментарий скопирован.", model.ErrorMessage); Assert.Contains("F5", model.ErrorMessage);
        Assert.Equal(requiresReload, model.RequiresReload); Assert.False(model.IsCopyingSelection); Assert.False(model.IsUpdatingOrders);
        if (requiresReload)
        {
            await model.CopyCommentAsync(clipboard); Assert.Equal(1, editor.Writes);
            await model.ReloadCommand.ExecuteAsync(null); Assert.False(model.RequiresReload);
        }
    }

    [Fact]
    public async Task SelectionChangeDuringClipboardWaitDoesNotRedirectTheStatusWrite()
    {
        var store = new DemoOrderStore(); var editor = new Editor(store);
        var model = await BatchOrderTests.Model(store, editor);
        var row = model.Orders.Single(row => row.Id == 2); var other = model.Orders.Single(row => row.Id == 3);
        model.UpdateSelection([row], row);
        var clipboard = new Clipboard { BeforeCopy = () =>
        {
            Assert.True(model.IsCopyingSelection); Assert.False(model.CanCopyRowComment);
            model.UpdateSelection([other], other); return Task.CompletedTask;
        } };
        await model.CopyCommentAsync(clipboard);
        Assert.Equal(OrderCommentFormatter.Row(row.Source), clipboard.Text);
        Assert.Equal(2, Assert.Single(editor.Expected!).Id);
        Assert.Equal(OrderColors.None, (await store.ReadAsync(Token)).Single(order => order.Id == 3).Color);
    }

    [Fact]
    public async Task ColleaguesColorChangeIsNotOverwrittenAfterCopy()
    {
        var store = new DemoOrderStore(); var editor = new Editor(store);
        var model = await BatchOrderTests.Model(store, editor); var row = model.Orders.Single(row => row.Id == 2);
        model.UpdateSelection([row], row);
        var clipboard = new Clipboard { BeforeCopy = async () =>
            await store.SetColorAsync([row.Source], OrderColors.Shipped, Token) };
        await model.CopyCommentAsync(clipboard);
        Assert.NotNull(clipboard.Text); Assert.True(model.RequiresReload);
        Assert.Equal(OrderColors.Shipped, (await store.ReadAsync(Token)).Single(order => order.Id == 2).Color);
        Assert.Contains("Комментарий скопирован.", model.ErrorMessage);
    }

    [Fact]
    public async Task CuttingTaskAndMultipleSelectionKeepTheirExistingBehavior()
    {
        var store = new DemoOrderStore(); var editor = new Editor(store);
        var model = await BatchOrderTests.Model(store, editor); var before = await store.ReadAsync(Token);
        var row = model.Orders.Single(row => row.Id == 2); model.UpdateSelection([row], row);
        var clipboard = new Clipboard(); await model.CopyCommentAsync(clipboard, telegram: true);
        Assert.NotNull(clipboard.Text); Assert.Equal(0, editor.Writes);
        model.UpdateSelection(model.Orders, row); Assert.False(model.CanCopyRowComment);
        clipboard.Text = null; await model.CopyCommentAsync(clipboard);
        Assert.Null(clipboard.Text); Assert.Equal(0, editor.Writes); Assert.Equal(before, await store.ReadAsync(Token));
    }

    private sealed class Clipboard : IJournalClipboard
    {
        public int MaximumFolderNameLength => 140;
        public string? Text;
        public Func<Task>? BeforeCopy;
        public async Task WriteTextAsync(string text) { if (BeforeCopy is not null) await BeforeCopy(); Text = text; }
        public Task WriteFoldersAsync(OrderFolderPlan plan) => throw new NotSupportedException();
    }

    private sealed class Editor(DemoOrderStore store) : IOrderBatchEditor
    {
        public int Writes;
        public Exception? Failure;
        public IReadOnlyCollection<OrderSummary>? Expected;
        public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color, CancellationToken cancellationToken = default)
        {
            Writes++; Expected = expected;
            return Failure is { } failure ? Task.FromException<IReadOnlyList<OrderSummary>>(failure) : store.SetColorAsync(expected, color, cancellationToken);
        }
        public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number, CancellationToken cancellationToken = default) =>
            store.SetDeliveryNumberAsync(expected, number, cancellationToken);
    }
}
