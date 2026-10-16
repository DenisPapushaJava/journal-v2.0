using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.DataSources;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ManualMdbCreationTests
{
    [Fact]
    public async Task ReviewedPriceChangeKeepsAllFieldsAndRequiresAnotherExplicitSave()
    {
        var creator = new PricedCreator(); var form = Filled(creator);
        await form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.Equal("977 ₽", form.PriceText);
        Assert.Contains("17 ₽/м", form.PriceBreakdown);
        creator.Rate = 1;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.IsSaved); Assert.True(form.HasSaveError); Assert.Equal(0, creator.Created);
        Assert.Equal("57 ₽", form.PriceText); Assert.Equal("Клиент 'А'", form.Customer.Value);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(1, creator.Created); Assert.Equal(57m, form.CreatedOrder!.StoredCuttingPrice);
    }

    [Fact]
    public async Task PricingInputResetsThePreviewWhileCustomerChangesKeepIt()
    {
        var creator = new PricedCreator(); var form = Filled(creator);
        await form.CalculatePriceCommand.ExecuteAsync(null);
        form.Customer.Value = "Другой клиент"; Assert.Equal("977 ₽", form.PriceText);
        form.Thickness.Value = "4"; Assert.Equal("Не рассчитана", form.PriceText);
        await form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.True(form.HasPriceError); Assert.Equal(0, creator.Created);
        form.Thickness.Value = "2,5";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(977m, form.CreatedOrder!.StoredCuttingPrice);
    }

    [Fact]
    public async Task UnknownCreationBlocksFurtherWritesUntilReloadAndRestoresTheForm()
    {
        var creator = new PricedCreator { Fail = OrderWriteFailure.OutcomeUnknown };
        var settings = new OrderSourceTests.MemorySettings { Loaded = new(new(DatabasePath: OrderSourceTests.DatabasePath)) };
        var reader = new DemoOrderStore();
        var manager = new OrderSourceManager(settings, _ => reader, _ => reader, _ => creator);
        var model = OrderSourceTests.Model(manager);
        await model.ReloadCommand.ExecuteAsync(null);
        var form = model.CreateOrderForm()!; Fill(form);
        Assert.True(form.IsDatabase); Assert.Contains("MDB", form.WindowTitle); Assert.True(form.HasPricing);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.RequiresReload); Assert.True(model.RequiresReload);
        Assert.False(form.SaveCommand.CanExecute(null)); Assert.False(model.CanCreateOrder);
        Assert.Null(model.CreateOrderForm()); Assert.False(model.IsUpdatingOrders);
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload);
        var restored = model.CreateOrderForm()!;
        Assert.Same(form, restored); Assert.Equal("Клиент 'А'", restored.Customer.Value);
        Assert.True(restored.SaveCommand.CanExecute(null)); Assert.Equal(0, creator.Created);
        creator.Fail = null;
        await restored.SaveCommand.ExecuteAsync(null);
        Assert.True(restored.IsSaved); Assert.Equal(1, creator.Created);
        model.AcceptCreatedOrder(restored.CreatedOrder!);
        Assert.Contains("сохранено в MDB", model.UpdatedText);
        Assert.NotSame(restored, model.CreateOrderForm());
    }

    [Fact]
    public async Task ConfirmedFailureKeepsTheFormRetryableAndReleasesTheMainWindow()
    {
        var creator = new PricedCreator { Fail = OrderWriteFailure.NotSaved };
        var model = new MainWindowViewModel(new DemoOrderStore(), creator);
        var form = model.CreateOrderForm()!; Fill(form);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.False(model.IsUpdatingOrders);
        Assert.True(form.SaveCommand.CanExecute(null)); Assert.Equal("Клиент 'А'", form.Customer.Value);
        creator.Fail = null; await form.SaveCommand.ExecuteAsync(null); Assert.True(form.IsSaved);
    }

    [Fact]
    public async Task DisplayFailureAfterCreationRequiresReloadAndDoesNotSaveAgain()
    {
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store);
        await model.ReloadCommand.ExecuteAsync(null);
        var form = model.CreateOrderForm()!; Fill(form);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved);
        System.Collections.Specialized.NotifyCollectionChangedEventHandler fail = (_, _) => throw new IOException("Display unavailable");
        model.Orders.CollectionChanged += fail;
        try { model.AcceptCreatedOrder(form.CreatedOrder!); }
        finally { model.Orders.CollectionChanged -= fail; }
        Assert.True(model.RequiresReload); Assert.Contains("сохранена", model.ErrorMessage);
        await model.ReloadCommand.ExecuteAsync(null);
        Assert.False(model.RequiresReload); Assert.Equal(4, model.Orders.Count);
        Assert.Single(model.Orders, row => row.Id == form.CreatedOrder!.Id);
    }

    [Theory]
    [InlineData("2026-01-02", 255)]
    [InlineData("0001-01-01", 3)]
    public void DraftRejectsUnrepresentableAccessValues(string date, int idLength)
    {
        var draft = Draft() with { OrderDate = DateOnly.Parse(date), MaterialId = new string('a', idLength), CuttingGas = "Газ" };
        Assert.Throws<OrderWriteException>(draft.Validate);
    }

    internal static NewOrderViewModel Filled(IManualOrderCreator creator)
    { var form = new NewOrderViewModel(creator, isDatabase: true); Fill(form); return form; }
    internal static void Fill(NewOrderViewModel form)
    {
        form.OrderDate.Value = "02.01.2026"; form.SpecificationNumber.Value = "0102--44";
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Клиент 'А'"; form.CuttingType.Value = "Лазер";
        form.Material.Value = "5083H111"; form.Thickness.Value = "2,5"; form.SheetSize.Value = "1500x3000";
        form.SheetCount.Value = "02 / комплект"; form.Weight.Value = "0012,350";
        form.CutLength.Value = "25"; form.Coefficient.Value = "2,3";
    }
    private static ManualOrderDraft Draft() => new(new(2026, 1, 2), "0102--44", "Имя", "Клиент", "Лазер", "5083H111", 2.5m,
        "1500x3000", "2", 25, "38,5", 2.3m, "", "");

    internal sealed class PricedCreator : IManualOrderCreator, IPriceReader
    {
        public int Rate { get; set; } = 17;
        public int Created { get; private set; }
        public OrderWriteFailure? Fail { get; set; }
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PriceTable(name, true, [new(2.5m, "2,5", Rate)]));
        public async Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        {
            if (Fail is { } failure) throw new OrderWriteException(failure, "Ошибка сохранения");
            draft.Validate(); var quote = draft.Calculate(await ReadAsync(CuttingPriceCalculator.SelectTable(draft.PriceRequest), cancellationToken));
            Created++; return draft.ToOrder(100 + Created, quote.StoredPrice);
        }
    }
}
