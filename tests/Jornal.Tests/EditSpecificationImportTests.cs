using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Parsing.Specifications;
using Jornal.Infrastructure.Local;
using Xunit;
namespace Jornal.Tests;
public sealed class EditSpecificationImportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static EditOrderViewModel Form(Store store) => new(store.Current!, store, "Тестовый источник", _ => Task.FromResult(store.Current));
    internal sealed class Store : IOrderMaintenance
    {
        public OrderRecord? Current = OrderMaintenanceTests.Record() with { SheetCount = "2" };
        public int Updates; public int PriceReads; public string? LastTable; public bool Unknown;
        public Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default)
        {
            PriceReads++; LastTable = name;
            return Task.FromResult(new PriceTable(name, true, [new(2.5m, "2,5", 17), new(6m, "6", name == PriceTables.HansLaserId2 ? 101 : 10)]));
        }
        public async Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken cancellationToken = default)
        {
            if (Current != change.Expected) throw OrderWriteException.Conflict();
            if (Unknown) throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Не подтверждено");
            Current = change.Calculate(await ReadAsync(CuttingPriceCalculator.SelectTable(change.PriceRequest), cancellationToken));
            Updates++; return Current;
        }
        public Task DeleteAsync(OrderRecord expected, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
        public Task<OrderRecord> RestoreAsync(OrderRecord archived, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
    [Fact]
    public async Task ReadAndCancelPreserveManualEditsQuoteAndStorageUntilExplicitReplacement()
    {
        using var sample = new SpecificationImportTests.Sample(); var store = new Store(); var form = Form(store);
        form.Customer.Value = "Моя правка";
        await form.CalculateCommand.ExecuteAsync(null); var price = form.PriceText;
        var values = form.Fields.Select(entry => entry.Value).ToArray();
        await form.RequestImportAsync([sample.Path], Token);
        Assert.True(form.HasPendingImport); Assert.False(form.CanSave); Assert.False(form.CanImport);
        Assert.Equal(values, form.Fields.Select(entry => entry.Value)); Assert.Equal(price, form.PriceText);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(0, store.Updates);
        form.CancelImportReplacementCommand.Execute(null);
        Assert.False(form.HasPendingImport); Assert.Equal(values, form.Fields.Select(entry => entry.Value)); Assert.Equal(price, form.PriceText);
        await form.RequestImportAsync([sample.Path], Token); form.ReplaceImportCommand.Execute(null);
        Assert.True(form.HasImport); Assert.Equal("Моя правка", form.Customer.Value); Assert.Equal("Не рассчитана", form.PriceText);
        var change = form.BuildChange();
        Assert.Equal(store.Current, change.Expected); Assert.Equal(store.Current!.Id, change.Values.Id);
        Assert.Equal(store.Current.Manager, change.Values.Manager); Assert.Equal(store.Current.DeliveryNumber, change.Values.DeliveryNumber);
        Assert.Equal(store.Current.Color, change.Values.Color); Assert.Equal(store.Current.Price, change.Values.Price);
        Assert.Equal("1.1|14|31|-1|-1", change.Values.Coefficient);
        Assert.Equal(new DateTime(2026, 5, 14), change.Values.Date); Assert.Equal("0514-0078", change.Values.Specification);
        Assert.Equal(200 * 60 + 1, change.Values.ToSummary().CuttingDurationMinutes);
        Assert.Contains(form.Changes, row => row.Label == "Пробивки" && row.Before == "340" && row.After == "31");
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(0, store.Updates); Assert.Null(form.SavedRecord);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(1, store.Updates); Assert.Equal(change.Values with { Price = form.SavedRecord!.Price }, store.Current);
    }
    [Theory]
    [InlineData(null)] [InlineData("1,25")] [InlineData("1.25|20|340")] [InlineData("1.25|20|340|-1|-1")] [InlineData("1.25|20|340|2|N2|хвост")]
    public async Task HtmlToHansPopulatesMissingMetadataAndUsesNewMachinePrice(string? oldCoefficient)
    {
        var store = new Store { Current = OrderMaintenanceTests.Record() with { Coefficient = oldCoefficient, SheetCount = "2" } };
        var form = Form(store);
        var preview = SpecificationImportPreview.Create("new.pdf", new HansPdfSpecificationParser().Parse(HansSample.Document(), Token));
        form.ImportReader = new BatchImportTests.Reader(preview);
        await form.RequestImportAsync(["new.pdf"], Token); form.ReplaceImportCommand.Execute(null);
        var change = form.BuildChange();
        Assert.Equal("HansLaser", change.Values.CuttingType); Assert.Equal("N2", form.Gas.Value); Assert.Equal("2", form.MaterialId.Value);
        Assert.Equal("1.1|14|31|2|N2" + (oldCoefficient?.EndsWith("хвост", StringComparison.Ordinal) == true ? "|хвост" : ""), change.Values.Coefficient);
        Assert.Equal("Сплав_Х77", change.Values.Material); Assert.Equal("48.2", change.Values.Weight);
        Assert.Contains(form.Changes, row => row.Label == "Вид резки / станок" && row.After == "HansLaser");
        if (oldCoefficient is null) Assert.Contains(form.Changes, row => row.Label == "Газ HansLaser" && row.Before == "—" && row.After == "N2");
        await form.CalculateCommand.ExecuteAsync(null); Assert.Equal(PriceTables.HansLaserId2, store.LastTable);
        await form.SaveCommand.ExecuteAsync(null); Assert.Equal(1, store.Updates);
        Assert.Equal(change.Values.Id, store.Current!.Id); Assert.Equal(3666m, store.Current.Price);
    }
    [Fact]
    public async Task HansToBlockClearsOldGasIdAndUnknownTimeAndRetainsExtraLegacySegments()
    {
        using var sample = new SpecificationImportTests.Sample(SpecificationImportTests.Sample.Html.Replace("<tr><td>Время:(ч:мм)</td><td>200:01</td></tr>", ""));
        var original = OrderMaintenanceTests.Record() with { CuttingType = "HansLaser", Coefficient = "1.1|20|340|2|N2|old-extra", SheetCount = "2" };
        original = original with { CuttingTime = OrderCuttingTime.For(original.ToSummary(), 2207) };
        var store = new Store { Current = original }; var form = Form(store);
        await form.RequestImportAsync([sample.Path], Token); form.ReplaceImportCommand.Execute(null);
        var change = form.BuildChange();
        Assert.Equal("Лазер", change.Values.CuttingType); Assert.Empty(form.MaterialId.Value); Assert.Empty(form.Gas.Value);
        Assert.Equal("1.1|14|31|-1|-1|old-extra", change.Values.Coefficient); Assert.Null(change.Values.CuttingTime);
        Assert.Contains(form.Changes, row => row.Label == "Газ HansLaser" && row.Before == "N2" && row.After == "—");
        Assert.False(form.HasCuttingTimeNotice);
        await form.CalculateCommand.ExecuteAsync(null); Assert.Equal(PriceTables.Laser, store.LastTable);
    }
    [Fact]
    public async Task MissingImportedHansIdAndGasCannotUseAnOldValueOrDefaultTariff()
    {
        var report = new HansPdfSpecificationParser().Parse(HansSample.Replace(HansSample.Document(), "Сплав_Х77_2", "Сплав_Х77"), Token);
        var preview = SpecificationImportPreview.Create("new.pdf", report);
        var values = preview.Fields.ToDictionary(pair => pair.Key, pair => pair.Value); values["Gas"] = new("", "Не найдено");
        var store = new Store(); var form = Form(store); form.ImportReader = new BatchImportTests.Reader(preview with { Fields = values });
        await form.RequestImportAsync(["new.pdf"], Token); form.ReplaceImportCommand.Execute(null);
        Assert.Empty(form.MaterialId.Value); Assert.Empty(form.Gas.Value);
        await form.CalculateCommand.ExecuteAsync(null); await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, store.PriceReads); Assert.Equal(0, store.Updates); Assert.True(form.MaterialId.HasError); Assert.True(form.Gas.HasError);
        form.MaterialId.Value = "2"; form.Gas.Value = "N2";
        await form.CalculateCommand.ExecuteAsync(null); await form.SaveCommand.ExecuteAsync(null); Assert.Equal(1, store.Updates);
    }
    [Fact]
    public async Task RejectedMultipleUnknownOrCancelledFilesNeverAlterExistingFieldsOrQuote()
    {
        using var bad = new SpecificationImportTests.Sample("<html>Счёт, вес, HANSLASER</html>");
        var store = new Store(); var form = Form(store); await form.CalculateCommand.ExecuteAsync(null);
        var before = form.Fields.Select(entry => entry.Value).ToArray(); var price = form.PriceText;
        await form.RequestImportAsync([bad.Path, bad.Path], Token); Assert.Contains("одну", form.ImportError);
        await form.RequestImportAsync([bad.Path], Token); Assert.True(form.HasImportError); Assert.False(form.HasPendingImport);
        var reader = new SpecificationImportTests.BlockingReader(); form.ImportReader = reader;
        var reading = form.RequestImportAsync([bad.Path], Token);
        Assert.True(form.IsBusy); Assert.False(form.CanSave); form.CancelImportRead(); await reading;
        Assert.Equal(before, form.Fields.Select(entry => entry.Value)); Assert.Equal(price, form.PriceText); Assert.Equal(0, store.Updates);
    }
    [Fact]
    public async Task ConflictRebasePreservesImportedFieldsCountsAndTimeWithoutOverwritingForeignStatus()
    {
        using var sample = new SpecificationImportTests.Sample(); var store = new Store(); var form = Form(store);
        await form.RequestImportAsync([sample.Path], Token); form.ReplaceImportCommand.Execute(null);
        await form.CalculateCommand.ExecuteAsync(null);
        store.Current = store.Current! with { Manager = "Другой менеджер", Color = OrderColors.Ready, Weight = "999", Coefficient = "9|1|2|2|O2" };
        await form.SaveCommand.ExecuteAsync(null); Assert.True(form.RequiresReload); Assert.False(form.CanImport);
        await form.RequestImportAsync([sample.Path], Token); Assert.False(form.HasPendingImport);
        await form.RefreshCommand.ExecuteAsync(null); form.RebaseCommand.Execute(null);
        var change = form.BuildChange();
        Assert.Equal("Другой менеджер", form.Manager.Value); Assert.Equal(OrderColors.Ready, change.Values.Color);
        Assert.Equal("48.2", change.Values.Weight); Assert.Equal("1.1|14|31|-1|-1", change.Values.Coefficient);
        Assert.Equal(12001L, change.Values.ToSummary().CuttingDurationMinutes);
        Assert.Equal(store.Current, change.Expected); Assert.Equal(0, store.Updates);
    }
    [Fact]
    public async Task UnknownCommitKeepsImportAndBlocksAnotherReplacementUntilRefresh()
    {
        using var sample = new SpecificationImportTests.Sample(); var store = new Store { Unknown = true }; var form = Form(store);
        await form.RequestImportAsync([sample.Path], Token); form.ReplaceImportCommand.Execute(null);
        await form.CalculateCommand.ExecuteAsync(null); await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.RequiresReload); Assert.False(form.CanImport); Assert.True(form.HasImport);
        var fields = form.Fields.Select(entry => entry.Value).ToArray();
        await form.RequestImportAsync([sample.Path], Token);
        Assert.False(form.HasPendingImport); Assert.Equal(fields, form.Fields.Select(entry => entry.Value)); Assert.Equal(0, store.Updates);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LocalStoreReplacementUpdatesExactlyOneExistingRowAndSurvivesReopening(bool hans)
    {
        using var fixture = new LocalJournalFixture();
        var prices = LocalJournalFixture.Prices.Select(table => table with { Rates = table.Rates.Select(rate => rate with { RublesPerMeter = 17 }).ToArray() }).ToArray();
        await LocalJournalStore.InitializeAsync(fixture.Path, LocalJournalFixture.Orders, prices, "synthetic.mdb", new string('a', 64), Token);
        var store = new LocalJournalStore(fixture.Path);
        var original = (await store.ReadRecordAsync(42, Token))!;
        var history = await store.ReadAsync(Token); var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        var form = new EditOrderViewModel(original, store, "Тестовый источник", token => store.ReadRecordAsync(42, token));
        using var html = new SpecificationImportTests.Sample();
        var preview = hans ? SpecificationImportPreview.Create("new.pdf", new HansPdfSpecificationParser().Parse(HansSample.Document(), Token)) :
            await new SpecificationImportService().ReadAsync(html.Path, Token);
        form.ImportReader = new BatchImportTests.Reader(preview);
        await form.RequestImportAsync([hans ? "new.pdf" : "new.html"], Token); form.ReplaceImportCommand.Execute(null);
        await form.SaveCommand.ExecuteAsync(null); Assert.Null(form.SavedRecord);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        await form.SaveCommand.ExecuteAsync(null); Assert.NotNull(form.SavedRecord);
        var reopened = new LocalJournalStore(fixture.Path); var rows = await reopened.ReadAsync(Token);
        Assert.Equal(history.Count, rows.Count); Assert.Equal(history.Where(row => row.Id != 42), rows.Where(row => row.Id != 42));
        Assert.Equal(form.SavedRecord, await reopened.ReadRecordAsync(42, Token));
        Assert.Equal(original.Color, form.SavedRecord.Color); Assert.Equal(original.DeliveryNumber, form.SavedRecord.DeliveryNumber);
        Assert.Equal(original.Manager, form.SavedRecord.Manager); Assert.Equal(original.Customer, form.SavedRecord.Customer);
        Assert.Equal(hans ? "HansLaser" : "Лазер", form.SavedRecord.CuttingType);
        Assert.Equal(6d, form.SavedRecord.Thickness);
        Assert.Equal(hans ? "N2" : "", form.SavedRecord.ToSummary().CuttingGas);
    }

}
