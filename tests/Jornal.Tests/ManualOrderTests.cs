using System.Globalization;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class ManualOrderTests
{
    [Theory]
    [InlineData("ru-RU", "2,5", "1.25")]
    [InlineData("en-US", "2.5", "1,25")]
    public async Task CreationRetainsManualFieldsAndDoesNotInventPrice(string culture, string thickness, string coefficient)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var store = new DemoOrderStore();
            var initial = await store.ReadAsync(TestContext.Current.CancellationToken);
            var form = ValidForm(store);
            form.Thickness.Value = thickness;
            form.Coefficient.Value = coefficient;
            await form.SaveCommand.ExecuteAsync(null);
            await form.SaveCommand.ExecuteAsync(null);
            Assert.False(form.SaveCommand.CanExecute(null));
            var rows = await store.ReadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(4, rows.Count);
            var added = rows.First();
            Assert.Equal("Амг3-5083H111", added.MaterialName);
            Assert.Equal("1015--1004", added.SpecificationNumber);
            Assert.Equal(new DateOnly(2026, 10, 15), added.OrderDate);
            Assert.Equal("Заказчик 'Б'", added.CustomerName);
            Assert.Equal(2.5m, added.ThicknessMillimeters);
            Assert.Equal(1.25m, added.Coefficient);
            Assert.Equal("1500x3000", added.SheetSize);
            Assert.Equal("3", added.SheetCount);
            Assert.Equal("38,5", added.WeightKilograms);
            Assert.Equal(48, added.CutLengthMeters);
            Assert.Equal("2", added.MaterialId);
            Assert.Equal("N2", added.CuttingGas);
            Assert.Null(added.StoredCuttingPrice);
            Assert.Equal("Не рассчитана", new OrderRowViewModel(added).StoredPriceText);
            Assert.Contains("0,00", new OrderRowViewModel(added with { StoredCuttingPrice = 0 }).StoredPriceText);
            Assert.All(initial, row => Assert.Equal(row, rows.Single(saved => saved.Id == row.Id)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task ValidationKeepsInputAndRechecksDependentFields()
    {
        var store = new DemoOrderStore();
        var form = ValidForm(store);
        form.OrderDate.Value = "31.02.2026";
        form.Material.Value = "Амг 3";
        form.CutLength.Value = "1,5";
        form.Coefficient.Value = "NaN";
        form.Thickness.Value = "1,000.5";
        form.Manager.Value = new string('я', 256);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.HasValidationErrors);
        Assert.True(form.OrderDate.HasError);
        Assert.True(form.Material.HasError);
        Assert.True(form.CutLength.HasError);
        Assert.True(form.Coefficient.HasError);
        Assert.True(form.Thickness.HasError);
        Assert.True(form.Manager.HasError);
        Assert.Equal("Амг 3", form.Material.Value);
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);

        form.Thickness.Value = "1,5";
        form.CuttingType.Value = "Плазма";
        Assert.True(form.Thickness.HasError);
        form.CuttingType.Value = "Лазер";
        Assert.False(form.Thickness.HasError);
        Assert.Equal("Заказчик 'Б'", form.Customer.Value);
    }

    [Theory]
    [InlineData("Лазер", "20", true)]
    [InlineData("Лазер", "20,1", false)]
    [InlineData("Плазма", "2", true)]
    [InlineData("Плазма", "33", false)]
    [InlineData("Газо-кислородная", "16", true)]
    [InlineData("Газо-кислородная", "200", true)]
    [InlineData("Газо-кислородная", "15", false)]
    [InlineData("HansLaser", "60", true)]
    [InlineData("HansLaser", "61", false)]
    [InlineData("HansLaser", "0", false)]
    public async Task ThicknessKeepsLegacyMachineRanges(string machine, string thickness, bool valid)
    {
        var form = ValidForm(new DemoOrderStore());
        form.CuttingType.Value = machine;
        form.Thickness.Value = thickness;
        await form.SaveCommand.ExecuteAsync(null);
        Assert.Equal(valid, form.IsSaved);
        Assert.Equal(!valid, form.Thickness.HasError);
    }

    [Fact]
    public async Task SaveFailureAllowsRetryWithoutLosingData()
    {
        var store = new DemoOrderStore();
        var creator = new FailingCreator(store);
        var form = ValidForm(creator);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.HasSaveError);
        Assert.False(form.IsSaved);
        Assert.Equal("1015--1004", form.SpecificationNumber.Value);
        Assert.True(form.SaveCommand.CanExecute(null));
        await form.SaveCommand.ExecuteAsync(null);
        Assert.False(form.HasSaveError);
        Assert.True(form.IsSaved);
        Assert.Equal(4, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
    }

    internal static NewOrderViewModel ValidForm(IManualOrderCreator creator)
    {
        var form = new NewOrderViewModel(creator);
        form.OrderDate.Value = "15.10.2026";
        form.SpecificationNumber.Value = "1015--1004";
        form.Manager.Value = "Менеджер 3";
        form.Customer.Value = "Заказчик 'Б'";
        form.CuttingType.Value = "HansLaser";
        form.Material.Value = "Амг3-5083H111";
        form.Thickness.Value = "2,5";
        form.SheetSize.Value = "1500x3000";
        form.SheetCount.Value = "3";
        form.Weight.Value = "38,5";
        form.CutLength.Value = "48";
        form.Coefficient.Value = "1,25";
        form.MaterialId.Value = "2";
        form.Gas.Value = "N2";
        return form;
    }

    private sealed class FailingCreator(DemoOrderStore store) : IManualOrderCreator
    {
        private bool _fail = true;
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        {
            if (!_fail) return store.CreateAsync(draft, cancellationToken);
            _fail = false;
            throw new IOException("Simulated failure");
        }
    }
}
