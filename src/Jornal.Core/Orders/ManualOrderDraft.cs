namespace Jornal.Core.Orders;

// Ручной ввод до расчёта. Текстовые поля Access сохраняют своё представление.
public sealed record ManualOrderDraft(
    DateOnly OrderDate,
    string SpecificationNumber,
    string ManagerName,
    string CustomerName,
    string CuttingType,
    string MaterialName,
    decimal ThicknessMillimeters,
    string SheetSize,
    string SheetCount,
    int CutLengthMeters,
    string WeightKilograms,
    decimal Coefficient,
    string MaterialId,
    string CuttingGas);
