namespace Jornal.Core.Orders;

public static class OrderSearch
{
    public static IEnumerable<OrderSummary> Filter(IEnumerable<OrderSummary> orders, string? searchText)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var query = searchText?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return orders;
        }

        return orders.Where(order => Matches(order, query));
    }

    public static bool Matches(OrderSummary order, string query) =>
        order.SpecificationNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        order.ManagerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        order.CustomerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        order.MaterialName.Contains(query, StringComparison.OrdinalIgnoreCase);
}
