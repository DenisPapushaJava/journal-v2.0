namespace Jornal.Core.Pricing;

public sealed record CuttingPriceRequest(string CuttingType, decimal ThicknessMillimeters,
    int CutLengthMeters, decimal Coefficient, string? MaterialId = null);

public sealed record CuttingPriceQuote(string TableName, string ThicknessColumn, int RublesPerMeter,
    int CutLengthMeters, decimal Coefficient, decimal StoredPrice);

public enum PriceCalculationFailure
{
    InvalidParameters, UnsupportedCuttingType, ThicknessOutOfRange, PriceUnavailable, InvalidPrice, AmountOutOfRange
}

public sealed class PriceCalculationException(PriceCalculationFailure failure, string message) : Exception(message)
{
    public PriceCalculationFailure Failure { get; } = failure;
}

public static class CuttingPriceCalculator
{
    public static string SelectTable(CuttingPriceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ThicknessMillimeters <= 0 || request.CutLengthMeters < 0 || request.Coefficient <= 0)
            throw new PriceCalculationException(PriceCalculationFailure.InvalidParameters,
                "Толщина и коэффициент должны быть больше нуля, длина — неотрицательным целым числом метров.");
        var (table, minimum, maximum) = request.CuttingType switch
        {
            "Лазер" => (PriceTables.Laser, 0m, 20m),
            "Плазма" => (PriceTables.Plasma, 2m, 32m),
            "Газо-кислородная" => (PriceTables.GasOxygen, 16m, 200m),
            "HansLaser" => (request.MaterialId switch
            {
                "1" => PriceTables.HansLaserId1, "2" => PriceTables.HansLaserId2, _ => PriceTables.Laser
            }, 0m, 60m),
            _ => throw new PriceCalculationException(PriceCalculationFailure.UnsupportedCuttingType, "Неизвестный вид резки.")
        };
        if (request.ThicknessMillimeters < minimum || request.ThicknessMillimeters > maximum)
            throw new PriceCalculationException(PriceCalculationFailure.ThicknessOutOfRange,
                "Толщина находится вне диапазона выбранного вида резки.");
        return table;
    }

    public static CuttingPriceQuote Calculate(CuttingPriceRequest request, PriceTable table)
    {
        var selected = SelectTable(request);
        table.Validate();
        if (table.Name != selected)
            throw new ArgumentException("Передан прайс другого вида резки или ID материала.", nameof(table));
        var rate = table.Rates.SingleOrDefault(rate => rate.ThicknessMillimeters == request.ThicknessMillimeters);
        if (!table.HasRow || rate?.RublesPerMeter is null)
            throw new PriceCalculationException(PriceCalculationFailure.PriceUnavailable,
                "В первой строке выбранного прайса нет тарифа для этой толщины. Цена не рассчитана.");
        if (rate.RublesPerMeter < 0)
            throw new PriceCalculationException(PriceCalculationFailure.InvalidPrice, "Тариф в базе отрицательный. Проверьте прайс.");
        // Совместимость с NewOrd/Edit: Double, прежний порядок умножения и ToEven.
        // Переход к decimal-арифметике изменил бы некоторые цены на границе округления.
        var rounded = Math.Round((double)rate.RublesPerMeter.Value * request.CutLengthMeters * (double)request.Coefficient,
            MidpointRounding.ToEven);
        if (!double.IsFinite(rounded) || rounded > 922337203685477d)
            throw new PriceCalculationException(PriceCalculationFailure.AmountOutOfRange, "Стоимость превышает диапазон Currency в Access.");
        // Минимум 500 ₽ применяется только при подготовке результата, не здесь.
        return new(table.Name, rate.ColumnName, rate.RublesPerMeter.Value, request.CutLengthMeters,
            request.Coefficient, (decimal)rounded);
    }

    public static decimal CalculatePunchCoefficient(int partCount, int punchCount)
    {
        if (partCount <= 0 || punchCount < 0)
            throw new PriceCalculationException(PriceCalculationFailure.InvalidParameters,
                "Для расчёта коэффициента нужно положительное количество деталей и неотрицательное количество пробивок.");
        var ratio = Math.Round((double)punchCount / partCount, MidpointRounding.ToEven);
        return ratio switch
        {
            <= 1 => 1m, <= 10 => 1m + ((decimal)ratio - 1m) / 10m,
            <= 15 => 2m, <= 20 => 2.1m, <= 30 => 2.2m, <= 40 => 2.3m,
            <= 50 => 2.4m, <= 60 => 2.5m, <= 70 => 2.6m, <= 80 => 2.7m,
            <= 90 => 2.8m, <= 100 => 2.9m, _ => 3m
        };
    }
}

public sealed class CuttingPriceService(IPriceReader reader)
{
    public async Task<CuttingPriceQuote> CalculateAsync(CuttingPriceRequest request, CancellationToken cancellationToken = default)
    {
        var name = CuttingPriceCalculator.SelectTable(request);
        cancellationToken.ThrowIfCancellationRequested();
        var table = await reader.ReadAsync(name, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return CuttingPriceCalculator.Calculate(request, table);
    }
}
