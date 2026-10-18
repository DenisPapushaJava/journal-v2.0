using System.Runtime.ExceptionServices;
using Avalonia.Headless.XUnit;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class MissingTariffTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task WaterjetBatchShowsTheAffectedCardWithoutThrowingOrSavingAndCanBeRecalculated(bool calculateFirst, bool nullRate)
    {
        using var fixture = new LocalJournalFixture();
        var (store, machine, form) = await BatchAsync(fixture, nullRate);
        var fields = form.BatchItems.Select(item => item.Form.Fields.Select(field => field.Value).ToArray()).ToArray();
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        var savedEvents = 0;
        form.Saved += (_, _) => savedEvents++;
        var thrown = 0;
        void OnException(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception is PriceCalculationException { Failure: PriceCalculationFailure.PriceUnavailable } &&
                e.Exception.Message.StartsWith(machine.Name, StringComparison.Ordinal)) Interlocked.Increment(ref thrown);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            if (calculateFirst) await form.CalculatePriceCommand.ExecuteAsync(null);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Equal(0, thrown);
            Assert.False(form.IsSaved); Assert.False(form.RequiresReload);
            Assert.Equal(0, savedEvents); Assert.Empty(form.CreatedOrders);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
            Assert.Equal(LocalJournalFixture.Orders.Length, (await store.ReadAsync(Token)).Count);
            Assert.True(form.BatchItems[0].IsReady);
            var failed = form.BatchItems[1];
            Assert.False(failed.IsReady); Assert.Equal("Проверьте толщину", failed.Status);
            Assert.True(failed.Form.ThicknessOptions.HasUnavailableValue);
            Assert.Contains("80 мм", failed.Form.Thickness.ChoiceError);
            Assert.True(failed.Form.ThicknessOptions.CanOpenPrices);
            Assert.Equal(failed.Form.Thickness.ChoiceError, failed.Details);
            if (calculateFirst) { Assert.True(failed.Form.HasPriceValidationError); Assert.Contains(machine.Name, failed.Form.PriceError); }
            Assert.Equal("Готово: 1 из 2", form.BatchSummary);
            Assert.False(form.SaveCommand.CanExecute(null)); Assert.True(form.CalculatePriceCommand.CanExecute(null));
            Assert.Null(form.SaveError); // Известная недоступная толщина блокирует кнопку ещё до попытки записи.
            for (var index = 0; index < form.BatchItems.Count; index++)
            {
                Assert.Equal(fields[index], form.BatchItems[index].Form.Fields.Select(field => field.Value));
                Assert.True(form.BatchItems[index].Form.Annotations.HasBending);
                Assert.Equal("Сохранить ввод", form.BatchItems[index].Form.Annotations.Comment);
            }
            var oldPrice = await store.ReadAsync(machine.TableName, Token);
            var rates = nullRate ? oldPrice.Rates.Select(rate => rate.ThicknessMillimeters == 80m ? rate with { RublesPerMeter = 800 } : rate).ToArray() :
                [.. oldPrice.Rates, new(80m, "80", 800)];
            await store.SaveAsync(new(oldPrice, oldPrice with { Rates = rates }), Token);
            await form.CalculatePriceCommand.ExecuteAsync(null);
            Assert.All(form.BatchItems, item => { Assert.False(item.Form.HasPriceError); Assert.True(item.IsReady); });
            Assert.Null(form.SaveError); Assert.True(form.SaveCommand.CanExecute(null));
            await form.SaveCommand.ExecuteAsync(null);
            Assert.True(form.IsSaved); Assert.Equal(1, savedEvents);
            Assert.Equal(new[] { 43, 44 }, form.CreatedOrders.Select(row => row.Id));
            Assert.All(form.CreatedOrders, row => { Assert.True(row.HasBending); Assert.Equal("Сохранить ввод", row.Comment); });
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
    }

    [Fact]
    public async Task ExplicitExclusionOfMissingTariffCardAllowsTheOtherCardAndClearsOnlyThePriceNotice()
    {
        using var fixture = new LocalJournalFixture();
        var (store, _, form) = await BatchAsync(fixture);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.BatchItems[1].Form.ThicknessOptions.HasUnavailableValue);
        form.BatchItems[1].IsIncluded = false;
        Assert.Null(form.SaveError); Assert.True(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(70m, Assert.Single(form.CreatedOrders).ThicknessMillimeters);
        Assert.Equal(3, (await store.ReadAsync(Token)).Count);
        Assert.True(form.BatchItems[1].Form.ThicknessOptions.HasUnavailableValue);
    }

    [Fact]
    public async Task SingleOrderAndEditorKeepInputAndDataOnMissingTariffAndAcceptARealZeroTariff()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        var form = ManualOrderTests.ValidForm(store); form.Thickness.Value = "6";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.IsSaved); Assert.True(form.HasPriceError);
        Assert.Contains("HansLaser", form.PriceError); Assert.Contains("6 мм", form.PriceError);
        Assert.False(form.SaveCommand.CanExecute(null)); Assert.True(form.CalculatePriceCommand.CanExecute(null));
        var original = (await store.ReadRecordAsync(42, Token))!;
        var edit = new EditOrderViewModel(original, store, fixture.Path, token => store.ReadRecordAsync(42, token));
        edit.Thickness.Value = "6"; edit.Annotations.Comment = "Правка сохранена в форме";
        await edit.SaveCommand.ExecuteAsync(null);
        Assert.Null(edit.SavedRecord); Assert.Contains("6 мм", edit.Error);
        Assert.Equal("Правка сохранена в форме", edit.Annotations.Comment);
        await edit.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("6 мм", edit.Error); Assert.Equal("Не рассчитана", edit.PriceText);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        form.Thickness.Value = "10";
        Assert.False(form.HasPriceError); Assert.True(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(0m, form.CreatedOrder!.StoredCuttingPrice);
    }

    [AvaloniaFact]
    public async Task UnavailablePriceSourceBlocksTheWholeBatchAndCanBeRetriedWithoutRepeatedReads()
    {
        using var fixture = new LocalJournalFixture();
        TrackingCreator? creator = null;
        var (store, _, form) = await BatchAsync(fixture, decorate: source => creator = new(source));
        form.BatchItems[1].Form.Thickness.Value = "70";
        creator!.PriceReads = 0; creator.MachineReads = 0; creator.ReadFailure = true;
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.IsSaved); Assert.False(form.RequiresReload); Assert.Equal(0, creator.Writes);
        Assert.Equal(1, creator.PriceReads); Assert.Equal(1, creator.MachineReads);
        Assert.All(form.BatchItems, item => { Assert.True(item.Form.HasPriceError); Assert.False(item.Form.HasPriceValidationError); Assert.False(item.IsReady); });
        Assert.True(form.SaveCommand.CanExecute(null)); // После технического отказа можно повторить проверку кнопкой добавления.
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        creator.ReadFailure = false;
        await form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.Null(form.SaveError); Assert.True(form.SaveCommand.CanExecute(null));
        creator.PriceReads = 0; creator.MachineReads = 0;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(1, creator.Writes);
        Assert.Equal(1, creator.PriceReads); Assert.Equal(1, creator.MachineReads);
        Assert.Equal(4, (await store.ReadAsync(Token)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransactionStillRejectsChangedOrRemovedTariffAfterSuccessfulPreflight(bool removed)
    {
        using var fixture = new LocalJournalFixture();
        TrackingCreator? creator = null;
        var (store, machine, form) = await BatchAsync(fixture, decorate: source => creator = new(source));
        form.BatchItems[1].Form.Thickness.Value = "2,5";
        await form.CalculatePriceCommand.ExecuteAsync(null);
        var before = await store.ReadAsync(Token);
        creator!.BeforeBatchWrite = async () =>
        {
            var table = await store.ReadAsync(machine.TableName, Token);
            await store.SaveAsync(new(table, table with { Rates = table.Rates.Select(rate => rate.ThicknessMillimeters == 70m ?
                rate with { RublesPerMeter = removed ? null : 601 } : rate).ToArray() }), Token);
        };
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.IsSaved); Assert.False(form.RequiresReload); Assert.True(form.HasSaveError);
        Assert.Equal(before, await store.ReadAsync(Token));
        Assert.Contains(removed ? "70 мм" : "Прайс изменился", form.SaveError);
        creator.BeforeBatchWrite = null;
        if (removed)
        {
            var table = await store.ReadAsync(machine.TableName, Token);
            await store.SaveAsync(new(table, table with { Rates = table.Rates.Select(rate => rate.ThicknessMillimeters == 70m ? rate with { RublesPerMeter = 600 } : rate).ToArray() }), Token);
            await form.CalculatePriceCommand.ExecuteAsync(null);
        }
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(2, form.CreatedOrders.Count);
    }

    internal static async Task<(LocalJournalStore Store, CuttingMachine Machine, NewOrderViewModel Form)> BatchAsync(LocalJournalFixture fixture,
        bool nullRate = false, Func<LocalJournalStore, IManualOrderCreator>? decorate = null)
    {
        await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var machine = MachineCatalogTests.Machine("ГАР " + Guid.NewGuid().ToString("N")[..8]);
        var creation = MachineCatalogTests.Creation(machine);
        if (nullRate) creation = creation with { Rates = [.. creation.Rates, new(80m, "80", 800)] };
        await store.CreateMachineAsync(creation, Token);
        if (nullRate)
        {
            var original = await store.ReadAsync(machine.TableName, Token);
            await store.SaveAsync(new(original, original with { Rates = original.Rates.Select(rate => rate.ThicknessMillimeters == 80m ? rate with { RublesPerMeter = null } : rate).ToArray() }), Token);
        }
        var form = new NewOrderViewModel(decorate?.Invoke(store) ?? store, isLocalCopy: true)
        { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        await form.ImportFilesAsync(["valid.pdf", "missing-tariff.pdf"], Token);
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Заказчик";
        foreach (var item in form.BatchItems)
        {
            item.Form.CuttingType.Value = machine.Name; item.Form.Thickness.Value = "70"; item.Form.Coefficient.Value = "1";
            item.Form.Annotations.HasBending = true; item.Form.Annotations.Comment = "Сохранить ввод";
        }
        form.BatchItems[1].Form.Thickness.Value = "80";
        form.BatchItems[1].Form.SpecificationNumber.Value = "0514-0079";
        await form.WaitForThicknessOptionsAsync(Token);
        return (store, machine, form);
    }

    private sealed class TrackingCreator(LocalJournalStore store) : IManualOrderCreator, IManualOrderBatchCreator, IPriceReader, IMachineCatalogReader
    {
        public int PriceReads, MachineReads, Writes;
        public bool ReadFailure;
        public Func<Task>? BeforeBatchWrite;
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default)
        {
            PriceReads++;
            return ReadFailure ? Task.FromException<PriceTable>(new PriceReadException("Прайс недоступен.")) : store.ReadAsync(name, cancellationToken);
        }
        public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default)
        { MachineReads++; return store.ReadMachinesAsync(cancellationToken); }
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        { Writes++; return store.CreateAsync(draft, cancellationToken); }
        public async Task<IReadOnlyList<OrderSummary>> CreateBatchAsync(IReadOnlyList<ManualOrderDraft> drafts, CancellationToken cancellationToken = default)
        {
            Writes++;
            if (BeforeBatchWrite is not null) await BeforeBatchWrite();
            return await store.CreateBatchAsync(drafts, cancellationToken);
        }
    }
}
