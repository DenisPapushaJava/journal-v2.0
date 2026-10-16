using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Desktop.ViewModels;

public partial class NewOrderViewModel : ObservableObject
{
    private readonly IManualOrderCreator _creator;
    private bool _submitted;
    private CuttingPriceQuote? _quote;

    public NewOrderViewModel(IManualOrderCreator creator, bool isLocalCopy = false, bool isDatabase = false)
    {
        _creator = creator;
        IsLocalCopy = isLocalCopy;
        IsDatabase = isDatabase;
        foreach (var field in Fields) field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OrderEntryField.Value)) return;
            if (_submitted) Validate();
            if (field == CuttingType || field == Thickness || field == CutLength || field == Coefficient || field == MaterialId)
            { _quote = null; PriceText = "Не рассчитана"; PriceBreakdown = ""; PriceError = null; }
        };
        SaveCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(IAsyncRelayCommand.IsRunning)) return;
            OnPropertyChanged(nameof(IsBusy)); CalculatePriceCommand.NotifyCanExecuteChanged();
        };
        CalculatePriceCommand.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(IAsyncRelayCommand.IsRunning)) return;
            OnPropertyChanged(nameof(IsBusy)); SaveCommand.NotifyCanExecuteChanged();
        };
        CuttingType.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OrderEntryField.Value)) OnPropertyChanged(nameof(IsHansLaser));
        };
    }

    public bool IsLocalCopy { get; }
    public bool IsDatabase { get; }
    public AccessHostHelpViewModel AccessHostHelp { get; } = new();
    partial void OnPriceErrorChanged(string? value) { if (value is null && SaveError is null) AccessHostHelp.ShowFor(null); }
    partial void OnSaveErrorChanged(string? value) { if (value is null && PriceError is null) AccessHostHelp.ShowFor(null); }
    public bool HasPricing => _creator is IPriceReader;
    public bool IsBusy => SaveCommand.IsRunning || CalculatePriceCommand.IsRunning || IsImporting;
    [ObservableProperty] private string _priceText = "Не рассчитана";
    [ObservableProperty] private string _priceBreakdown = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPriceError))] private string? _priceError;
    public bool HasPriceError => PriceError is not null;
    public string WindowTitle => IsLocalCopy ? "Новая заявка · Локальная копия" : IsDatabase ? "Новая заявка · MDB" : "Новая заявка · Деморежим";
    public string StorageDescription => (HasImport ? "Из спецификации · " : "Ручной ввод · ") + (IsLocalCopy ? "Локальная тестовая копия — изменения сохраняются между запусками" :
        IsDatabase ? "Сохранение в подключённую MDB" : "Деморежим — данные доступны до закрытия программы");
    public string SaveDescription => IsLocalCopy ? "Добавление в локальную копию; исходная MDB не меняется" :
        IsDatabase ? "Новая заявка будет добавлена в MDB с расчётом по её прайсу" : "Добавление только в деможурнал текущего сеанса";
    public string SaveLabel => IsLocalCopy || IsDatabase ? "Добавить заявку" : "Добавить в деможурнал";
    public string PriceDescription => IsDatabase ? "Тариф проверяется при сохранении. Если он изменился, покажем новую цену." :
        IsLocalCopy ? "При сохранении цена рассчитывается по прайсам, перенесённым из MDB. Существующие суммы не пересчитываются." :
        "Расчёт появится после подключения прайсов из базы.";

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
    public event EventHandler? ReloadRequired;
    [ObservableProperty] private bool _requiresReload;
    partial void OnRequiresReloadChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();

    public void ResumeAfterReload()
    {
        RequiresReload = false;
        SaveError = "Ввод восстановлен. Перед повторным добавлением проверьте список: предыдущая заявка могла сохраниться.";
    }

    private bool CanSave() => !IsSaved && !RequiresReload && !CalculatePriceCommand.IsRunning && !IsImporting && !HasPendingImport;

    private bool CanCalculatePrice() => HasPricing && !IsSaved && !SaveCommand.IsRunning && !IsImporting && !HasPendingImport;

    [RelayCommand(CanExecute = nameof(CanCalculatePrice))]
    private async Task CalculatePriceAsync(CancellationToken cancellationToken)
    {
        if (!CanCalculatePrice()) return;
        PriceError = null;
        AccessHostHelp.ShowFor(null);
        _quote = null;
        PriceText = "Не рассчитана";
        PriceBreakdown = "";
        if (!TryDecimal(Thickness.Value, out var thickness) || !TryDecimal(Coefficient.Value, out var coefficient) ||
            !int.TryParse(CutLength.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        { PriceError = "Укажите толщину, целую длину в метрах и коэффициент."; return; }
        try
        {
            var quote = await new CuttingPriceService((IPriceReader)_creator).CalculateAsync(
                new(CuttingType.Value, thickness, length, coefficient, IsHansLaser ? MaterialId.Value.Trim() : null), cancellationToken);
            ShowQuote(quote);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is PriceCalculationException or PriceReadException or OrderReadException)
        { PriceError = exception.Message; AccessHostHelp.ShowFor(exception); }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        _submitted = true;
        SaveError = null;
        AccessHostHelp.ShowFor(null);
        if (!Validate()) return;
        DateOnly.TryParseExact(OrderDate.Value.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date);
        TryDecimal(Thickness.Value, out var thickness);
        TryDecimal(Coefficient.Value, out var coefficient);
        int.TryParse(CutLength.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length);
        var draft = new ManualOrderDraft(date, SpecificationNumber.Value.Trim(), Manager.Value.Trim(),
            Customer.Value.Trim(), CuttingType.Value, Material.Value.Trim(), thickness, SheetSize.Value.Trim(),
            SheetCount.Value.Trim(), length, Weight.Value.Trim(), coefficient,
            IsHansLaser ? MaterialId.Value.Trim() : "", IsHansLaser ? Gas.Value.Trim() : "")
            { ExpectedQuote = _quote, PartCount = ImportPreview?.PartCount, PunchCount = ImportPreview?.PunchCount };
        try
        {
            CreatedOrder = await _creator.CreateAsync(draft);
        }
        catch (OrderPriceChangedException exception)
        {
            ShowQuote(exception.Quote);
            SaveError = exception.Message;
            return;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Manual order creation failed: {0}", exception);
            SaveError = exception is PriceCalculationException or OrderWriteException ? exception.Message :
                "Не удалось добавить заявку. Введённые данные сохранены в форме — попробуйте ещё раз.";
            AccessHostHelp.ShowFor(exception);
            if (exception is OrderWriteException { RequiresReload: true })
            { RequiresReload = true; ReloadRequired?.Invoke(this, EventArgs.Empty); }
            return;
        }
        IsSaved = true;
        SaveCommand.NotifyCanExecuteChanged();
        CalculatePriceCommand.NotifyCanExecuteChanged();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void ShowQuote(CuttingPriceQuote quote)
    {
        _quote = quote;
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        PriceText = quote.StoredPrice.ToString("N0", culture) + " ₽";
        PriceBreakdown = $"{quote.RublesPerMeter.ToString("N0", culture)} ₽/м × {quote.CutLengthMeters} м × {quote.Coefficient.ToString(culture)}";
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
