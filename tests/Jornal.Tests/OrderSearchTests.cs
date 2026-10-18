using Jornal.Core.Orders;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderSearchTests
{
    [Theory]
    [InlineData("  ВаХрЕеВа\t9285 ", OrderSearchField.All, true)]
    [InlineData("Вахреева 9285", OrderSearchField.Manager, false)]
    [InlineData("9285", OrderSearchField.DeliveryNumber, true)]
    [InlineData("9285", OrderSearchField.Specification, false)]
    [InlineData("1018-859", OrderSearchField.Specification, true)]
    [InlineData("МАСТЭНЕРГО", OrderSearchField.Customer, true)]
    [InlineData("09г2с", OrderSearchField.Material, true)]
    [InlineData("контроль кромки", OrderSearchField.Comment, true)]
    [InlineData("ГАР", OrderSearchField.All, true)]
    [InlineData("2,5", OrderSearchField.All, true)]
    [InlineData("2.5", OrderSearchField.All, true)]
    [InlineData("9?85", OrderSearchField.All, false)]
    [InlineData("контроль отсутствует", OrderSearchField.All, false)]
    public void PreparedQueryMatchesLiteralWordsAndOptionalField(string text, OrderSearchField field, bool expected)
    {
        var row = new OrderSummary(1, new(2026, 10, 18), "1018-859", "Вахреева", "ООО МАСТЭНЕРГО", "ГАР", "09Г2С", 2.5m, 100m, null)
        { DeliveryNumber = "БЛЦБ-9285" };
        row = row with { Annotations = OrderAnnotations.For(row, false, "Нужен контроль кромки") };
        Assert.Equal(expected, new OrderSearchQuery(text, field).Matches(row));
    }

    [Theory]
    [InlineData("[А]", 1)]
    [InlineData("5083h111", 2)]
    [InlineData("  1014-1003  ", 3)]
    public async Task FindsLiteralTextRegardlessOfCase(string query, int expectedId)
    {
        var orders = await new DemoOrderStore().ReadAsync(TestContext.Current.CancellationToken);
        var result = OrderSearch.Filter(orders, query);

        Assert.Equal(expectedId, Assert.Single(result).Id);
    }

    [Fact]
    public async Task FilteringKeepsTheStoredPrice()
    {
        var orders = await new DemoOrderStore().ReadAsync(TestContext.Current.CancellationToken);
        var original = orders[0] with { StoredCuttingPrice = 123.45m };

        var result = Assert.Single(OrderSearch.Filter([original], "менеджер"));

        Assert.Same(original, result);
        Assert.Equal(123.45m, result.StoredCuttingPrice);
    }
}
