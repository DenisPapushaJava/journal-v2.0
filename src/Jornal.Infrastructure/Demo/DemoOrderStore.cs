using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Demo;

public sealed class DemoOrderStore : IOrderReader, IManualOrderCreator, IOrderBatchEditor
{
    private readonly object _gate = new();
    // Только память текущего сеанса. Рабочая MDB здесь никогда не открывается.
    private readonly List<OrderSummary> _orders =
        [
            new(1, new DateOnly(2026, 10, 15), "1015-1001", "Менеджер 1", "Пример [А]",
                "Лазер", "Ст-3", 2.5m, 1250m, -882036)
                { SheetSize = "1250x2500", SheetCount = "2", CutLengthMeters = 48, WeightKilograms = "38.5", DeliveryNumber = "БЛЦБ-00010001" },
            new(2, new DateOnly(2026, 10, 15), "1015-1002", "Менеджер 2", "Пример Б",
                "HansLaser", "5083H111", 6m, 4850m, -10238087)
                { SheetSize = "1500x3000", SheetCount = "3", CutLengthMeters = 127, WeightKilograms = "218.4", DeliveryNumber = "БЛЦБ-00010002" },
            new(3, new DateOnly(2026, 10, 14), "1014-1003", "Менеджер 1", "Пример В",
                "Плазма", "09Г2С", 16m, 3250m, -1)
                { SheetSize = "1500x6000", SheetCount = "1", CutLengthMeters = 35, WeightKilograms = "156.0" }
        ];

    public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<OrderSummary>>(_orders.ToArray());
        }
    }

    public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var order = new OrderSummary(_orders.Max(row => row.Id) + 1, draft.OrderDate,
                draft.SpecificationNumber, draft.ManagerName, draft.CustomerName, draft.CuttingType,
                draft.MaterialName, draft.ThicknessMillimeters, null, -1)
            {
                SheetSize = draft.SheetSize, SheetCount = draft.SheetCount,
                CutLengthMeters = draft.CutLengthMeters, WeightKilograms = draft.WeightKilograms,
                Coefficient = draft.Coefficient, MaterialId = draft.MaterialId, CuttingGas = draft.CuttingGas
            };
            _orders.Insert(0, order);
            return Task.FromResult(order);
        }
    }

    public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color,
        CancellationToken cancellationToken = default) => Update(OrderBatchChange.ForColor(expected, color), cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number,
        CancellationToken cancellationToken = default) => Update(OrderBatchChange.ForDeliveryNumber(expected, number), cancellationToken);

    private Task<IReadOnlyList<OrderSummary>> Update(OrderBatchChange change, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unique = change.Expected.Select(row => row.Id).ToHashSet();
            var matches = _orders.Select((order, index) => (order, index)).Where(pair => unique.Contains(pair.order.Id)).ToArray();
            var current = matches.ToDictionary(pair => pair.order.Id, pair => pair.order);
            if (matches.Length != unique.Count || change.Expected.Any(expected =>
                !change.Matches(current[expected.Id], expected)))
                throw OrderWriteException.Conflict();
            // Сначала проверяем и подготавливаем весь пакет, затем заменяем записи без промежуточных await.
            var prepared = matches.Select(pair => (Order: change.Apply(pair.order), Index: pair.index)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var pair in prepared) _orders[pair.Index] = pair.Order;
            return Task.FromResult<IReadOnlyList<OrderSummary>>(prepared.Select(pair => pair.Order).ToArray());
        }
    }
}
