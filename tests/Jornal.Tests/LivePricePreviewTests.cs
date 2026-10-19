using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Desktop.Controls;
using Jornal.Desktop.Views;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class LivePricePreviewTests
{
    [AvaloniaFact]
    public async Task BatchCardsShareThePriceSnapshotAndRemovingACardDoesNotCancelTheOthers()
    {
        var clock = new Clock(); var store = new Store();
        var form = new NewOrderViewModel(store, priceTimeProvider: clock) { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        try
        {
            await form.ImportFilesAsync(["one.html", "two.html"], TestContext.Current.CancellationToken);
            foreach (var item in form.BatchItems)
            { item.Form.Thickness.Value = "2,5"; item.Form.Coefficient.Value = "1,25"; item.Form.CutLength.Value = "31"; }
            await form.WaitForThicknessOptionsAsync(); clock.Advance(300); Pump();
            Assert.Equal(1, store.Reads);
            Assert.All(form.BatchItems, item => Assert.Equal(659m, item.Form.BuildDraft().ExpectedQuote!.StoredPrice));
            form.RemoveBatchItem(form.BatchItems[0]);
            var remaining = Assert.Single(form.BatchItems).Form;
            remaining.CutLength.Value = "32"; clock.Advance(300); Pump();
            Assert.Equal(680m, remaining.BuildDraft().ExpectedQuote!.StoredPrice); Assert.Equal(1, store.Reads);
            await form.CalculatePriceCommand.ExecuteAsync(null);
            Assert.Equal(2, store.Reads); Assert.Equal(680m, remaining.BuildDraft().ExpectedQuote!.StoredPrice);
        }
        finally { form.StopThicknessOptions(); Assert.Equal(0, clock.ActiveTimers); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PriceAndOptionalRefreshRemainVisibleInBothFormsWithoutTheCalculateFooter(bool dark)
    {
        var clock = new Clock(); var store = new Store(); var form = New(store, clock);
        var window = new NewOrderWindow { DataContext = form, Width = 1060, Height = 660, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); await form.ThicknessOptions.WaitForLoadAsync(); clock.Advance(300); Pump();
            Check(window, form.PriceText, $"live-price-new-{dark}");
        }
        finally { window.Close(); Assert.Equal(0, clock.ActiveTimers); }
        var edit = new EditOrderViewModel(store.Current, store, "Тест", _ => Task.FromResult<OrderRecord?>(store.Current), clock);
        var editor = new EditOrderWindow { DataContext = edit, Width = 1060, Height = 660, RequestedThemeVariant = window.RequestedThemeVariant };
        try
        {
            editor.Show(); await edit.ThicknessOptions.WaitForLoadAsync();
            edit.CutLength.Value = "32"; clock.Advance(300); Pump();
            Check(editor, edit.PriceText, $"live-price-edit-{dark}");
            Assert.True(edit.ShowStoredPrice);
        }
        finally { editor.Close(); Assert.Equal(0, clock.ActiveTimers); }
    }

    private static void Check(Window window, string price, string screenshot)
    {
        Assert.NotEqual("—", price);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.IsEffectivelyVisible && block.Text == price);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<JournalButton>(), button => button.Label == "Рассчитать");
        var refresh = window.FindControl<JournalButton>("CalculatePriceButton")!;
        Assert.True(refresh.IsEffectivelyVisible); Assert.True(refresh.IconOnly);
        var point = refresh.TranslatePoint(default, window)!.Value;
        Assert.InRange(point.Y, 0, window.ClientSize.Height - refresh.Bounds.Height);
        if (Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1)); bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, screenshot + ".png"), new PngBitmapEncoderOptions());
    }

    [AvaloniaFact]
    public async Task TypingUsesOneCachedPriceTableAndOnlyTheLatestCompleteInputs()
    {
        var clock = new Clock(); var store = new Store(); var form = New(store, clock);
        try
        {
            await form.RefreshThicknessOptionsAsync();
            clock.Advance(300); Pump();
            Assert.Equal(659m, form.BuildDraft().ExpectedQuote!.StoredPrice); Assert.Equal(1, store.Reads);
            form.CutLength.Value = "3"; clock.Advance(200);
            form.CutLength.Value = "32"; clock.Advance(299); Pump(); Assert.Equal("—", form.PriceText);
            clock.Advance(1); Pump();
            Assert.Equal(680m, form.BuildDraft().ExpectedQuote!.StoredPrice); Assert.Equal(1, store.Reads);
            form.Coefficient.Value = "1,"; clock.Advance(300); Pump();
            form.Coefficient.Value = "1,5"; clock.Advance(300); Pump();
            Assert.Equal(816m, form.BuildDraft().ExpectedQuote!.StoredPrice); Assert.Equal(1, store.Reads);
            form.CutLength.Value = ""; clock.Advance(300); Pump();
            Assert.Equal("—", form.PriceText); Assert.False(form.HasPriceError); Assert.False(form.CutLength.HasError);
            Assert.Null(form.BuildDraft().ExpectedQuote);
            form.CutLength.Value = "31"; clock.Advance(300); Pump(); Assert.NotNull(form.BuildDraft().ExpectedQuote);
            var price = form.PriceText;
            form.Manager.Value = "Другой"; form.Customer.Value = "Новый"; form.Weight.Value = "999";
            form.Annotations.Comment = "Комментарий"; form.Annotations.HasBending = true;
            clock.Advance(500); Pump();
            Assert.Equal(price, form.PriceText); Assert.Equal(1, store.Reads); Assert.Equal(0, clock.ActiveTimers);
        }
        finally { form.StopThicknessOptions(); }
    }

    [AvaloniaFact]
    public async Task EditorPreservesHistoricalPriceUntilPricingInputsOrExplicitRefreshChangeIt()
    {
        var clock = new Clock(); var store = new Store(); var original = store.Current;
        var form = new EditOrderViewModel(original, store, "Тест", _ => Task.FromResult<OrderRecord?>(store.Current), clock);
        try
        {
            await form.RefreshThicknessOptionsAsync(); clock.Advance(300); Pump();
            Assert.False(form.HasChanges); Assert.False(form.CanSave); Assert.Null(form.BuildChange().ExpectedQuote);
            Assert.Equal(form.StoredPriceText, form.PriceText);
            form.Customer.Value = "Исправлено"; form.Annotations.Comment = "Текст";
            clock.Advance(300); Pump();
            Assert.Null(form.BuildChange().ExpectedQuote); Assert.Equal(1, store.Reads);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Equal(original.Price, store.Current.Price); Assert.Equal(1, store.Reads); Assert.Equal(1, store.Writes);
        }
        finally { form.StopThicknessOptions(); }
        form = new EditOrderViewModel(store.Current, store, "Тест", _ => Task.FromResult<OrderRecord?>(store.Current), clock);
        try
        {
            await form.RefreshThicknessOptionsAsync();
            form.CutLength.Value = "32"; clock.Advance(300); Pump();
            Assert.Equal(680m, form.BuildChange().ExpectedQuote!.StoredPrice);
            Assert.Contains(form.Changes, change => change.Label == "Стоимость"); Assert.True(form.ShowStoredPrice);
            form.CutLength.Value = "31"; clock.Advance(300); Pump();
            Assert.False(form.HasChanges); Assert.Null(form.BuildChange().ExpectedQuote);
            Assert.Equal(form.StoredPriceText, form.PriceText);
            await form.CalculateCommand.ExecuteAsync(null);
            Assert.True(form.HasChanges); Assert.Equal(659m, form.BuildChange().ExpectedQuote!.StoredPrice);
            await form.SaveCommand.ExecuteAsync(null); Assert.Equal(659m, store.Current.Price);
        }
        finally { form.StopThicknessOptions(); }
    }

    [AvaloniaFact]
    public async Task ClosingCancelsTimersAndRejectsCallbacksAlreadyQueuedOnTheUiThread()
    {
        var clock = new Clock(); var store = new Store(); var form = New(store, clock);
        await form.RefreshThicknessOptionsAsync();
        form.CutLength.Value = "32"; var price = form.PriceText;
        Assert.True(clock.ActiveTimers > 0);
        clock.Advance(300); // callback queued, but not yet applied
        form.StopThicknessOptions(); Pump();
        Assert.Equal(price, form.PriceText); Assert.Equal(0, clock.ActiveTimers);
        clock.Advance(1000); Pump(); Assert.Equal(price, form.PriceText);
    }

    [AvaloniaFact]
    public async Task SaveBeforeTheDebounceFinishesStillChecksTheDisplayedPriceAndNeverRetriesAWrite()
    {
        var clock = new Clock(); var store = new Store(); var form = New(store, clock);
        try
        {
            await form.RefreshThicknessOptionsAsync(); clock.Advance(300); Pump();
            store.Rate = 20; // colleague changed tariff after our preview
            await form.SaveCommand.ExecuteAsync(null);
            Assert.False(form.IsSaved); Assert.Equal(0, store.Writes); Assert.True(form.HasSaveError);
            Assert.Equal(775m, form.BuildDraft().ExpectedQuote!.StoredPrice);
            Assert.Equal(0, clock.ActiveTimers);
            clock.Advance(1000); Pump(); Assert.Equal(775m, form.BuildDraft().ExpectedQuote!.StoredPrice);
            await form.SaveCommand.ExecuteAsync(null); Assert.True(form.IsSaved); Assert.Equal(1, store.Writes);
            await form.SaveCommand.ExecuteAsync(null); Assert.Equal(1, store.Writes);
        }
        finally { form.StopThicknessOptions(); }
        form = New(new Store(), clock);
        try
        {
            await form.RefreshThicknessOptionsAsync(); form.CutLength.Value = "32";
            await form.SaveCommand.ExecuteAsync(null);
            Assert.True(form.IsSaved); Assert.Equal(680m, form.CreatedOrder!.StoredCuttingPrice);
        }
        finally { form.StopThicknessOptions(); }
    }

    [AvaloniaFact]
    public async Task ReplacingASpecificationStillChecksThePriceWhenThePreviewCouldNotLoad()
    {
        var store = new Store { FailReads = true };
        var form = new EditOrderViewModel(store.Current, store, "Тест", _ => Task.FromResult<OrderRecord?>(store.Current))
        { ImportReader = new BatchImportTests.Reader(await BatchImportTests.Preview()) };
        try
        {
            await form.RequestImportAsync(["replacement.html"], TestContext.Current.CancellationToken);
            form.ReplaceImportCommand.Execute(null);
            form.Thickness.Value = "2,5"; form.CutLength.Value = "31"; form.Coefficient.Value = "1,25";
            await form.RefreshThicknessOptionsAsync();
            Assert.False(form.ThicknessOptions.IsVerified);
            Assert.False(form.BuildChange().RequiresPriceCalculation);
            var reads = store.Reads;
            store.FailReads = false;
            await form.SaveCommand.ExecuteAsync(null);
            Assert.True(store.Reads > reads);
            Assert.Equal(659m, form.SavedRecord!.Price);
            Assert.Equal(1, store.Writes);
        }
        finally { form.StopThicknessOptions(); }
    }

    [AvaloniaFact]
    public async Task MetadataOnlyWritesKeepCurrencyNullsAndWorkWithoutAnAvailableTariff()
    {
        foreach (var price in new decimal?[] { 1234.5678m, null, 0m })
        {
            var store = new Store { Rate = null }; var original = store.Current with { Price = price };
            store.Current = original;
            var form = new EditOrderViewModel(original, store, "Тест", _ => Task.FromResult<OrderRecord?>(store.Current));
            try
            {
                await form.RefreshThicknessOptionsAsync(); Assert.True(form.ThicknessOptions.HasUnavailableValue);
                await form.CalculateCommand.ExecuteAsync(null); // Failed optional refresh must not block metadata edits.
                var reads = store.Reads;
                form.Annotations.Comment = "Правка";
                Assert.True(form.CanSave);
                await form.SaveCommand.ExecuteAsync(null);
                Assert.False(form.HasError, form.Error); Assert.Equal(price, store.Current.Price); Assert.Equal(reads, store.Reads);
            }
            finally { form.StopThicknessOptions(); }
        }
    }

    [Fact]
    public async Task HostRejectsAnUnexpectedRepricingOfAMetadataOnlyUpdate()
    {
        var original = OrderMaintenanceTests.Record(); var change = new OrderEditChange(original, original with { Customer = "Новый" });
        Assert.False(change.RequiresPriceCalculation);
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => PriceEditingTests.Host(new(1, Record: change.Values with { Price = 1 }, MaintenanceCompleted: true))
            .UpdateAsync(change, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, failure.Failure);
    }

    private static NewOrderViewModel New(Store store, Clock clock)
    {
        var form = new NewOrderViewModel(store, priceTimeProvider: clock);
        form.OrderDate.Value = "02.01.2026"; form.SpecificationNumber.Value = "0102-900";
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Заказчик"; form.CuttingType.Value = "Лазер";
        form.Material.Value = "Ст3"; form.Thickness.Value = "2,5"; form.CutLength.Value = "31"; form.Coefficient.Value = "1,25";
        form.SheetSize.Value = "1500x3000"; form.SheetCount.Value = "1"; form.Weight.Value = "10";
        return form;
    }
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private sealed class Store : IManualOrderCreator, IManualOrderBatchCreator, IOrderMaintenance
    {
        public OrderRecord Current = OrderMaintenanceTests.Record();
        public int? Rate = 17;
        public bool FailReads;
        public int Reads, Writes;
        private PriceTable Table(string name) => new(name, true, [new(2.5m, "2,5", Rate)]);
        public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
        { Reads++; return FailReads ? Task.FromException<PriceTable>(new PriceReadException("Прайс временно недоступен.")) : Task.FromResult(Table(tableName)); }
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        {
            Writes++;
            return Task.FromResult(draft.ToOrder(44, draft.Calculate(Table(CuttingPriceCalculator.SelectTable(draft.PriceRequest))).StoredPrice));
        }
        public Task<IReadOnlyList<OrderSummary>> CreateBatchAsync(IReadOnlyList<ManualOrderDraft> drafts, CancellationToken cancellationToken = default)
        {
            var rows = drafts.Select((draft, index) => draft.ToOrder(44 + index,
                draft.Calculate(Table(CuttingPriceCalculator.SelectTable(draft.PriceRequest))).StoredPrice)).ToArray();
            Writes++; return Task.FromResult<IReadOnlyList<OrderSummary>>(rows);
        }
        public Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken cancellationToken = default)
        {
            Writes++; Assert.Equal(Current, change.Expected);
            Current = change.Calculate(change.RequiresPriceCalculation ? Table(CuttingPriceCalculator.SelectTable(change.PriceRequest)) : null);
            return Task.FromResult(Current);
        }
        public Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<OrderRecord?>(Current);
        public Task DeleteAsync(OrderRecord expected, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OrderRecord> RestoreAsync(OrderRecord archived, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Clock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private long _milliseconds;
        public int ActiveTimers => _timers.Count;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { var timer = new Timer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer; }
        public void Advance(int milliseconds)
        {
            _milliseconds += milliseconds;
            foreach (var timer in _timers.Where(timer => timer.Due <= _milliseconds).ToArray()) timer.Fire();
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            public long Due = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock._milliseconds + (long)dueTime.TotalMilliseconds; return true; }
            public void Fire() { Due = long.MaxValue; callback(state); }
            public void Dispose() => clock._timers.Remove(this);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
