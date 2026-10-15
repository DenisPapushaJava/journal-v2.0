namespace Jornal.Core.Orders;

/// <summary>
/// Обновляет только указанное поле выбранных заявок. При успехе возвращает все затронутые записи,
/// при ошибке не изменяет ни одной. Реальный адаптер обязан обеспечить атомарность операции.
/// </summary>
public interface IOrderBatchEditor
{
    Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<int> ids, int color,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<int> ids, string number,
        CancellationToken cancellationToken = default);
}

public static class OrderColors
{
    public const int Work = -882036;
    public const int Ready = -10238087;
    public const int Shipped = -8154114;
    public const int None = -1;
}
