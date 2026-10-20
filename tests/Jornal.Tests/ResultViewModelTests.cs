using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultViewModelTests
{
    [Fact]
    public async Task EachBuildReplacesPreviousTextAndSortsTheCurrentSelectionWithoutChangingTableOrder()
    {
        var model = await Model();
        var rows = model.Orders.ToArray();
        var originalIds = rows.Select(row => row.Id).ToArray();
        model.ResultText = "Примечание заказчику";

        model.AddOrdersToResult(rows.Reverse());
        var lines = model.ResultText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.DoesNotContain("Примечание заказчику", model.ResultText);
        Assert.Contains("Лист 2,5x1250x2500,", lines[0]);
        Assert.Contains("6x1500x3000,", lines[1]);
        Assert.Contains("16x1500x6000,", lines[2]);
        Assert.Equal(originalIds, model.Orders.Select(row => row.Id));

        model.AcceptCreatedOrder(ResultOutputTests.Order() with { Id = 4, ThicknessMillimeters = 1 });
        model.AddOrdersToResult([model.Orders.Single(row => row.Id == 4)]);
        Assert.Single(model.ResultText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("Лист 1x1250x2500,", model.ResultText);
        Assert.DoesNotContain("6x1500x3000,", model.ResultText);

        model.AddOrdersToResult(model.Orders.Reverse());
        lines = model.ResultText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        Assert.Contains("1x1250x2500,", lines[0]);
        Assert.Contains("2,5x1250x2500,", lines[1]);
        Assert.Contains("6x1500x3000,", lines[2]);
        Assert.Contains("16x1500x6000,", lines[3]);

        var clipboard = new RecordingClipboard();
        await model.CopyResultAsync(clipboard);
        Assert.Equal(model.ResultText, clipboard.Content!.Text);
    }

    [Fact]
    public async Task RebuildingTheSameSelectionAfterReloadDoesNotDuplicateRowsAndClearKeepsSelection()
    {
        var model = await Model();
        var rows = model.Orders.ToArray();
        model.UpdateSelection(rows, rows[0]);
        Assert.True(model.CanAddResult);
        model.AddOrdersToResult(rows.Concat(rows));
        var original = model.ResultText;
        Assert.Equal(3, original.Count(character => character == '\n'));
        model.AddOrdersToResult(rows);
        Assert.Equal(original, model.ResultText);
        await model.ReloadCommand.ExecuteAsync(null);
        model.AddOrdersToResult(model.Orders);
        Assert.Equal(original, model.ResultText);
        model.UpdateSelection(model.Orders, model.Orders[0]);
        model.ClearResultCommand.Execute(null);
        Assert.Empty(model.ResultText);
        Assert.Equal("Выбрано: 3", model.SelectionText);
        model.AddOrdersToResult(model.Orders);
        Assert.Equal(original, model.ResultText);
    }

    [Fact]
    public async Task SelectionWithUncalculatedPriceLeavesThePreviousResultUntouched()
    {
        var model = await Model();
        model.AddOrdersToResult([model.Orders[0]]);
        var original = model.ResultText;
        model.AcceptCreatedOrder(ResultOutputTests.Order() with { Id = 4, StoredCuttingPrice = null });
        var missing = model.Orders.Single(row => row.Id == 4);
        var known = model.Orders.Single(row => row.Id == 2);
        model.AddOrdersToResult([missing, known]);
        Assert.True(model.HasResultError);
        Assert.Contains(missing.SpecificationNumber, model.ResultMessage);
        Assert.Equal(original, model.ResultText);
        model.AddOrdersToResult([known]);
        Assert.False(model.HasResultError);
        Assert.Single(model.ResultText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("5083H111", model.ResultText);
    }

    [Fact]
    public async Task ReplacementKeepsOnlyCurrentOrderIdsAndEmptySelectionPreservesThePreparedResult()
    {
        var store = new DemoOrderStore();
        var model = new MainWindowViewModel(store);
        model.InitializeSource(new(Path.Combine(Path.GetTempPath(), "synthetic-result.mdb"), store));
        await model.ReloadCommand.ExecuteAsync(null);
        var rows = model.Orders.ToArray();
        model.AddOrdersToResult(rows);
        Assert.Equal(3, model.CaptureUpdateDraft().ResultOrderIds.Length);

        model.AddOrdersToResult([rows[1]]);
        Assert.Equal(rows[1].Id, Assert.Single(model.CaptureUpdateDraft().ResultOrderIds));
        var text = model.ResultText;
        model.AddOrdersToResult([]);
        Assert.Equal(text, model.ResultText);
        Assert.Equal(rows[1].Id, Assert.Single(model.CaptureUpdateDraft().ResultOrderIds));
        model.ClearResultCommand.Execute(null);
        Assert.Empty(model.CaptureUpdateDraft().ResultOrderIds);
    }

    [Fact]
    public async Task ManualEditsAreCopiedAsShownAndReplacedOnlyByExplicitRebuilding()
    {
        var model = await Model();
        model.AddOrdersToResult([model.Orders[0]]);
        model.ResultText = "Примечание заказчику\n" + model.ResultText.TrimEnd();
        model.SearchText = "нет совпадений";
        Assert.False(model.CanAddResult);
        Assert.True(model.CanCopyResult);
        var clipboard = new RecordingClipboard();
        await model.CopyResultAsync(clipboard);
        Assert.Equal(model.ResultText, clipboard.Content!.Text);
        Assert.Contains("Примечание заказчику", clipboard.Content.Html);
        model.ResetFiltersCommand.Execute(null);
        model.AddOrdersToResult([model.Orders[1]]);
        Assert.Single(model.ResultText.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain("Примечание заказчику", model.ResultText);
        Assert.Contains("5083H111", model.ResultText);
    }

    [Fact]
    public async Task ClipboardFailureIsRetryableAndEmptyResultDoesNotWrite()
    {
        var model = await Model();
        var clipboard = new RecordingClipboard { Fail = true };
        await model.CopyResultAsync(clipboard);
        Assert.Equal(0, clipboard.Calls);
        model.AddOrdersToResult([model.Orders[0]]);
        var original = model.ResultText;
        await model.CopyResultAsync(clipboard);
        Assert.True(model.HasResultError);
        Assert.Equal(original, model.ResultText);
        Assert.True(model.CanCopyResult);
        model.AddOrdersToResult([model.Orders[0]]);
        Assert.Equal(original, model.ResultText);
        Assert.False(model.HasResultError);
        clipboard.Fail = false;
        await model.CopyResultAsync(clipboard);
        Assert.False(model.HasResultError);
        Assert.Contains("Скопировано", model.ResultMessage);
        Assert.Equal(2, clipboard.Calls);
    }

    [Fact]
    public async Task CopyAndCustomizationBlockResultActionsWithoutChangingTheText()
    {
        var model = await Model();
        model.UpdateSelection(model.Orders, model.Orders[0]);
        model.AddOrdersToResult([model.Orders[0]]);
        var original = model.ResultText;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clipboard = new RecordingClipboard { Wait = completion.Task };
        var copy = model.CopyResultAsync(clipboard);
        Assert.True(model.IsCopyingResult);
        Assert.False(model.CanCopyResult);
        Assert.False(model.CanClearResult);
        Assert.False(model.CanAddResult);
        model.ClearResultCommand.Execute(null);
        model.AddOrdersToResult([model.Orders[1]]);
        await model.CopyResultAsync(clipboard);
        Assert.Equal(original, model.ResultText);
        Assert.Equal(1, clipboard.Calls);
        completion.SetResult();
        await copy;
        model.IsCustomizing = true;
        Assert.False(model.CanEditResult);
        model.ClearResultCommand.Execute(null);
        Assert.Equal(original, model.ResultText);
        model.IsCustomizing = false;
        Assert.True(model.CanCopyResult);
    }

    private static async Task<MainWindowViewModel> Model()
    {
        var model = new MainWindowViewModel(new DemoOrderStore());
        await model.ReloadCommand.ExecuteAsync(null);
        return model;
    }

    private sealed class RecordingClipboard : IResultClipboard
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task? Wait { get; init; }
        public ResultClipboardContent? Content { get; private set; }
        public async Task WriteAsync(ResultClipboardContent content)
        {
            Calls++;
            if (Fail) throw new IOException("Проверка отказа буфера обмена");
            Content = content;
            if (Wait is not null) await Wait;
        }
    }
}
