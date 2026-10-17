using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Documents;
using Xunit;
namespace Jornal.Tests;
public sealed class BatchImportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static async Task<SpecificationImportPreview> Preview()
    {
        using var sample = new SpecificationImportTests.Sample();
        return await new SpecificationImportService().ReadAsync(sample.Path, Token);
    }
    internal sealed class Reader(SpecificationImportPreview preview) : ISpecificationImportService
    {
        public int Calls;
        public Task<SpecificationImportPreview> ReadAsync(string path, CancellationToken cancellationToken)
        {
            Calls++;
            return path.Contains("foreign", StringComparison.Ordinal) ? throw new DocumentRejectedException(DocumentRejection.UnrecognizedSpecification, "Формат спецификации не распознан.") : Task.FromResult(preview with { FileName = Path.GetFileName(path) });
        }
    }
    [Fact]
    public async Task SingleImportAndManualSaveRetainTheOriginalFlow()
    {
        var form = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = new Reader(await Preview()) };
        await form.ImportFilesAsync(["one.html"], Token);
        Assert.False(form.IsBatchMode); Assert.True(form.HasImport); Assert.Empty(form.BatchItems);
        form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Single(form.CreatedOrders); Assert.Equal(form.CreatedOrder, form.CreatedOrders[0]);
    }
    [Fact]
    public async Task MixedFilesRemainVisibleAndMustBeExplicitlyExcludedBeforeSaving()
    {
        var store = new DemoOrderStore(); var form = new NewOrderViewModel(store) { ImportReader = new Reader(await Preview()) };
        await form.ImportFilesAsync(["one.html", "foreign.pdf", "notes.txt", "two.html"], Token);
        Assert.True(form.IsBatchMode); Assert.Equal(4, form.BatchItems.Count);
        Assert.True(form.BatchItems[0].IsReady); Assert.True(form.BatchItems[3].IsReady);
        Assert.All(form.BatchItems.Skip(1).Take(2), item => { Assert.True(item.HasFailure); Assert.False(item.HasParameters); });
        Assert.False(form.SaveCommand.CanExecute(null));
        form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
        form.BatchItems[1].IsIncluded = false; form.BatchItems[2].IsIncluded = false;
        form.BatchItems[3].Form.SheetCount.Value = "5";
        Assert.True(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(2, form.CreatedOrders.Count);
        Assert.All(form.CreatedOrders, row => { Assert.Equal("Иван", row.ManagerName); Assert.Equal("Клиент", row.CustomerName); Assert.Equal("6", row.ThicknessMillimeters!.Value.ToString()); });
        Assert.Equal(new[] { "3", "5" }, form.CreatedOrders.Select(row => row.SheetCount));
        Assert.Equal(5, (await store.ReadAsync(Token)).Count);
    }
    [Fact]
    public async Task PartialSpecificationCanBeRepairedAndDuplicateWarningsFollowEdits()
    {
        var preview = await Preview(); var fields = preview.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields[nameof(NewOrderViewModel.Weight)] = new("", "Не найдено");
        var form = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = new Reader(preview with { Fields = fields }) };
        await form.ImportFilesAsync(["one.html", "two.html"], Token);
        Assert.All(form.BatchItems, item => { Assert.False(item.IsReady); Assert.True(item.HasParameters); Assert.True(item.HasWarning); });
        form.BatchItems[0].Form.Weight.Value = "1,2"; form.BatchItems[1].Form.Weight.Value = "3";
        Assert.True(form.SaveCommand.CanExecute(null));
        form.BatchItems[1].Form.SpecificationNumber.Value = "другой";
        Assert.All(form.BatchItems, item => Assert.False(item.HasWarning));
    }
    [Fact]
    public async Task ConvertingFilledSingleFormPreservesManualEditsSharedHeaderAndQuoteInputs()
    {
        var form = ManualOrderTests.ValidForm(new DemoOrderStore());
        var values = form.Fields.Select(field => field.Value).ToArray(); form.ImportReader = new Reader(await Preview());
        await form.ImportFilesAsync(["one.html", "two.html"], Token);
        Assert.Equal(3, form.BatchItems.Count);
        Assert.Equal(values, form.BatchItems[0].Form.Fields.Select(field => field.Value));
        Assert.Equal("Текущая заявка", form.BatchItems[0].FileName);
        form.Customer.Value = "Общий"; Assert.All(form.BatchItems, item => Assert.Equal("Общий", item.Form.Customer.Value));
        Assert.Equal(values[1], form.BatchItems[0].Form.SpecificationNumber.Value);
    }
    [Fact]
    public async Task DuplicateFileAndLimitNeverSilentlySkipOrOverwriteCards()
    {
        var reader = new Reader(await Preview()); var form = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        await form.ImportFilesAsync(["one.html", "one.html"], Token);
        Assert.Equal(1, reader.Calls); Assert.Equal(2, form.BatchItems.Count); Assert.True(form.BatchItems[1].HasFailure);
        Assert.False(form.SaveCommand.CanExecute(null)); form.BatchItems[1].RemoveCommand.Execute(null);
        Assert.True(form.SaveCommand.CanExecute(null));
        await form.ImportFilesAsync(Enumerable.Range(0, 100).Select(i => i + ".html").ToArray(), Token);
        Assert.Single(form.BatchItems); Assert.Contains("100", form.ImportError); Assert.Equal(1, reader.Calls);
    }
    [Fact]
    public async Task AppendingAndEditingWhileReadingWorksAndRemovedCardsNeverReturn()
    {
        var reader = new SpecificationImportTests.BlockingReader(); var form = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        var first = form.ImportFilesAsync(["one.html", "two.html"], Token);
        Assert.Equal(2, form.BatchItems.Count); Assert.Equal(2, reader.Calls); Assert.True(form.CanAddFiles);
        var second = form.ImportFilesAsync(["three.html"], Token);
        Assert.Equal(3, form.BatchItems.Count); Assert.Equal(2, reader.Calls);
        form.Manager.Value = "Общий"; form.BatchItems[0].RemoveCommand.Execute(null);
        reader.Result.SetResult(await Preview()); await Task.WhenAll(first, second);
        Assert.Equal(2, form.BatchItems.Count); Assert.All(form.BatchItems, item => { Assert.True(item.IsReady); Assert.Equal("Общий", item.Form.Manager.Value); });
        Assert.DoesNotContain(form.BatchItems, item => item.FileName == "one.html");
    }
    [Fact]
    public async Task UnconfirmedBatchRetainsEveryCardAndRequiresReloadWithoutRetrying()
    {
        var creator = new UnconfirmedCreator(); var store = new DemoOrderStore();
        var main = new MainWindowViewModel(store, creator); var form = main.CreateOrderForm()!;
        form.ImportReader = new Reader(await Preview()); await form.ImportFilesAsync(["one.html", "two.html"], Token);
        form.Manager.Value = "Иван"; form.Customer.Value = "Клиент";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(main.RequiresReload); Assert.True(form.RequiresReload); Assert.False(form.CanAddFiles);
        Assert.False(form.SaveCommand.CanExecute(null)); Assert.Equal(1, creator.Calls); Assert.Equal(2, form.BatchItems.Count);
        await main.ReloadCommand.ExecuteAsync(null);
        Assert.Same(form, main.CreateOrderForm()); Assert.True(form.SaveCommand.CanExecute(null)); Assert.Equal(1, creator.Calls);
        Assert.Equal("Клиент", form.Customer.Value);
    }

    [Fact]
    public async Task SequentialDropsBecomeBatchAndRepeatedOriginalFileIsAnErrorCard()
    {
        var form = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = new Reader(await Preview()) };
        await form.ImportFilesAsync(["one.html"], Token, appendToCurrent: true);
        Assert.False(form.IsBatchMode);
        form.Customer.Value = "Общий"; form.Weight.Value = "999";
        await form.ImportFilesAsync(["two.html"], Token, appendToCurrent: true);
        Assert.True(form.IsBatchMode); Assert.Equal(2, form.BatchItems.Count);
        Assert.Equal("999", form.BatchItems[0].Form.Weight.Value); Assert.False(form.HasPendingImport);
        await form.ImportFilesAsync(["one.html"], Token, appendToCurrent: true);
        Assert.Equal(3, form.BatchItems.Count); Assert.True(form.BatchItems[2].HasFailure);
        Assert.Equal("Общий", form.Customer.Value); Assert.False(form.SaveCommand.CanExecute(null));
    }
    [Fact]
    public async Task CancelBatchReadKeepsCardsAndDoesNotAcceptLateResults()
    {
        var reader = new SpecificationImportTests.BlockingReader();
        var form = new NewOrderViewModel(new DemoOrderStore()) { ImportReader = reader };
        var reading = form.ImportFilesAsync(["one.html", "two.html", "three.html"], Token);
        form.CancelImportRead(); await reading;
        Assert.False(form.IsBatchReading); Assert.Equal(3, form.BatchItems.Count);
        Assert.All(form.BatchItems, item => { Assert.True(item.HasFailure); Assert.False(item.HasParameters); });
        reader.Result.SetResult(await Preview());
        Assert.All(form.BatchItems, item => Assert.False(item.HasParameters));
    }

    [Fact]
    public async Task CancellingBatchPriceCalculationDoesNotEscapeOrContinueToNextCard()
    {
        var creator = new WaitingPriceCreator(); var form = new NewOrderViewModel(creator) { ImportReader = new Reader(await Preview()) };
        await form.ImportFilesAsync(["one.html", "two.html"], Token);
        var calculation = form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.True(form.IsBusy); Assert.False(form.CanAddFiles); Assert.Equal(1, creator.Calls);
        form.CalculatePriceCommand.Cancel(); await calculation;
        Assert.False(form.IsBusy); Assert.True(form.CanAddFiles); Assert.Equal(1, creator.Calls);
        Assert.All(form.BatchItems, item => Assert.False(item.Form.HasPriceError));
    }
    private sealed class WaitingPriceCreator : IManualOrderCreator, IManualOrderBatchCreator, IPriceReader
    {
        public int Calls;
        public async Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
        { Calls++; await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<IReadOnlyList<OrderSummary>> CreateBatchAsync(IReadOnlyList<ManualOrderDraft> drafts, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
    private sealed class UnconfirmedCreator : IManualOrderCreator, IManualOrderBatchCreator
    {
        public int Calls;
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Single save must not be used");
        public Task<IReadOnlyList<OrderSummary>> CreateBatchAsync(IReadOnlyList<ManualOrderDraft> drafts, CancellationToken cancellationToken = default)
        { Calls++; throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Нет подтверждения"); }
    }
}
