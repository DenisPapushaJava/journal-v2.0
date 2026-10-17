using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class PriceExpansionTests
{
    [Fact]
    public void OnlyCanonicalAppendedLongIntegerRatesAreAllowed()
    {
        var original = LocalJournalFixture.Prices[1];
        var values = Append(original, new PriceRate(2.80m, "2,8", 0));
        var change = new PriceTableChange(original, values).Snapshot();
        Assert.True(change.HasAdditions); Assert.Equal(new PriceRate(2.8m, "2,8", 0), Assert.Single(change.AddedRates));
        Assert.Equal("28", PriceTableChange.ColumnName(28.00m));
        foreach (var invalid in new PriceRate[] { new(2.8m, "2.8", 20), new(2.8m, "2,80", 20), new(2.8m, "2,8]; DROP TABLE Orders", 20),
            new(2.8m, "2,8", -1), new(2.8m, "2,8", null), new(0m, "0", 20), new(2.5m, "2,50", 20) })
            Assert.Throws<ArgumentException>(() => new PriceTableChange(original, Append(original, invalid)).Snapshot());
        Assert.Throws<ArgumentException>(() => new PriceTableChange(original, original with { Rates = original.Rates.Reverse().ToArray() }).Snapshot());
        Assert.Throws<ArgumentException>(() => new PriceTableChange(original, original with { Rates = original.Rates.Skip(1).ToArray() }).Snapshot());
    }

    [Fact]
    public async Task LocalAdditionKeepsLegacyRepresentationOrdersAndOtherPricesAndCalculatesTheNewThickness()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var original = await store.ReadAsync(PriceTables.HansLaserId2, TestContext.Current.CancellationToken);
        var orders = await store.ReadAsync(TestContext.Current.CancellationToken);
        var values = Append(original, new(2.8m, "2,8", 25), new(3.7m, "3,7", 0));
        var saved = await store.SaveAsync(new(original, values), TestContext.Current.CancellationToken);
        Assert.True(PriceTableChange.Matches(values, saved));
        var reopened = new LocalJournalStore(fixture.Path);
        Assert.True(PriceTableChange.Matches(values, await reopened.ReadAsync(original.Name, TestContext.Current.CancellationToken)));
        Assert.Equal(orders, await reopened.ReadAsync(TestContext.Current.CancellationToken));
        foreach (var table in LocalJournalFixture.Prices.Where(table => table.Name != original.Name))
            Assert.True(PriceTableChange.Matches(table, await reopened.ReadAsync(table.Name, TestContext.Current.CancellationToken)));
        var draft = LocalJournalFixture.Draft with { ThicknessMillimeters = 2.8m };
        var created = await reopened.CreateAsync(draft, TestContext.Current.CancellationToken);
        Assert.Equal(CuttingPriceCalculator.Calculate(draft.PriceRequest, values).StoredPrice, created.StoredCuttingPrice);
        var stale = await Assert.ThrowsAsync<OrderWriteException>(() => store.SaveAsync(new(original, Append(original, new PriceRate(4.2m, "4,2", 20))), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, stale.Failure);
        Assert.True(PriceTableChange.Matches(values, await store.ReadAsync(original.Name, TestContext.Current.CancellationToken)));
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.bak"));
    }

    [AvaloniaFact]
    public async Task NewRatesAreDraftsUntilSaveAndRejectEquivalentThicknessAndMissingPrice()
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "", PriceStorageKind.Session);
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        model.BeginAddRateCommand.Execute(null); model.NewThickness = "2,500"; model.NewRate = "20";
        model.StageRateCommand.Execute(null); Assert.True(model.HasAdditionError); Assert.Contains("уже есть", model.AdditionError);
        foreach (var invalid in new[] { "0", "-1", "1+2", "NaN", "2; DROP TABLE" })
        { model.NewThickness = invalid; model.StageRateCommand.Execute(null); Assert.True(model.HasAdditionError); }
        model.NewThickness = "2.8000";
        foreach (var invalid in new[] { "", "-1", "1,5", "2147483648" })
        { model.NewRate = invalid; model.StageRateCommand.Execute(null); Assert.True(model.HasAdditionError); }
        model.NewRate = "0"; model.StageRateCommand.Execute(null);
        Assert.False(model.ShowAddRate); Assert.True(model.CanSave);
        var added = Assert.Single(model.Rows, row => row.IsNew);
        Assert.Equal("2,8", added.Original.ColumnName); Assert.Equal("Новая", added.CurrentPrice);
        Assert.DoesNotContain((await store.ReadAsync(PriceTables.Laser)).Rates, rate => rate.ThicknessMillimeters == 2.8m);
        model.RemoveNewRate(model.Rows.First(row => !row.IsNew)); Assert.Equal(17, model.Rows.Count);
        model.RemoveNewRate(added); Assert.Equal(16, model.Rows.Count); Assert.False(model.HasChanges);
        Add(model, "2,8", "35"); model.Rows.First(row => row.IsNew).Value = "";
        Assert.True(model.HasInvalidPrices); Assert.False(model.CanSave);
        model.Rows.First(row => row.IsNew).Value = "35";
        Add(model, "3,7", "36");
        await model.SaveCommand.ExecuteAsync(null);
        Assert.False(model.HasError, model.Error); Assert.False(model.IsEditing); Assert.DoesNotContain(model.Rows, row => row.IsNew);
        var actual = await store.ReadAsync(PriceTables.Laser);
        Assert.Equal(35, actual.Rates.Single(rate => rate.ThicknessMillimeters == 2.8m).RublesPerMeter);
        Assert.Equal(36, actual.Rates.Single(rate => rate.ThicknessMillimeters == 3.7m).RublesPerMeter);
    }

    [AvaloniaFact]
    public async Task ExplicitRefreshMergesConcurrentAddedColumnsWithoutLosingOwnNewRatesOrEdits()
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        model.Rows[0].Value = "51"; Add(model, "2,8", "71");
        var original = await store.ReadAsync(PriceTables.Laser);
        await store.SaveAsync(new(original, Append(original, new PriceRate(3.7m, "3,7", 93))));
        await model.SaveCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.True(model.Rows.Single(row => row.Original.ThicknessMillimeters == 2.8m).IsNew);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.True(model.CanSave);
        Assert.Equal("51", model.Rows[0].Value);
        Assert.Equal("93", model.Rows.Single(row => row.Original.ThicknessMillimeters == 3.7m).CurrentPrice);
        Assert.True(model.Rows.Single(row => row.Original.ThicknessMillimeters == 2.8m).IsNew);
        await model.SaveCommand.ExecuteAsync(null); Assert.False(model.HasError, model.Error);
        var actual = await store.ReadAsync(PriceTables.Laser);
        Assert.Equal(71, actual.Rates.Single(rate => rate.ThicknessMillimeters == 2.8m).RublesPerMeter);
        Assert.Equal(93, actual.Rates.Single(rate => rate.ThicknessMillimeters == 3.7m).RublesPerMeter);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostAcknowledgementReconcilesTheAddedColumnBeforeAnyRetry(bool applied)
    {
        var store = new DemoOrderStore(); var uncertain = new Uncertain(store, applied);
        var model = new PriceEditorViewModel(store.Prices, uncertain, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null); Add(model, "2,8", "77");
        await model.SaveCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.False(model.CanSave);
        await model.RefreshCommand.ExecuteAsync(null); Assert.False(model.HasError, model.Error);
        var row = model.Rows.Single(row => row.Original.ThicknessMillimeters == 2.8m);
        Assert.Equal("77", row.Value); Assert.Equal(!applied, row.IsNew); Assert.Equal(!applied, model.CanSave);
        Assert.Equal(1, uncertain.Attempts);
        if (!applied) { await model.SaveCommand.ExecuteAsync(null); Assert.False(model.HasError, model.Error); }
        Assert.Single((await store.ReadAsync(PriceTables.Laser)).Rates, rate => rate.ThicknessMillimeters == 2.8m);
    }

    [Fact]
    public void BackupIsCompleteAndLockedOrCancelledCopiesDoNotLeavePartialFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-price-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "synthetic.mdb");
        var bytes = Enumerable.Range(0, 300000).Select(i => (byte)i).ToArray(); File.WriteAllBytes(path, bytes);
        try
        {
            var backup = AccessPriceBackup.Create(path, TestContext.Current.CancellationToken);
            Assert.Equal(bytes, File.ReadAllBytes(backup)); Assert.Equal(bytes, File.ReadAllBytes(path));
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => AccessPriceBackup.Create(path, TestContext.Current.CancellationToken));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => AccessPriceBackup.Create(path, cancelled.Token));
            Assert.Single(Directory.GetFiles(directory, "*.bak")); Assert.Empty(Directory.GetFiles(directory, "*.partial"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task HostAcknowledgementMustContainEveryAppendedColumn()
    {
        var original = LocalJournalFixture.Prices[1]; var expanded = Append(original, new PriceRate(2.8m, "2,8", 47));
        var saved = await PriceEditingTests.Host(new(1, PriceTable: expanded)).SaveAsync(new(original, expanded), TestContext.Current.CancellationToken);
        Assert.True(PriceTableChange.Matches(expanded, saved));
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => PriceEditingTests.Host(new(1, PriceTable: original))
            .SaveAsync(new(original, expanded), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, failure.Failure);
    }

    internal static PriceTable Append(PriceTable table, params PriceRate[] rates) => table with { Rates = table.Rates.Concat(rates).ToArray() };
    internal static void Add(PriceEditorViewModel model, string thickness, string amount)
    { model.BeginAddRateCommand.Execute(null); model.NewThickness = thickness; model.NewRate = amount; model.StageRateCommand.Execute(null); Assert.False(model.HasAdditionError, model.AdditionError); }
    private sealed class Uncertain(DemoOrderStore store, bool applied) : IPriceEditor
    {
        public int Attempts { get; private set; }
        public async Task<PriceTable> SaveAsync(PriceTableChange change, CancellationToken cancellationToken = default)
        {
            Attempts++; if (Attempts > 1 || applied) { var result = await store.SaveAsync(change, cancellationToken); if (Attempts > 1) return result; }
            throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Обновите прайс.");
        }
    }
}
