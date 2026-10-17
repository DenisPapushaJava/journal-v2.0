using System.Globalization;
using System.Text;
using Jornal.Core.Orders;

namespace Jornal.Core.Results;

public static class OrderCommentFormatter
{
    private static readonly string[] LegacyOrder = ["Лазер", "HansLaser", "Плазма", "Газо-кислородная"];
    public static string Row(OrderSummary order)
    {
        RequireDelivery(order);
        return $"({Thickness(order)} мм)  {Line(order.SpecificationNumber)},  {Line(order.ManagerName)},  ({Line(order.CustomerName)}),  {Line(order.DeliveryNumber)}";
    }

    public static string Telegram(IReadOnlyList<OrderSummary> orders)
    {
        if (orders.Count == 0) throw new ArgumentException("Выберите заявки.");
        foreach (var order in orders) RequireDelivery(order);
        var groups = orders.GroupBy(order => (Number: Line(order.DeliveryNumber), Manager: Line(order.ManagerName), Customer: Line(order.CustomerName))).ToArray();
        var result = new StringBuilder();
        foreach (var group in groups)
        {
            if (result.Length > 0 && result[^1] != '\n') result.Append("\n\n");
            var display = TelegramNumber(group.Key.Number);
            // Разные исходные номера с одинаковыми последними цифрами не смешиваем.
            if (groups.Any(other => other.Key != group.Key && other.Key.Manager == group.Key.Manager && other.Key.Customer == group.Key.Customer &&
                TelegramNumber(other.Key.Number) == display)) display = group.Key.Number;
            result.Append(display).Append("  ").Append(group.Key.Manager).Append(" (").Append(group.Key.Customer).Append(")\n");
            foreach (var machine in group.GroupBy(order => Line(order.CuttingType)).OrderBy(machine =>
                { var index = Array.IndexOf(LegacyOrder, machine.Key); return index < 0 ? int.MaxValue : index; }))
            {
                if (result.Length > 0 && result[^1] != '\n') result.Append("\n\n");
                foreach (var order in machine) result.Append("**").Append(Thickness(order)).Append(" мм ").Append(Line(order.MaterialName)).Append("**  \n");
                result.Length--; // Прежний формат: подпись станка после последней строки группы.
                var caption = machine.Key switch { "Плазма" => "Плазму", "Газо-кислородная" => "Кристалл резка газом", _ => machine.Key.Length > 0 ? machine.Key : "Станок не указан" };
                result.Append(" на ").Append(caption);
                if (machine.Key != "Газо-кислородная") result.Append("\n\n");
            }
        }
        return result.ToString();
    }

    public static string Line(string? value) => string.Join(" ", new string((value ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Thickness(OrderSummary order) => order.ThicknessMillimeters?.ToString("0.############################", CultureInfo.GetCultureInfo("ru-RU")) ?? "";
    private static void RequireDelivery(OrderSummary order)
    {
        if (Line(order.DeliveryNumber).Length == 0)
            throw new ArgumentException("Укажите БЛЦБ для заявки " + Line(order.SpecificationNumber) + ".");
    }
    private static string TelegramNumber(string value)
    {
        var suffix = value.Length > 5 ? value[^5..] : value;
        return suffix.Length > 0 && suffix.All(char.IsAsciiDigit) ? suffix.TrimStart('0') is { Length: > 0 } number ? number : "0" : value;
    }
}
