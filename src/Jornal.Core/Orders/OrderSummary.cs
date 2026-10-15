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
    decimal StoredCuttingPrice,
    int Color);
