using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public sealed class OrderRowViewModel(OrderSummary order) : ObservableObject
{
    private OrderSummary _order = order;
    private long? _cuttingDurationMinutes = order.CuttingDurationMinutes;
    internal void Update(OrderSummary replacement)
    {
        if (replacement.Id != Id) throw new ArgumentException("Нельзя заменить идентификатор строки.", nameof(replacement));
        _order = replacement;
        _cuttingDurationMinutes = replacement.CuttingDurationMinutes;
        OnPropertyChanged(string.Empty);
    }

    private static readonly CultureInfo DisplayCulture = CultureInfo.GetCultureInfo("ru-RU");

    internal OrderSummary Source => _order;
    public int Id => _order.Id;
    public DateOnly? OrderDate => _order.OrderDate;
    public int? Color => _order.Color;
    public decimal? ThicknessMillimeters => _order.ThicknessMillimeters;
    public decimal? StoredCuttingPrice => _order.StoredCuttingPrice;
    public int? CutLengthMeters => _order.CutLengthMeters;
    public long? CuttingDurationMinutes => _cuttingDurationMinutes;
    public string CuttingTimeText => CuttingDurationMinutes is { } minutes ? CuttingDuration.Format(minutes) : "—";
    public string CuttingTimeHint => _order.CuttingTime is not null && CuttingDurationMinutes is null
        ? "Параметры заявки изменились. Проверьте время в редактировании."
        : CuttingDurationMinutes is null ? "Время резки не задано" : "Длительность резки, часы:минуты. Часы могут превышать 24.";

    public string DateText => _order.OrderDate?.ToString("dd.MM.yyyy", DisplayCulture) ?? "—";
    public string SpecificationNumber => _order.SpecificationNumber;
    public string ManagerName => _order.ManagerName;
    public string CustomerName => _order.CustomerName;
    public string CuttingType => _order.CuttingType;
    public string MaterialName => _order.MaterialName;
    public string ThicknessText => _order.ThicknessMillimeters?.ToString("0.###", DisplayCulture) ?? "—";
    public string StoredPriceText => _order.StoredCuttingPrice is { } price
        ? price.ToString("N2", DisplayCulture) + " ₽" : "Не рассчитана";
    public string SheetSize => _order.SheetSize;
    public string SheetCount => _order.SheetCount;
    public string CutLengthText => _order.CutLengthMeters?.ToString(DisplayCulture) ?? "—";
    public string WeightText => _order.WeightKilograms.Replace('.', ',');
    public string DeliveryNumber => _order.DeliveryNumber ?? "";

    public string StatusKey => _order.Color switch
    {
        OrderColors.Work => "work",
        OrderColors.Ready => "ready",
        OrderColors.Shipped => "shipped",
        _ => "neutral"
    };

    public bool HasStandardStatus => _order.Color is OrderColors.Work or OrderColors.Ready or OrderColors.Shipped;

    public string StatusText => _order.Color switch
    {
        OrderColors.Work => "В работе",
        OrderColors.Ready => "Готово",
        OrderColors.Shipped => "Отгружено",
        _ => ""
    };
}
