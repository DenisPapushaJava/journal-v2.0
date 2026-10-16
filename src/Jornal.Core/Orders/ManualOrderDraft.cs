using System.Globalization;
using System.Text.Json.Serialization;
using Jornal.Core.Pricing;

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
    string CuttingGas)
{
    public int InitialColor { get; init; } = OrderColors.None;
    public int? PartCount { get; init; }
    public int? PunchCount { get; init; }
    public CuttingPriceQuote? ExpectedQuote { get; init; }
    [JsonIgnore] public CuttingPriceRequest PriceRequest => new(CuttingType, ThicknessMillimeters, CutLengthMeters, Coefficient, MaterialId);
    [JsonIgnore] public string StoredCoefficient => Coefficient.ToString(CultureInfo.InvariantCulture) + "|" +
        (PartCount?.ToString(CultureInfo.InvariantCulture) ?? "-1") + "|" + (PunchCount?.ToString(CultureInfo.InvariantCulture) ?? "-1") + "|" +
        (string.IsNullOrEmpty(MaterialId) ? "-1" : MaterialId) + "|" + (string.IsNullOrEmpty(CuttingGas) ? "-1" : CuttingGas);

    public void Validate()
    {
        var required = new[] { SpecificationNumber, ManagerName, CustomerName, MaterialName, SheetSize, SheetCount, WeightKilograms };
        if (required.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl)) ||
            OrderDate.Year < 100 || MaterialName.Any(char.IsWhiteSpace) || MaterialId is null || CuttingGas is null ||
            MaterialId.Length > 255 || CuttingGas.Length > 255 ||
            MaterialId.Any(c => char.IsControl(c) || c == '|') || CuttingGas.Any(c => char.IsControl(c) || c == '|') ||
            StoredCoefficient.Length > 255 || PartCount is < 1 || PunchCount is < 0)
            throw new OrderWriteException(OrderWriteFailure.NotSaved, "Проверьте обязательные поля, дату и формат ID/газа. Текстовые поля MDB — не более 255 символов.");
        _ = CuttingPriceCalculator.SelectTable(PriceRequest);
    }

    public CuttingPriceQuote Calculate(PriceTable table)
    {
        var quote = CuttingPriceCalculator.Calculate(PriceRequest, table);
        if (ExpectedQuote is not null && ExpectedQuote != quote) throw new OrderPriceChangedException(quote);
        return quote;
    }

    public OrderSummary ToOrder(int id, decimal? price) => new(id, OrderDate, SpecificationNumber, ManagerName,
        CustomerName, CuttingType, MaterialName, ThicknessMillimeters, price, InitialColor)
    {
        SheetSize = SheetSize, SheetCount = SheetCount, CutLengthMeters = CutLengthMeters, WeightKilograms = WeightKilograms,
        Coefficient = Coefficient, PartCount = PartCount, PunchCount = PunchCount,
        MaterialId = MaterialId == "-1" ? "" : MaterialId, CuttingGas = CuttingGas == "-1" ? "" : CuttingGas,
        StoredOrderDateTime = OrderDate.ToDateTime(TimeOnly.MinValue), StoredCoefficient = StoredCoefficient
    };
}

public sealed class OrderPriceChangedException(CuttingPriceQuote quote) : Exception(
    "Прайс изменился после предварительного расчёта. Заявка не добавлена. Проверьте новую цену и нажмите «Добавить заявку» ещё раз.")
{
    public CuttingPriceQuote Quote { get; } = quote;
}
