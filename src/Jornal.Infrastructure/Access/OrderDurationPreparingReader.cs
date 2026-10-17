using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

// Только рабочее подключение UI инициирует миграцию; обычный IOrderReader остаётся read-only.
public sealed class OrderDurationPreparingReader(IOrderReader reader, IOrderDurationInitializer initializer) : IOrderReader
{
    private bool _prepared;
    public string? PreparationWarning { get; private set; }
    public async Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var rows = await reader.ReadAsync(cancellationToken);
        if (_prepared) return rows;
        try { await initializer.PrepareCuttingTimeAsync(cancellationToken); _prepared = true; PreparationWarning = null; }
        catch (OrderWriteException exception) { PreparationWarning = exception.Message; }
        return rows;
    }
}
