using System.Text.Json;
using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class PriceThicknessEditingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static PriceTableChange Rename(PriceTable table, decimal thickness = 2.8m) => new(table, table with
    { Rates = table.Rates.Select((rate, index) => index == 0 ? new(thickness, PriceTableChange.ColumnName(thickness), 23) : rate).ToArray() });

    [Fact]
    public void RenamesAreExplicitNumericChangesWithoutMergingDroppingReorderingOrReformattingExistingColumns()
    {
        var original = LocalJournalFixture.Prices[1]; var change = Rename(original).Snapshot();
        Assert.True(change.HasRenames); Assert.True(change.ChangesSchema); Assert.False(change.HasAdditions);
        Assert.Equal(original.Rates[0], Assert.Single(change.RenamedRates).Before);
        Assert.Throws<ArgumentException>(() => Rename(original, 6m).Snapshot());
        Assert.Throws<ArgumentException>(() => new PriceTableChange(original, original with { Rates = original.Rates.Reverse().ToArray() }).Snapshot());
        Assert.Throws<ArgumentException>(() => new PriceTableChange(original, change.Values with { Rates = change.Values.Rates.Skip(1).ToArray() }).Snapshot());
        Assert.Throws<ArgumentException>(() => new PriceTableChange(original, change.Values with { Rates = [new(2.8m, "2.8", 23), .. original.Rates.Skip(1)] }).Snapshot());
        Assert.Throws<ArgumentException>(() => (change with { ConfirmedUsage = [new("wrong", 1)] }).Snapshot());
        Assert.Throws<ArgumentException>(() => (change with { ConfirmedUsage = [new("2,5", -1)] }).Snapshot());
        Assert.Throws<OrderWriteException>(() => change.VerifyUsage([new("2,5", 1)]));
        var confirmed = (change with { ConfirmedUsage = [new("2,5", 1)] }).Snapshot();
        Assert.NotSame(confirmed.ConfirmedUsage, JsonSerializer.Deserialize<PriceTableChange>(JsonSerializer.Serialize(confirmed))!.ConfirmedUsage);
        confirmed.VerifyUsage([new("2,5", 1)]);
        Assert.Throws<OrderWriteException>(() => confirmed.VerifyUsage([new("2,5", 2)]));
    }

    [Fact]
    public void UsageCountsTheWholeSharedPriceAndExactHansProfileIncludingCustomMachines()
    {
        var machine = new CuttingMachine("ГАР", "PriceMachine_" + new string('a', 32), CuttingMachine.WaterjetProfile);
        PriceUsageOrder[] orders = [new("Лазер", 2.5m, ""), new("HansLaser", 2.5m, "0"), new("HansLaser", 2.5m, "1"),
            new("HansLaser", 2.5m, "2"), new("Плазма", 2.5m, ""), new("Лазер", 6m, ""), new("ГАР", 2.5m, ""), new("неизвестный", 2.5m, "")];
        Assert.Equal(2, Assert.Single(PriceThicknessUsage.Count(Rename(LocalJournalFixture.Prices[1]), orders, [machine])).OrderCount);
        Assert.Equal(1, Assert.Single(PriceThicknessUsage.Count(Rename(LocalJournalFixture.Prices[4]), orders, [machine])).OrderCount);
        Assert.Equal(1, Assert.Single(PriceThicknessUsage.Count(Rename(LocalJournalFixture.Prices[5]), orders, [machine])).OrderCount);
        Assert.Equal(1, Assert.Single(PriceThicknessUsage.Count(Rename(LocalJournalFixture.Prices[1] with { Name = machine.TableName }), orders, [machine])).OrderCount);
        Assert.Equal("2", PriceUsageOrder.FromStored("HansLaser", 2.5m, "1 | 10 | 20 | 2 | N2").MaterialId);
    }

    [Fact]
    public async Task LocalRenameIsAtomicWithPriceEditsAndAdditionsAndNeverRewritesOrderHistory()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync(); var store = new LocalJournalStore(fixture.Path);
        var before = await store.ReadAsync(Token); var original = await store.ReadAsync(PriceTables.HansLaserId2, Token);
        var change = Rename(original); change = change with { Values = PriceExpansionTests.Append(change.Values, new PriceRate(21m, "21", 0)) };
        var bytes = await File.ReadAllBytesAsync(fixture.Path, Token);
        var usage = await store.ReadThicknessUsageAsync(change, Token); Assert.Equal(1, Assert.Single(usage).OrderCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path, Token));
        await Assert.ThrowsAsync<OrderWriteException>(() => store.SaveAsync(change, Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Path, Token));
        var saved = await store.SaveAsync(change with { ConfirmedUsage = usage.ToArray() }, Token);
        Assert.True(PriceTableChange.Matches(change.Values, saved));
        var reopened = new LocalJournalStore(fixture.Path);
        Assert.Equal(before, await reopened.ReadAsync(Token));
        Assert.DoesNotContain((await reopened.ReadAsync(original.Name, Token)).Rates, rate => rate.ThicknessMillimeters == 2.5m);
        Assert.Equal(575m, (await new CuttingPriceService(reopened).CalculateAsync(new("HansLaser", 2.8m, 25, 1m, "2"), Token)).StoredPrice);
        var missing = await new CuttingPriceService(reopened).CalculateResultAsync(new("HansLaser", 2.5m, 25, 1m, "2"), Token);
        Assert.Equal(PriceCalculationFailure.PriceUnavailable, missing.Failure);
        foreach (var table in LocalJournalFixture.Prices.Where(table => table.Name != original.Name))
            Assert.True(PriceTableChange.Matches(table, await reopened.ReadAsync(table.Name, Token)));
    }

    [AvaloniaFact]
    public async Task DraftValidatesNullDuplicatesAndEquivalentSpellingAndCancelRestoresEverything()
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        var row = model.Rows.Single(row => row.Original.ThicknessMillimeters == 2.5m);
        row.ThicknessValue = "2,50"; Assert.False(row.IsChanged); Assert.False(model.CanSave);
        row.ThicknessValue = null!; Assert.True(row.HasThicknessError); Assert.False(model.CanSave);
        row.ThicknessValue = "3"; Assert.True(row.HasThicknessConflict); Assert.Contains("уже есть", row.ThicknessError);
        row.ThicknessValue = "2,8"; row.Value = "45"; Assert.False(row.HasThicknessError); Assert.True(model.CanSave);
        Assert.Same(row, model.Rows[4]); // Ввод не пересортировывает строки под курсором.
        model.CancelEditCommand.Execute(null); Assert.False(model.HasChanges); Assert.False(model.IsEditing);
        Assert.Equal("25", model.Rows.Single(row => row.Thickness == "2,5").Value);
        model.BeginEditCommand.Execute(null); model.BeginAddRateCommand.Execute(null); model.NewThickness = null!; model.NewRate = null!;
        model.StageRateCommand.Execute(null); Assert.True(model.HasAdditionError);
        model.NewThickness = "2,8"; model.StageRateCommand.Execute(null); Assert.True(model.HasAdditionError);
    }

    [AvaloniaFact]
    public async Task ConfirmationOnlyReadsAndStaleUsageRequiresAnotherReviewWithoutWriting()
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        model.Rows.Single(row => row.Thickness == "2,5").ThicknessValue = "2,8";
        var original = await store.ReadAsync(PriceTables.Laser, Token);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.True(model.IsConfirmingThickness); Assert.Equal(1, Assert.Single(model.ThicknessChanges).OrderCount);
        Assert.True(PriceTableChange.Matches(original, await store.ReadAsync(PriceTables.Laser, Token))); Assert.False(model.CanSave);
        await store.CreateAsync(LocalJournalFixture.Draft with { CuttingType = "Лазер" }, Token);
        await model.ConfirmThicknessChangesCommand.ExecuteAsync(null);
        Assert.True(model.HasError); Assert.False(model.RequiresReload); Assert.True(model.IsEditing);
        Assert.True(PriceTableChange.Matches(original, await store.ReadAsync(PriceTables.Laser, Token)));
        await model.SaveCommand.ExecuteAsync(null); Assert.Equal(2, Assert.Single(model.ThicknessChanges).OrderCount);
        var history = await store.ReadAsync(Token);
        await model.ConfirmThicknessChangesCommand.ExecuteAsync(null);
        Assert.False(model.HasError, model.Error); Assert.False(model.IsEditing); Assert.Equal(history, await store.ReadAsync(Token));
        Assert.Contains((await store.ReadAsync(PriceTables.Laser, Token)).Rates, rate => rate.ThicknessMillimeters == 2.8m);
    }

    [AvaloniaFact]
    public async Task CopyInsteadOfRenamingKeepsTheOldTariffAndNewUnsavedThicknessCanBeCorrected()
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        var row = model.Rows.Single(row => row.Thickness == "2,5"); row.ThicknessValue = "2,8"; row.Value = "45";
        await model.SaveCommand.ExecuteAsync(null); model.KeepOriginalThicknessesCommand.Execute(null);
        Assert.False(model.IsConfirmingThickness); var added = Assert.Single(model.Rows, row => row.IsNew);
        Assert.Equal("2,8", added.ThicknessValue); added.ThicknessValue = "2,9";
        await model.SaveCommand.ExecuteAsync(null); Assert.False(model.HasError, model.Error);
        var saved = await store.ReadAsync(PriceTables.Laser, Token);
        Assert.Equal(25, saved.Rates.Single(rate => rate.ThicknessMillimeters == 2.5m).RublesPerMeter);
        Assert.Equal(45, saved.Rates.Single(rate => rate.ThicknessMillimeters == 2.9m).RublesPerMeter);
        Assert.DoesNotContain(saved.Rates, rate => rate.ThicknessMillimeters == 2.8m);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LostAcknowledgementReconcilesTheRenamedColumnWithoutRepeatingTheRename(bool addedBeforeFailure, bool editAfterFailure)
    {
        var store = new DemoOrderStore(); var editor = new UncertainEditor(store.Prices);
        var model = new PriceEditorViewModel(store.Prices, editor, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        if (addedBeforeFailure)
        {
            model.BeginAddRateCommand.Execute(null); model.NewThickness = "2,8"; model.NewRate = "25"; model.StageRateCommand.Execute(null);
        }
        else model.Rows.Single(row => row.Thickness == "2,5").ThicknessValue = "2,8";
        await model.SaveCommand.ExecuteAsync(null); await model.ConfirmThicknessChangesCommand.ExecuteAsync(null);
        Assert.True(model.RequiresReload); Assert.Equal(1, editor.Writes);
        if (editAfterFailure) { var row = model.Rows.Single(row => addedBeforeFailure ? row.IsNew : row.Thickness == "2,5"); row.ThicknessValue = "2,9"; row.Value = "71"; }
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.Equal(editAfterFailure, model.HasChanges); Assert.Equal(editAfterFailure, model.CanSave);
        Assert.Contains(model.Rows, row => row.Thickness == "2,8"); Assert.Equal(1, editor.Writes);
        if (editAfterFailure)
        {
            var rebased = model.Rows.Single(row => row.Thickness == "2,8"); Assert.Equal("2,9", rebased.ThicknessValue); Assert.Equal("71", rebased.Value);
            await model.SaveCommand.ExecuteAsync(null); await model.ConfirmThicknessChangesCommand.ExecuteAsync(null);
            Assert.False(model.HasError, model.Error); Assert.Equal(2, editor.Writes);
            Assert.Equal(71, (await store.ReadAsync(PriceTables.Laser, Token)).Rates.Single(rate => rate.ThicknessMillimeters == 2.9m).RublesPerMeter);
        }
    }

    [Fact]
    public async Task ProtocolValidatesTheReadOnlyUsageReplyAndDoesNotRenumberExistingOperations()
    {
        Assert.Equal(12, (int)AccessHostOperation.UpdatePriceTable); Assert.Equal(15, (int)AccessHostOperation.PrepareAnnotations);
        Assert.Equal(16, (int)AccessHostOperation.ReadPriceUsage);
        var change = Rename(LocalJournalFixture.Prices[1]);
        Assert.Equal(4, Assert.Single(await PriceEditingTests.Host(new(1, PriceUsage: [new("2,5", 4)])).ReadThicknessUsageAsync(change, Token)).OrderCount);
        foreach (var response in new AccessHostResponse[] { new(1), new(1, PriceUsage: [new("wrong", 4)]), new(1, PriceUsage: [new("2,5", -1)]),
            new(1, PriceUsage: [new("2,5", 1)], Orders: []), new(2, PriceUsage: [new("2,5", 1)]) })
            await Assert.ThrowsAsync<OrderReadException>(() => PriceEditingTests.Host(response).ReadThicknessUsageAsync(change, Token));
    }

    [AvaloniaFact]
    public async Task RefreshKeepsTheRenamingDraftWithoutOverwritingAnotherUsersPrice()
    {
        var store = new DemoOrderStore(); var model = new PriceEditorViewModel(store.Prices, store.Prices, "Демо", "");
        await model.RefreshCommand.ExecuteAsync(null); model.BeginEditCommand.Execute(null);
        model.Rows.Single(row => row.Thickness == "2,5").ThicknessValue = "2,8";
        var original = await store.ReadAsync(PriceTables.Laser, Token);
        await store.SaveAsync(new(original, PriceEditingTests.Replace(original, "2.5", 81)), Token);
        await model.SaveCommand.ExecuteAsync(null); Assert.True(model.RequiresReload);
        await model.RefreshCommand.ExecuteAsync(null);
        var row = model.Rows.Single(row => row.Thickness == "2,5");
        Assert.Equal("2,8", row.ThicknessValue); Assert.Equal("81", row.Value); Assert.Equal("81", row.CurrentPrice);
        await model.SaveCommand.ExecuteAsync(null); await model.ConfirmThicknessChangesCommand.ExecuteAsync(null);
        Assert.False(model.HasError, model.Error);
        Assert.Equal(81, (await store.ReadAsync(PriceTables.Laser, Token)).Rates.Single(rate => rate.ThicknessMillimeters == 2.8m).RublesPerMeter);
    }

    private sealed class UncertainEditor(IPriceEditor inner) : IPriceEditor
    {
        public int Writes;
        public async Task<PriceTable> SaveAsync(PriceTableChange change, CancellationToken cancellationToken = default)
        { Writes++; var saved = await inner.SaveAsync(change, cancellationToken); if (Writes == 1) throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Обновите прайс."); return saved; }
    }
}
