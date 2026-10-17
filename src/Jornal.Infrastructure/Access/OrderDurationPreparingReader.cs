using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

// Только рабочее подключение UI инициирует миграцию; обычный IOrderReader остаётся read-only.
public sealed class OrderDurationPreparingReader(IOrderReader reader, IOrderDurationInitializer initializer, IOrderAnnotationsInitializer? annotations = null) : IOrderReader
{
    private bool _prepared;
    private bool _annotationsPrepared;
    public string? PreparationWarning { get; private set; }
    public async Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var rows = await reader.ReadAsync(cancellationToken);
        var warnings = new List<string>();
        if (!_prepared)
        {
            try { await initializer.PrepareCuttingTimeAsync(cancellationToken); _prepared = true; }
            catch (OrderWriteException exception) { warnings.Add(exception.Message); }
        }
        if (annotations is not null && !_annotationsPrepared)
        {
            try { await annotations.PrepareAnnotationsAsync(cancellationToken); _annotationsPrepared = true; }
            catch (OrderWriteException exception) { warnings.Add(exception.Message); }
        }
        if (rows.Any(row => row.AnnotationsUnavailable)) warnings.Add(OrderAnnotations.UnavailableMessage);
        PreparationWarning = warnings.Count > 0 ? string.Join("\n", warnings) : null;
        return rows;
    }
}
