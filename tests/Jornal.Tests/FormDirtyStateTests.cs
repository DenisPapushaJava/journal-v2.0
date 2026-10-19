using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Views;
using Xunit;

namespace Jornal.Tests;

public sealed class FormDirtyStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedEditorDoesNotReadPricesOrWriteEvenWhenCommandIsCalledDirectly(bool missingParameters)
    {
        var store = new Maintenance();
        if (missingParameters) store.Current = store.Current with { Thickness = null, CutLength = null, Coefficient = null };
        var before = store.Current;
        var form = Edit(store);
        Assert.False(form.HasChanges);
        Assert.False(form.CanSave);
        Assert.False(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, store.PriceReads);
        Assert.Equal(0, store.Updates);
        Assert.Equal(before, store.Current);
        Assert.Null(form.SavedRecord);
        Assert.False(form.HasError);
    }

    [Fact]
    public async Task RevertingEveryFieldClearsDirtyStateAndNotifiesTheSaveCommand()
    {
        var store = new Maintenance();
        var form = Edit(store);
        var notifications = 0;
        form.SaveCommand.CanExecuteChanged += (_, _) => notifications++;
        foreach (var field in form.Fields)
        {
            var before = field.Value;
            field.Value = before + "1";
            Assert.True(form.HasChanges, field.Label);
            Assert.True(form.CanSave, field.Label);
            Assert.True(form.SaveCommand.CanExecute(null), field.Label);
            field.Value = before;
            Assert.False(form.HasChanges, field.Label);
            Assert.False(form.CanSave, field.Label);
            await form.SaveCommand.ExecuteAsync(null);
        }
        Assert.True(notifications >= form.Fields.Count * 2);
        Assert.Equal(0, store.PriceReads);
        Assert.Equal(0, store.Updates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BendingAndCommentCanBeChangedAndRevertedWithoutWriting(bool withAnnotations)
    {
        var store = new Maintenance();
        if (withAnnotations) store.Current = store.Current with { Annotations = OrderAnnotations.For(store.Current.ToSummary(), true, "Пометка") };
        var form = Edit(store);
        var bending = form.Annotations.HasBending;
        var comment = form.Annotations.Comment;
        form.Annotations.HasBending = !bending;
        Assert.True(form.CanSave);
        form.Annotations.HasBending = bending;
        Assert.False(form.CanSave);
        form.Annotations.Comment = comment + "Правка";
        Assert.True(form.CanSave);
        form.Annotations.Comment = comment;
        Assert.False(form.CanSave);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, store.PriceReads);
        Assert.Equal(0, store.Updates);
    }

    [Fact]
    public async Task MetadataSavePreservesHistoricalPriceWithoutReadingTariffs()
    {
        var store = new Maintenance();
        var before = store.Current;
        var form = Edit(store);
        form.Customer.Value = "Исправлено";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.HasError, form.Error);
        Assert.Equal(before with { Customer = "Исправлено" }, form.SavedRecord);
        Assert.Equal(0, store.PriceReads);
        Assert.Equal(1, store.Updates);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(0, store.PriceReads);
        Assert.Equal(1, store.Updates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitCalculationOnlyEnablesSaveWhenPriceChanges(bool samePrice)
    {
        var store = new Maintenance();
        if (samePrice) store.Current = store.Current with { Price = 659m };
        var form = Edit(store);
        Assert.True(form.CanCalculate);
        await form.CalculateCommand.ExecuteAsync(null);
        Assert.Equal(!samePrice, form.HasChanges);
        Assert.Equal(!samePrice, form.CanSave);
        if (!samePrice) Assert.Equal("Стоимость", Assert.Single(form.Changes).Label);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(samePrice ? 0 : 1, store.Updates);
        Assert.Equal(samePrice ? 1 : 2, store.PriceReads);
    }

    [Fact]
    public async Task ConfirmingStaleAnnotationsIsARealChangeEvenWithoutEditingTheirText()
    {
        var store = new Maintenance();
        store.Current = store.Current with { Annotations = OrderAnnotations.For(store.Current.ToSummary(), true, "Пометка"), Specification = "Новый номер" };
        var form = Edit(store);
        Assert.True(form.Annotations.NeedsReview);
        Assert.False(form.CanSave);
        form.Annotations.ConfirmReviewCommand.Execute(null);
        Assert.True(form.HasChanges);
        Assert.True(form.CanSave);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, store.Updates);
        Assert.True(store.Current.Annotations!.Matches(store.Current.ToSummary()));
    }

    [AvaloniaFact]
    public async Task EditorButtonsReactToChangesWithoutDisablingExplicitCalculation()
    {
        var store = new Maintenance();
        var form = Edit(store);
        var window = new EditOrderWindow { DataContext = form };
        try
        {
            window.Show();
            await form.ThicknessOptions.WaitForLoadAsync();
            Dispatcher.UIThread.RunJobs();
            var save = window.FindControl<JournalButton>("SaveButton")!;
            var calculate = window.FindControl<JournalButton>("CalculatePriceButton")!;
            Assert.False(save.IsEnabled);
            Assert.True(calculate.IsEnabled);
            var original = form.Customer.Value;
            form.Customer.Value = "Правка";
            Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled);
            form.Customer.Value = original;
            Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
            Assert.True(calculate.IsEnabled);
            var reads = store.PriceReads;
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Equal(reads, store.PriceReads);
            Assert.Equal(0, store.Updates);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task EmptyNewOrderNeverCallsTheCreatorAndUneditedPreparedOrderCreatesOnlyOnce()
    {
        var creator = new Creator();
        var empty = new NewOrderViewModel(creator);
        await empty.SaveCommand.ExecuteAsync(null);
        Assert.True(empty.HasValidationErrors);
        Assert.Equal(0, creator.Writes);
        Assert.Equal(0, creator.PriceReads);
        var prepared = ManualOrderTests.ValidForm(creator);
        await prepared.SaveCommand.ExecuteAsync(null);
        Assert.True(prepared.IsSaved);
        await prepared.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, creator.Writes);
        Assert.Equal(1, creator.PriceReads);
    }

    private static EditOrderViewModel Edit(Maintenance store) => new(store.Current, store, "Тест", _ => Task.FromResult<OrderRecord?>(store.Current));

    private sealed class Creator : IManualOrderCreator, IPriceReader
    {
        public int Writes;
        public int PriceReads;
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default)
        { PriceReads++; return Task.FromResult(new PriceTable(name, true, [new(2.5m, "2,5", 17)])); }
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        { Writes++; return Task.FromResult(draft.ToOrder(50, null)); }
    }

    private sealed class Maintenance : IOrderMaintenance
    {
        public OrderRecord Current = OrderMaintenanceTests.Record();
        public int PriceReads;
        public int Updates;
        private static PriceTable Table(string name) => new(name, true, [new(2.5m, "2,5", 17)]);
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default)
        { PriceReads++; return Task.FromResult(Table(name)); }
        public Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<OrderRecord?>(Current);
        public Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken cancellationToken = default)
        {
            Updates++;
            Assert.Equal(Current, change.Expected);
            Current = change.Calculate(Table(CuttingPriceCalculator.SelectTable(change.PriceRequest)));
            return Task.FromResult(Current);
        }
        public Task DeleteAsync(OrderRecord expected, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderRecord> RestoreAsync(OrderRecord archived, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
