using System.Globalization;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public sealed class OrderRowViewModel(OrderSummary order)
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string DateText => order.OrderDate.ToString("dd.MM.yyyy", DisplayCulture);
    public string SpecificationNumber => order.SpecificationNumber;
    public string ManagerName => order.ManagerName;
    public string CustomerName => order.CustomerName;
    public string CuttingType => order.CuttingType;
    public string MaterialName => order.MaterialName;
    public string ThicknessText => order.ThicknessMillimeters.ToString("0.###", DisplayCulture);
    public string StoredPriceText => order.StoredCuttingPrice.ToString("N2", DisplayCulture) + " ₽";

    public string StatusText => order.Color switch
    {
        -882036 => "В работе",
        -10238087 => "Готово",
        -8154114 => "Отгружено",
        -1 => "Без отметки",
        _ => "Своя отметка"
    };
}
