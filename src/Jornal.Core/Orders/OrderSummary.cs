namespace Jornal.Core.Orders;

// A read model, not the complete persisted order or a recalculated quote.
public sealed record OrderSummary(
    int Id,
    DateOnly OrderDate,
    string SpecificationNumber,
    string ManagerName,
    string CustomerName,
    string CuttingType,
    string MaterialName,
    decimal ThicknessMillimeters,
    decimal? StoredCuttingPrice,
    int Color)
{
    public string SheetSize { get; init; } = string.Empty;
    public string SheetCount { get; init; } = string.Empty;
    public int? CutLengthMeters { get; init; }
    public string WeightKilograms { get; init; } = string.Empty;
    public string DeliveryNumber { get; init; } = string.Empty;
    public decimal? Coefficient { get; init; }
    public string MaterialId { get; init; } = string.Empty;
    public string CuttingGas { get; init; } = string.Empty;
}
