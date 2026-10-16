namespace Jornal.Core.Orders;

// A read model, not the complete persisted order or a recalculated quote.
public sealed record OrderSummary(
    int Id,
    DateOnly? OrderDate,
    string SpecificationNumber,
    string ManagerName,
    string CustomerName,
    string CuttingType,
    string MaterialName,
    decimal? ThicknessMillimeters,
    decimal? StoredCuttingPrice,
    int? Color)
{
    public OrderCuttingTime? CuttingTime { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public long? CuttingDurationMinutes =>
        CuttingTime is { } time && time.Matches(this) ? time.Minutes : null;
    [System.Text.Json.Serialization.JsonIgnore] public bool CuttingTimeNeedsReview => CuttingTime is not null && CuttingDurationMinutes is null;
    public string SheetSize { get; init; } = string.Empty;
    public string SheetCount { get; init; } = string.Empty;
    public int? CutLengthMeters { get; init; }
    public string WeightKilograms { get; init; } = string.Empty;
    public string? DeliveryNumber { get; init; }
    public decimal? Coefficient { get; init; }
    public string MaterialId { get; init; } = string.Empty;
    public string CuttingGas { get; init; } = string.Empty;
    // Исходные значения нужны для диагностики и будущего редактирования без нормализации при чтении.
    public string? StoredCoefficient { get; init; }
    public DateTime? StoredOrderDateTime { get; init; }
    public int? PartCount { get; init; }
    public int? PunchCount { get; init; }
}
