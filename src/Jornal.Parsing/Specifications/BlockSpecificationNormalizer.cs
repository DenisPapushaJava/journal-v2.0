using System.Globalization;
using Jornal.Core.Pricing;

namespace Jornal.Parsing.Specifications;

public sealed record NormalizedBlockSpecification(DateOnly OrderDate, string SpecificationNumber, string CuttingType,
    string Material, decimal ThicknessMillimeters, string SheetSize, string SheetCount, int CutLengthMeters,
    string WeightKilograms, int PartCount, int PunchCount, decimal Coefficient);

public static class BlockSpecificationNormalizer
{
    public static NormalizedBlockSpecification Normalize(SpecificationParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (parsed.Profile != BlockHtmlSpecificationParser.Profile || !parsed.IsComplete)
            throw new ArgumentException("Спецификация неполна или содержит неоднозначные поля. Проверьте сообщения разбора.", nameof(parsed));
        var dimensions = parsed.Dimensions!.Value;
        var header = parsed.Header!.Value;
        return new(header.OrderDate, header.JournalNumber, parsed.CuttingType!.Value, parsed.Material!.Value,
            dimensions.ThicknessMillimeters, Number(dimensions.WidthMillimeters) + "x" + Number(dimensions.LengthMillimeters),
            Number(parsed.SheetCount!.Value), LegacyCutLengthMeters(parsed.CutLengthMillimeters!.Value),
            Number(parsed.WeightKilograms!.Value), parsed.PartCount!.Value, parsed.PunchCount!.Value,
            CuttingPriceCalculator.CalculatePunchCoefficient(parsed.PartCount.Value, parsed.PunchCount.Value));
    }

    public static int LegacyCutLengthMeters(decimal millimeters)
    {
        if (millimeters < 0 || decimal.Truncate(millimeters) / 1000 >= int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(millimeters), "Длина реза выходит за допустимый диапазон.");
        // Совместимость БЛОК: это намеренно не Ceiling. Точное кратное 1000 тоже получает дополнительный метр.
        return checked((int)(decimal.Truncate(millimeters) / 1000) + 1);
    }

    private static string Number(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
