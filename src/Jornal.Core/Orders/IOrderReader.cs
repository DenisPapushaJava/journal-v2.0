namespace Jornal.Core.Orders;

public interface IOrderReader
{
    Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default);
}
