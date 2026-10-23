using System.Globalization;
using System.Text;
using Jornal.Core.Orders;

namespace Jornal.Core.Results;

public static class OrderResultFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly StringComparer MaterialComparer = StringComparer.Create(Culture, ignoreCase: true);

    public static string Format(IReadOnlyList<OrderSummary> orders, ResultPriceSettings? pricing = null,
        ResultRowOrder rowOrder = ResultRowOrder.ThicknessAndMaterial, Func<string, string>? machineName = null)
    {
        if (orders.Any(order => order.StoredCuttingPrice is null))
            throw new ArgumentException("Для результата нужна рассчитанная стоимость каждой заявки.", nameof(orders));
        if (orders.Any(order => order.AnnotationsUnavailable || order.AnnotationsNeedReview || order.OperationsUnavailable || order.OperationsNeedReview))
            throw new ArgumentException("Не удалось проверить гибку и фаску выбранных заявок. Проверьте пометки в редакторе или обновите список (F5).");
        if (orders.Count == 0) return string.Empty;
        pricing = (pricing ?? new()).Normalize();
        // Сортируем весь выбранный набор; формат строк и исходный список сохраняются.
        if (rowOrder != ResultRowOrder.SelectionOrder) orders = orders.OrderBy(order => order.ThicknessMillimeters is null)
            .ThenBy(order => order.ThicknessMillimeters)
            .ThenBy(order => order.MaterialName, MaterialComparer).ToArray();
        var rows = orders.Select(order =>
        {
            // Ширина суммы зависит только от её итогового отображения.
            // Прежнее округление Convert.ToInt32 — к чётному. Currency не ограничиваем диапазоном Int32.
            var price = decimal.Round(order.StoredCuttingPrice!.Value, 0, MidpointRounding.ToEven);
            if (pricing.UseMinimumPrice) price = Math.Max(pricing.MinimumPrice, price);
            return new[]
            {
                order.ThicknessMillimeters?.ToString("0.###", Culture) ?? "—", order.MaterialName, order.SheetCount,
                order.WeightKilograms.Replace('.', ','), price.ToString("0", Culture),
                (machineName?.Invoke(order.CuttingType) ?? order.CuttingType).ToUpper(Culture)
            };
        }).ToArray();
        var widths = Enumerable.Range(0, rows[0].Length).Select(column => rows.Max(row => row[column].Length)).ToArray();
        var result = new StringBuilder();
        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            for (var column = 0; column < row.Length; column++)
                row[column] = new string(' ', Math.Max(0, widths[column] - row[column].Length) * 2) + row[column];
            // Порядок полей, табуляции и три пробела после них соответствуют PrintResult старого журнала.
            result.Append("Лист ").Append(row[0]).Append('x').Append(orders[index].SheetSize).Append(",\t   Материал: ")
                .Append(row[1]).Append(",\t   ").Append(row[2]).Append(" шт.\t   Вес ").Append(row[3])
                .Append(" кг.\t   Резка: ").Append(row[4]).Append(" ₽.");
            if (orders[index].CuttingDurationMinutes is { } minutes)
                result.Append("     Время резки: ").Append(CuttingDuration.Format(minutes));
            result.Append("\t   ").Append(row[5]);
            if (orders[index].HasChamfer) result.Append("   ФАСКА");
            if (orders[index].HasBending) result.Append("   ГИБКА");
            result.Append('\n');
        }
        // Последний LF переводит ввод на строку сразу под результатом.
        return result.ToString();
    }
}
