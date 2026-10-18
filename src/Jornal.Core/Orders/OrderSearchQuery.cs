using System.Globalization;

namespace Jornal.Core.Orders;

public enum OrderSearchField { All, Specification, DeliveryNumber, Customer, Manager, Material, Comment }

// Подготовка запроса выполняется один раз; каждое слово должно встретиться в заявке.
public sealed class OrderSearchQuery
{
    private readonly (string Text, decimal? Thickness)[] _terms;
    public string Text { get; }
    public OrderSearchField Field { get; }

    public OrderSearchQuery(string? text, OrderSearchField field = OrderSearchField.All)
    {
        Field = Enum.IsDefined(field) ? field : OrderSearchField.All;
        var words = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Text = string.Join(' ', words);
        _terms = words.Select(word => (word, decimal.TryParse(word.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var thickness) ? (decimal?)thickness : null)).ToArray();
    }

    public bool Matches(OrderSummary order)
    {
        foreach (var term in _terms)
        {
            bool Contains(string? value) => value?.Contains(term.Text, StringComparison.OrdinalIgnoreCase) == true;
            var matches = Field switch
            {
                OrderSearchField.Specification => Contains(order.SpecificationNumber),
                OrderSearchField.DeliveryNumber => Contains(order.DeliveryNumber),
                OrderSearchField.Customer => Contains(order.CustomerName),
                OrderSearchField.Manager => Contains(order.ManagerName),
                OrderSearchField.Material => Contains(order.MaterialName),
                OrderSearchField.Comment => Contains(order.Comment),
                _ => Contains(order.SpecificationNumber) || Contains(order.DeliveryNumber) || Contains(order.CustomerName)
                    || Contains(order.ManagerName) || Contains(order.MaterialName) || Contains(order.CuttingType)
                    || Contains(order.Comment) || term.Thickness is { } value && order.ThicknessMillimeters == value
            };
            if (!matches) return false;
        }
        return true;
    }
}
