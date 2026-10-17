using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Local;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class ThicknessOptionsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void AvailableThicknessesUseTheRealTariffAndMachineRangeAndSortNumerically()
    {
        var table = new PriceTable(PriceTables.Laser, true, [new(10m, "10", 0), new(2.5m, "2,5", null),
            new(3m, "3", 17), new(30m, "30", 1), new(0.8m, "0,8", 1), new(1m, "1", -1)]);
        Assert.Equal(new[] { 0.8m, 3m, 10m }, CuttingPriceCalculator.AvailableThicknesses("Лазер", "", table));
        Assert.Empty(CuttingPriceCalculator.AvailableThicknesses("Лазер", "", new(PriceTables.Laser, false, [])));
        Assert.Equal(PriceTables.HansLaserId1, CuttingPriceCalculator.SelectTableForMachine("HansLaser", "1"));
        Assert.Equal(PriceTables.HansLaserId2, CuttingPriceCalculator.SelectTableForMachine("HansLaser", "2"));
        Assert.Equal(PriceTables.Laser, CuttingPriceCalculator.SelectTableForMachine("HansLaser", "01"));
    }

    [Fact]
    public async Task RestrictedSelectionKeepsUnlistedImportedOrHistoricalValueAndNumericSpelling()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path); var form = ManualOrderTests.ValidForm(store);
        form.Thickness.Value = "2,50";
        await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        Assert.Equal(new[] { "2,5", "10" }, form.Thickness.Choices);
        Assert.Equal("2,5", form.Thickness.SelectedChoice); Assert.Equal("2,50", form.Thickness.Value);
        form.Thickness.SelectedChoice = null; Assert.Equal("2,50", form.Thickness.Value);
        form.Thickness.SelectedChoice = "6"; Assert.Equal("2,50", form.Thickness.Value);
        form.Thickness.Value = "6";
        Assert.Null(form.Thickness.SelectedChoice); Assert.Equal("6", form.Thickness.UnlistedChoice);
        Assert.True(form.ThicknessOptions.HasUnavailableValue); Assert.Contains("6 мм", form.Thickness.DisplayError);
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.IsSaved); Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        form.Thickness.SelectedChoice = "10";
        Assert.Equal("10", form.Thickness.Value); Assert.False(form.ThicknessOptions.HasUnavailableValue);
        Assert.Null(form.Thickness.ChoiceError);
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.IsSaved); Assert.Equal(0m, form.CreatedOrder!.StoredCuttingPrice);
        form.StopThicknessOptions();
    }

    [Fact]
    public async Task HansMaterialIdChangesTheSourceAndDoesNotReplaceTheCurrentThickness()
    {
        var creator = new ReaderCreator
        {
            Read = (name, _) => Task.FromResult(new PriceTable(name, true,
                name == PriceTables.HansLaserId1 ? [new(3m, "3", 17)] : [new(6m, "6", 17)]))
        };
        var form = ManualOrderTests.ValidForm(creator); form.MaterialId.Value = "1";
        await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        Assert.Equal(new[] { "3" }, form.Thickness.Choices);
        form.MaterialId.Value = "2"; await form.ThicknessOptions.WaitForLoadAsync(Token);
        Assert.Equal(new[] { "6" }, form.Thickness.Choices); Assert.Equal("2,5", form.Thickness.Value);
        Assert.Equal(PriceTables.HansLaserId2, form.ThicknessOptions.TableName);
        form.StopThicknessOptions();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedHansWithoutMaterialIdCannotUseTheFallbackPriceList(bool alreadyOpen)
    {
        var report = new HansPdfSpecificationParser().Parse(
            HansSample.Replace(HansSample.Document(), "Сплав_Х77_2", "Сплав_Х77"), Token);
        var creator = new ReaderCreator(); var form = new NewOrderViewModel(creator);
        if (alreadyOpen)
        {
            form.CuttingType.Value = "HansLaser"; form.MaterialId.Value = "1";
            await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        }
        var originalReads = creator.Reads;
        form.ApplyImport(SpecificationImportPreview.Create("spec.pdf", report));
        await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        Assert.Equal(originalReads, creator.Reads); Assert.Empty(form.Thickness.Choices!);
        Assert.True(form.ThicknessOptions.HasUnavailableValue); Assert.False(form.ThicknessOptions.CanOpenPrices);
        Assert.Contains("ID материала", form.Thickness.ChoiceError);
        form.MaterialId.Value = "2"; await form.ThicknessOptions.WaitForLoadAsync(Token);
        Assert.Equal(originalReads + 1, creator.Reads); Assert.Equal(PriceTables.HansLaserId2, form.ThicknessOptions.TableName);
        form.StopThicknessOptions();
    }

    [Fact]
    public async Task LateReplyForPreviousMachineCannotOverwriteTheNewChoices()
    {
        var laser = new TaskCompletionSource<PriceTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var plasma = new TaskCompletionSource<PriceTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var creator = new ReaderCreator { Read = (name, _) => name == PriceTables.Laser ? laser.Task : plasma.Task };
        var form = ManualMdbCreationTests.Filled(creator);
        var first = form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        Assert.True(form.ThicknessOptions.IsLoading);
        form.CuttingType.Value = "Плазма";
        plasma.SetResult(new(PriceTables.Plasma, true, [new(4m, "4", 10)]));
        await form.ThicknessOptions.WaitForLoadAsync(Token);
        laser.SetResult(new(PriceTables.Laser, true, [new(2.5m, "2,5", 10)])); await first;
        Assert.Equal(new[] { "4" }, form.Thickness.Choices); Assert.Equal("2,5", form.Thickness.Value);
        Assert.Equal(PriceTables.Plasma, form.ThicknessOptions.TableName); Assert.True(form.ThicknessOptions.HasUnavailableValue);
        form.StopThicknessOptions();
    }

    [Fact]
    public async Task ReadFailureKeepsTheValueAndAllowsExplicitReload()
    {
        var fail = true;
        var creator = new ReaderCreator { Read = (name, _) => fail ? Task.FromException<PriceTable>(new IOException("Недоступно")) :
            Task.FromResult(new PriceTable(name, true, [new(2.5m, "2,5", 0)])) };
        var form = ManualMdbCreationTests.Filled(creator);
        await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        Assert.Contains("Не удалось загрузить толщины", form.Thickness.DisplayError); Assert.Equal("2,5", form.Thickness.Value);
        Assert.False(form.ThicknessOptions.HasUnavailableValue); Assert.True(form.ThicknessOptions.CanReload);
        fail = false;
        await form.ThicknessOptions.ReloadCommand.ExecuteAsync(null);
        Assert.Null(form.Thickness.ChoiceError); Assert.Equal("2,5", form.Thickness.SelectedChoice);
        form.StopThicknessOptions();
    }

    [Fact]
    public async Task BatchParsingDoesNotWaitForPricesAndRemovingOneCardDoesNotCancelOthers()
    {
        var price = new TaskCompletionSource<PriceTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        var creator = new ReaderCreator { Read = (_, token) => { token.Register(() => cancelled = true); return price.Task; } };
        var form = new NewOrderViewModel(creator) { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        await form.ImportFilesAsync(["one.html", "two.html", "three.html"], Token);
        Assert.All(form.BatchItems, item => { Assert.False(item.IsReading); Assert.True(item.HasParameters); Assert.True(item.Form.ThicknessOptions.IsLoading); });
        Assert.Equal(1, creator.Reads);
        form.BatchItems[0].RemoveCommand.Execute(null); Assert.False(cancelled);
        price.SetResult(new(PriceTables.Laser, true, [new(6m, "6", 10)]));
        await form.WaitForThicknessOptionsAsync(Token);
        Assert.Equal(2, form.BatchItems.Count); Assert.All(form.BatchItems, item => Assert.Equal(new[] { "6" }, item.Form.Thickness.Choices));
        Assert.Equal(1, creator.Reads); Assert.Equal(0, creator.Writes); form.StopThicknessOptions();
    }

    [Fact]
    public async Task CancelledReloadAndReopeningTheFormKeepTheKnownUnsupportedValue()
    {
        var creator = new ReaderCreator(); var form = ManualMdbCreationTests.Filled(creator); form.Thickness.Value = "6";
        await form.RefreshThicknessOptionsAsync(cancellationToken: Token);
        var error = form.Thickness.ChoiceError; Assert.True(form.ThicknessOptions.HasUnavailableValue);
        var pending = new TaskCompletionSource<PriceTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        creator.Read = (_, _) => pending.Task;
        using var cancel = new CancellationTokenSource();
        var read = form.RefreshThicknessOptionsAsync(true, cancel.Token); cancel.Cancel(); await read;
        Assert.Equal("6", form.Thickness.Value); Assert.Equal(error, form.Thickness.ChoiceError);
        Assert.True(form.ThicknessOptions.HasUnavailableValue); Assert.False(form.ThicknessOptions.IsLoading);
        pending.SetResult(new(PriceTables.Laser, true, [new(6m, "6", 0)]));
        form.StopThicknessOptions(); form.StartThicknessOptions();
        await form.RefreshThicknessOptionsAsync(true, Token);
        Assert.Equal("6", form.Thickness.Value); Assert.Equal("6", form.Thickness.SelectedChoice);
        Assert.True(form.ThicknessOptions.CanReload); Assert.True(form.SaveCommand.CanExecute(null));
        form.StopThicknessOptions();
    }

    internal sealed class ReaderCreator : IManualOrderCreator, IManualOrderBatchCreator, IPriceReader
    {
        public Func<string, CancellationToken, Task<PriceTable>> Read = (name, _) => Task.FromResult(new PriceTable(name, true, [new(2.5m, "2,5", 17)]));
        public int Reads, Writes;
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref Reads); return Read(name, cancellationToken); }
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        { Writes++; return Task.FromResult(draft.ToOrder(100, 17m)); }
        public Task<IReadOnlyList<OrderSummary>> CreateBatchAsync(IReadOnlyList<ManualOrderDraft> drafts, CancellationToken cancellationToken = default)
        { Writes++; return Task.FromResult<IReadOnlyList<OrderSummary>>(drafts.Select((draft, i) => draft.ToOrder(100 + i, 17m)).ToArray()); }
    }
}
