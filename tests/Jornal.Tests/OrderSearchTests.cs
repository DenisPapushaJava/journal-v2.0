using Jornal.Core.Orders;
using Jornal.Infrastructure.Demo;
using Xunit;

namespace Jornal.Tests;

public sealed class OrderSearchTests
{
    [Theory]
    [InlineData("[А]", 1)]
    [InlineData("5083h111", 2)]
    [InlineData("  1014-1003  ", 3)]
    public async Task FindsLiteralTextRegardlessOfCase(string query, int expectedId)
    {
        var orders = await new DemoOrderReader().ReadAsync(TestContext.Current.CancellationToken);
        var result = OrderSearch.Filter(orders, query);

        Assert.Equal(expectedId, Assert.Single(result).Id);
    }

    [Fact]
    public async Task FilteringKeepsTheStoredPrice()
    {
        var orders = await new DemoOrderReader().ReadAsync(TestContext.Current.CancellationToken);
        var original = orders[0] with { StoredCuttingPrice = 123.45m };

        var result = Assert.Single(OrderSearch.Filter([original], "менеджер"));

        Assert.Same(original, result);
        Assert.Equal(123.45m, result.StoredCuttingPrice);
    }
}
