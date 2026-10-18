using Jornal.Core.Orders;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderSearchTests
{
    [Theory]
    [InlineData("1018-009285", OrderSearchField.Specification, true)]
    [InlineData("009285", OrderSearchField.Specification, false)]
    [InlineData("1018-9285", OrderSearchField.Specification, false)]
    [InlineData("009285", OrderSearchField.DeliveryNumber, true)]
    [InlineData(" 009285 ", OrderSearchField.DeliveryNumber, true)]
    [InlineData("9285", OrderSearchField.DeliveryNumber, false)]
    [InlineData("АННА иванова", OrderSearchField.Manager, true)]
    [InlineData("Иванова Анна", OrderSearchField.Manager, false)]
    [InlineData("Анна", OrderSearchField.Manager, false)]
    [InlineData("ООО  Альфа", OrderSearchField.Customer, true)]
    [InlineData("ООО Альфа", OrderSearchField.Customer, false)]
    [InlineData("09г2с", OrderSearchField.Material, true)]
    [InlineData("09-Г2С", OrderSearchField.Material, false)]
    [InlineData("контроль кромки", OrderSearchField.Comment, true)]
    [InlineData("кромки", OrderSearchField.Comment, false)]
    [InlineData("   ", OrderSearchField.Comment, true)]
    public void ExactSearchComparesWholeFieldPreservingZerosPunctuationAndInternalSpaces(string text, OrderSearchField field, bool expected)
    {
        var row = new OrderSummary(1, new(2026, 10, 18), "1018-009285", "Анна Иванова", "ООО  Альфа", "ГАР", "09Г2С", 2.5m, 100m, null)
        { DeliveryNumber = " 009285 " };
        row = row with { Annotations = OrderAnnotations.For(row, false, "Контроль кромки") };
        Assert.Equal(expected, new OrderSearchQuery(text, field, exactMatch: true).Matches(row));
    }

    [Theory]
    [InlineData(OrderSearchField.All)]
    [InlineData((OrderSearchField)999)]
    public void ExactModeIsUnavailableWithoutAValidSpecificField(OrderSearchField field)
    {
        var query = new OrderSearchQuery("9285", field, exactMatch: true);
        var row = new OrderSummary(1, null, "1018-19285", "", "", "", "", null, null, null);
        Assert.False(query.IsExactMatch); Assert.True(query.Matches(row));
        var scoped = new OrderSearchQuery("9285", OrderSearchField.DeliveryNumber, exactMatch: true);
        Assert.False(scoped.Matches(row));
    }

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
