using Jornal.Core.Orders;
using System.Text.Json.Serialization;

namespace Jornal.Core.Pricing;

public interface IPriceEditor
{
    Task<PriceTable> SaveAsync(PriceTableChange change, CancellationToken cancellationToken = default);
}

// Старые колонки сохраняются в прежнем порядке. Новые тарифы добавляются в конец.
public sealed record PriceTableChange(PriceTable Expected, PriceTable Values)
{
    [JsonIgnore] public bool HasAdditions => Values.Rates.Count > Expected.Rates.Count;
    [JsonIgnore] public IEnumerable<PriceRate> AddedRates => Values.Rates.Skip(Expected.Rates.Count);

    public static string ColumnName(decimal thickness) => thickness.ToString("0.############################",
        System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    public PriceTableChange Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Expected);
        ArgumentNullException.ThrowIfNull(Values);
        Expected.Validate(); Values.Validate();
        if (!PriceCatalog.CanEdit(Expected.Name) || Values.Name != Expected.Name || !Expected.HasRow || !Values.HasRow ||
            Expected.Rates.Count == 0 || Values.Rates.Count < Expected.Rates.Count || Values.Rates.Count > 255 ||
            Expected.Rates.Zip(Values.Rates).Any(pair => pair.First.ColumnName != pair.Second.ColumnName ||
                pair.First.ThicknessMillimeters != pair.Second.ThicknessMillimeters ||
                pair.First.RublesPerMeter != pair.Second.RublesPerMeter && pair.Second.RublesPerMeter < 0) ||
            AddedRates.Any(rate => rate.ColumnName != ColumnName(rate.ThicknessMillimeters) || rate.RublesPerMeter is null or < 0))
            throw new ArgumentException("Существующие колонки должны сохраняться. Новая толщина — положительное число, цена — целые неотрицательные рубли.");
        return new(Expected with { Rates = Expected.Rates.ToArray() }, Values with { Rates = Values.Rates.ToArray() });
    }

    public static bool Matches(PriceTable left, PriceTable right) => left.Name == right.Name &&
        left.HasRow == right.HasRow && left.Rates.SequenceEqual(right.Rates);

    public static OrderWriteException Conflict() => new(OrderWriteFailure.Conflict,
        "Прайс уже изменён другим пользователем. Ваш ввод сохранён. Обновите прайс и проверьте правки перед сохранением.");
}
