using System.Globalization;
using Avalonia.Media;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public sealed class OrderRowViewModel(OrderSummary order)
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("ru-RU");

    internal OrderSummary Source => order;
    public int Id => order.Id;
    public DateOnly OrderDate => order.OrderDate;
    public int Color => order.Color;
    public decimal ThicknessMillimeters => order.ThicknessMillimeters;
    public decimal? StoredCuttingPrice => order.StoredCuttingPrice;
    public int? CutLengthMeters => order.CutLengthMeters;

    public string DateText => order.OrderDate.ToString("dd.MM.yyyy", DisplayCulture);
    public string SpecificationNumber => order.SpecificationNumber;
    public string ManagerName => order.ManagerName;
    public string CustomerName => order.CustomerName;
    public string CuttingType => order.CuttingType;
    public string MaterialName => order.MaterialName;
    public string ThicknessText => order.ThicknessMillimeters.ToString("0.###", DisplayCulture);
    public string StoredPriceText => order.StoredCuttingPrice is { } price
        ? price.ToString("N2", DisplayCulture) + " ₽" : "Не рассчитана";
    public string SheetSize => order.SheetSize;
    public string SheetCount => order.SheetCount;
    public string CutLengthText => order.CutLengthMeters?.ToString(DisplayCulture) ?? "—";
    public string WeightText => order.WeightKilograms.Replace('.', ',');
    public string DeliveryNumber => order.DeliveryNumber;

    public string StatusKey => order.Color switch
    {
        -882036 => "work",
        -10238087 => "ready",
        -8154114 => "shipped",
        _ => "neutral"
    };

    public IBrush ColorMark => order.Color == -1
        ? Brushes.Transparent
        : new SolidColorBrush(Avalonia.Media.Color.FromUInt32(unchecked((uint)order.Color)));

    public string StatusText => order.Color switch
    {
        -882036 => "В работе",
        -10238087 => "Готово",
        -8154114 => "Отгружено",
        -1 => "Без отметки",
        _ => "Своя отметка"
    };
}
