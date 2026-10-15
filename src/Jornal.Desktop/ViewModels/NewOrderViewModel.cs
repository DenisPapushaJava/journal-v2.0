using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public partial class NewOrderViewModel : ObservableObject
{
    private readonly IManualOrderCreator _creator;
    private bool _submitted;

    public NewOrderViewModel(IManualOrderCreator creator)
    {
        _creator = creator;
        foreach (var field in Fields) field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrderEntryField.Value) && _submitted) Validate();
        };
        CuttingType.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrderEntryField.Value)) OnPropertyChanged(nameof(IsHansLaser));
        };
    }

    public OrderEntryField OrderDate { get; } = new("Дата *", "ДД.ММ.ГГГГ");
    public OrderEntryField SpecificationNumber { get; } = new("№ спецификации *", "Например, 1015-1004");
    public OrderEntryField Manager { get; } = new("Менеджер *", "Введите имя");
    public OrderEntryField Customer { get; } = new("Заказчик *", "Введите название");
    public OrderEntryField CuttingType { get; } = new("Вид резки / станок *", choices:
        ["Лазер", "Плазма", "Газо-кислородная", "HansLaser"]);
    public OrderEntryField Material { get; } = new("Материал *", "Любая марка без пробелов");
    public OrderEntryField Thickness { get; } = new("Толщина, мм *", "Например, 2,5");
    public OrderEntryField SheetSize { get; } = new("Размер листа, мм *", "Например, 1500x3000");
    public OrderEntryField SheetCount { get; } = new("Количество листов *", "Например, 2");
    public OrderEntryField Weight { get; } = new("Вес, кг *", "Например, 38,5");
    public OrderEntryField CutLength { get; } = new("Длина реза, м *", "Целое число метров");
    public OrderEntryField Coefficient { get; } = new("Коэффициент *", value: "1");
    public OrderEntryField MaterialId { get; } = new("ID материала HansLaser", "Если известен");
    public OrderEntryField Gas { get; } = new("Газ HansLaser", "Если известен");
    public bool IsHansLaser => CuttingType.Value == "HansLaser";
    public IReadOnlyList<OrderEntryField> Fields => [OrderDate, SpecificationNumber, Manager, Customer,
        CuttingType, Material, Thickness, SheetSize, SheetCount, Weight, CutLength, Coefficient, MaterialId, Gas];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSaveError))] private string? _saveError;
    [ObservableProperty] private bool _hasValidationErrors;
    [ObservableProperty] private bool _isSaved;
    public bool HasSaveError => SaveError is not null;
    public OrderSummary? CreatedOrder { get; private set; }
    public event EventHandler? Saved;

    private bool CanSave() => !IsSaved;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsSaved) return;
        _submitted = true;
        SaveError = null;
        if (!Validate()) return;
        DateOnly.TryParseExact(OrderDate.Value.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date);
        TryDecimal(Thickness.Value, out var thickness);
        TryDecimal(Coefficient.Value, out var coefficient);
        int.TryParse(CutLength.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length);
        var draft = new ManualOrderDraft(date, SpecificationNumber.Value.Trim(), Manager.Value.Trim(),
            Customer.Value.Trim(), CuttingType.Value, Material.Value.Trim(), thickness, SheetSize.Value.Trim(),
            SheetCount.Value.Trim(), length, Weight.Value.Trim(), coefficient,
            IsHansLaser ? MaterialId.Value.Trim() : "", IsHansLaser ? Gas.Value.Trim() : "");
        try
        {
            CreatedOrder = await _creator.CreateAsync(draft);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Manual order creation failed: {0}", exception);
            SaveError = "Не удалось добавить заявку. Введённые данные сохранены в форме — попробуйте ещё раз.";
            return;
        }
        IsSaved = true;
        SaveCommand.NotifyCanExecuteChanged();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private bool Validate()
    {
        foreach (var field in Fields)
        {
            if (!IsHansLaser && (field == MaterialId || field == Gas))
            {
                field.Error = null;
                continue;
            }
            var value = field.Value.Trim();
            field.Error = value.Length > 255 ? "Не более 255 символов." :
                field != MaterialId && field != Gas && value.Length == 0 ? "Заполните поле." : null;
        }
        if (OrderDate.Error is null && !DateOnly.TryParseExact(OrderDate.Value.Trim(), "dd.MM.yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            OrderDate.Error = "Введите существующую дату: ДД.ММ.ГГГГ.";
        if (CuttingType.Error is null && !CuttingType.Choices!.Contains(CuttingType.Value))
            CuttingType.Error = "Выберите вид резки.";
        if (Material.Error is null && Material.Value.Trim().Any(char.IsWhiteSpace))
            Material.Error = "Название материала должно быть без пробелов.";
        if (Thickness.Error is null)
        {
            if (!TryDecimal(Thickness.Value, out var thickness) || thickness <= 0)
                Thickness.Error = "Введите положительное число; дробь через запятую или точку.";
            else if (CuttingType.Value == "Лазер" && thickness > 20)
                Thickness.Error = "Для лазера — до 20 мм.";
            else if (CuttingType.Value == "Плазма" && (thickness < 2 || thickness > 32))
                Thickness.Error = "Для плазмы — от 2 до 32 мм.";
            else if (CuttingType.Value == "Газо-кислородная" && (thickness < 16 || thickness > 200))
                Thickness.Error = "Для газокислородной резки — от 16 до 200 мм.";
            else if (CuttingType.Value == "HansLaser" && thickness > 60)
                Thickness.Error = "Для HansLaser — до 60 мм.";
        }
        if (CutLength.Error is null && !int.TryParse(CutLength.Value.Trim(), NumberStyles.None,
            CultureInfo.InvariantCulture, out _))
            CutLength.Error = "Целое число от 0 до 2147483647 м, как в журнале.";
        if (Coefficient.Error is null && (!TryDecimal(Coefficient.Value, out var coefficient) || coefficient <= 0))
            Coefficient.Error = "Коэффициент должен быть положительным числом.";
        HasValidationErrors = Fields.Any(field => field.HasError);
        return !HasValidationErrors;
    }

    private static bool TryDecimal(string value, out decimal number) => decimal.TryParse(value.Trim().Replace(',', '.'),
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
}
