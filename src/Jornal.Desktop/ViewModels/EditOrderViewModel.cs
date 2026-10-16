using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Desktop.ViewModels;

public partial class EditOrderViewModel : ObservableObject
{
    private readonly IOrderMaintenance _store;
    private readonly Func<CancellationToken, Task<OrderRecord?>> _refresh;
    private OrderRecord _expected;
    private OrderRecord? _current;
    private readonly Dictionary<OrderEntryField, string> _initial = [];
    private CuttingPriceQuote? _quote;
    public int Id => _expected.Id;
    public string WindowTitle => $"Редактировать заявку · код {Id}";
    public string SourceDescription { get; }
    public string StoredPriceText => _expected.Price?.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + (_expected.Price is null ? "Неизвестна" : " ₽");
    public string MetadataText => $"Детали: {_expected.ToSummary().PartCount?.ToString() ?? "—"} · пробивки: {_expected.ToSummary().PunchCount?.ToString() ?? "—"}\nЦвет и БЛЦБ сохраняются отдельно и здесь не меняются.";
    public AccessHostHelpViewModel AccessHostHelp { get; } = new();
    public OrderRecord? SavedRecord { get; private set; }
    public event EventHandler? Saved;
    public event EventHandler? ReloadRequired;
    public event EventHandler? BusyChanged;

    public EditOrderViewModel(OrderRecord expected, IOrderMaintenance store, string sourceDescription,
        Func<CancellationToken, Task<OrderRecord?>> refresh)
    {
        _expected = expected; _store = store; SourceDescription = sourceDescription; _refresh = refresh;
        Populate(expected);
        foreach (var field in Fields) field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OrderEntryField.Value)) return;
            field.Error = null;
            if (field == CuttingType || field == Thickness || field == CutLength || field == Coefficient || field == MaterialId)
            { _quote = null; PriceText = "Не рассчитана"; }
            if (field == CuttingType) OnPropertyChanged(nameof(IsHansLaser));
        };
        SaveCommand.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning)) NotifyBusy(); };
        CalculateCommand.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning)) NotifyBusy(); };
        RefreshCommand.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IAsyncRelayCommand.IsRunning)) NotifyBusy(); };
    }

    public OrderEntryField OrderDate { get; } = new("Дата", "ДД.ММ.ГГГГ");
    public OrderEntryField SpecificationNumber { get; } = new("№ спецификации");
    public OrderEntryField Manager { get; } = new("Менеджер");
    public OrderEntryField Customer { get; } = new("Заказчик");
    public OrderEntryField CuttingType { get; } = new("Вид резки / станок", choices: ["Лазер", "Плазма", "Газо-кислородная", "HansLaser"]);
    public OrderEntryField Material { get; } = new("Материал", "Любая марка без пробелов");
    public OrderEntryField Thickness { get; } = new("Толщина, мм");
    public OrderEntryField SheetSize { get; } = new("Размер листа, мм");
    public OrderEntryField SheetCount { get; } = new("Количество листов");
    public OrderEntryField Weight { get; } = new("Вес, кг");
    public OrderEntryField CutLength { get; } = new("Длина реза, м");
    public OrderEntryField Coefficient { get; } = new("Коэффициент");
    public OrderEntryField MaterialId { get; } = new("ID материала HansLaser");
    public OrderEntryField Gas { get; } = new("Газ HansLaser");
    public IReadOnlyList<OrderEntryField> Fields => [OrderDate, SpecificationNumber, Manager, Customer, CuttingType, Material,
        Thickness, SheetSize, SheetCount, Weight, CutLength, Coefficient, MaterialId, Gas];
    public bool IsHansLaser => CuttingType.Value == "HansLaser";
    [ObservableProperty] private string _priceText = "Не рассчитана";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    [ObservableProperty] private bool _requiresReload;
    [ObservableProperty] private bool _hasCurrentVersion;
    [ObservableProperty] private string _comparison = "";
    public bool HasError => Error is not null;
    public bool IsBusy => SaveCommand.IsRunning || CalculateCommand.IsRunning || RefreshCommand.IsRunning;
    public bool CanSave => !RequiresReload && !IsBusy && SavedRecord is null;
    public bool CanRefresh => RequiresReload && !IsBusy;
    partial void OnRequiresReloadChanged(bool value) => NotifyBusy();
    private void NotifyBusy()
    {
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(CanRefresh));
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Populate(OrderRecord row)
    {
        var values = FieldValues(row);
        for (var i = 0; i < Fields.Count; i++) { Fields[i].Value = values[i]; _initial[Fields[i]] = values[i]; }
        _quote = null; PriceText = "Не рассчитана";
    }

    private static string[] FieldValues(OrderRecord row)
    {
        var view = row.ToSummary();
        return [row.Date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "", row.Specification ?? "", row.Manager ?? "",
            row.Customer ?? "", row.CuttingType ?? "", row.Material ?? "", view.ThicknessMillimeters?.ToString(CultureInfo.InvariantCulture) ?? "",
            row.SheetSize ?? "", row.SheetCount ?? "", row.Weight ?? "", row.CutLength?.ToString(CultureInfo.InvariantCulture) ?? "",
            view.Coefficient?.ToString(CultureInfo.InvariantCulture) ?? "", view.MaterialId, view.CuttingGas];
    }

    public OrderEditChange BuildChange()
    {
        foreach (var field in Fields) field.Error = null;
        string? Text(OrderEntryField field, string? original)
        {
            if (field.Value == _initial[field]) return original;
            if (string.IsNullOrWhiteSpace(field.Value) || field.Value.Length > 255 || field.Value.Any(char.IsControl))
            { field.Error = "Введите значение до 255 символов"; throw new ArgumentException(field.Label); }
            return field.Value.Trim();
        }
        decimal Number(OrderEntryField field)
        {
            if (decimal.TryParse(field.Value.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var number)) return number;
            field.Error = "Введите число"; throw new ArgumentException(field.Label);
        }
        var date = _expected.Date;
        if (OrderDate.Value != _initial[OrderDate])
        {
            if (!DateTime.TryParseExact(OrderDate.Value.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) || parsed.Year < 100)
            { OrderDate.Error = "Укажите дату ДД.ММ.ГГГГ"; throw new ArgumentException("Дата"); }
            date = parsed;
        }
        var thickness = Number(Thickness);
        if (!int.TryParse(CutLength.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        { CutLength.Error = "Целое число метров"; throw new ArgumentException("Длина"); }
        var coefficient = Number(Coefficient);
        var storedCoefficient = _expected.Coefficient;
        if (Coefficient.Value != _initial[Coefficient] || MaterialId.Value != _initial[MaterialId] || Gas.Value != _initial[Gas])
        {
            if (new[] { MaterialId.Value, Gas.Value }.Any(value => value.Length > 255 || value.Any(c => c == '|' || char.IsControl(c))))
                throw new ArgumentException("Проверьте ID материала и газ: разделитель | не допускается.");
            var original = _expected.Coefficient?.Split('|') ?? [];
            var parts = Enumerable.Range(0, Math.Max(5, original.Length)).Select(index => index < original.Length ? original[index] : "-1").ToArray();
            if (Coefficient.Value != _initial[Coefficient]) parts[0] = coefficient.ToString(CultureInfo.InvariantCulture);
            if (MaterialId.Value != _initial[MaterialId]) parts[3] = string.IsNullOrWhiteSpace(MaterialId.Value) ? "-1" : MaterialId.Value.Trim();
            if (Gas.Value != _initial[Gas]) parts[4] = string.IsNullOrWhiteSpace(Gas.Value) ? "-1" : Gas.Value.Trim();
            storedCoefficient = string.Join('|', parts);
        }
        var material = Text(Material, _expected.Material);
        if (Material.Value != _initial[Material] && material?.Any(char.IsWhiteSpace) == true)
        { Material.Error = "Материал без пробелов"; throw new ArgumentException("Материал"); }
        var values = _expected with { Date = date, Manager = Text(Manager, _expected.Manager), Specification = Text(SpecificationNumber, _expected.Specification),
            Customer = Text(Customer, _expected.Customer), CuttingType = CuttingType.Value, Material = material, SheetSize = Text(SheetSize, _expected.SheetSize),
            SheetCount = Text(SheetCount, _expected.SheetCount), Weight = Text(Weight, _expected.Weight),
            Thickness = Thickness.Value == _initial[Thickness] ? _expected.Thickness : (double)thickness, CutLength = length, Coefficient = storedCoefficient };
        var change = new OrderEditChange(_expected, values, _quote); change.Validate(); return change;
    }

    [RelayCommand]
    private async Task CalculateAsync(CancellationToken cancellationToken)
    {
        if (RequiresReload || SavedRecord is not null || SaveCommand.IsRunning) return;
        Error = null; AccessHostHelp.ShowFor(null); _quote = null;
        try { ShowQuote(await new CuttingPriceService(_store).CalculateAsync(BuildChange().PriceRequest, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { Error = exception.Message; AccessHostHelp.ShowFor(exception); }
    }

    private void ShowQuote(CuttingPriceQuote quote) { _quote = quote; PriceText = quote.StoredPrice.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽"; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (RequiresReload || SavedRecord is not null || CalculateCommand.IsRunning || RefreshCommand.IsRunning) return;
        Error = null; AccessHostHelp.ShowFor(null);
        try
        {
            var change = BuildChange();
            if (_quote is null)
            {
                ShowQuote(await new CuttingPriceService(_store).CalculateAsync(change.PriceRequest));
                Error = "Новая цена рассчитана. Сверьте её с прежней и нажмите «Сохранить» для записи."; return;
            }
            SavedRecord = await _store.UpdateAsync(change);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (OrderEditPriceChangedException exception) { ShowQuote(exception.Quote); Error = exception.Message; }
        catch (Exception exception)
        {
            Error = exception.Message; AccessHostHelp.ShowFor(exception);
            if (exception is OrderWriteException { RequiresReload: true }) { RequiresReload = true; ReloadRequired?.Invoke(this, EventArgs.Empty); }
        }
    }

    public void AcceptCurrentSnapshot(OrderRecord? current)
    {
        _quote = null; PriceText = "Не рассчитана";
        if (current is null) { Error = "Заявка удалена. Ввод сохранён для сверки; сохранение заблокировано."; RequiresReload = true; return; }
        if (current == _expected) { RequiresReload = false; HasCurrentVersion = false; Error = "Список обновлён. Ввод сохранён; перед повтором сверьте заявку."; return; }
        _current = current; HasCurrentVersion = true; RequiresReload = true;
        var actual = FieldValues(current);
        var differences = Fields.Select((field, index) => (field, value: actual[index])).Where(pair => pair.value != _initial[pair.field])
            .Select(pair => $"{pair.field.Label}: в базе «{pair.value}», в форме «{pair.field.Value}»");
        Comparison = "Текущая версия отличается:\n" + string.Join('\n', differences) +
            $"\nЦена в базе: {current.Price?.ToString(CultureInfo.InvariantCulture) ?? "NULL"}\nБЛЦБ: {current.DeliveryNumber ?? "NULL"} · цвет: {current.Color?.ToString() ?? "NULL"}\n" +
            "Ваши изменённые поля будут применены к текущей записи. Остальные поля обновятся из базы. Проверьте различия перед продолжением.";
        Error = "Автоматическая перезапись чужих изменений остановлена.";
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!RequiresReload || SaveCommand.IsRunning) return;
        try { AcceptCurrentSnapshot(await _refresh(cancellationToken)); }
        catch (Exception exception) { Error = exception.Message; AccessHostHelp.ShowFor(exception); }
    }

    [RelayCommand]
    private void Rebase()
    {
        if (_current is null || IsBusy) return;
        var edits = Fields.Where(field => field.Value != _initial[field]).ToDictionary(field => field, field => field.Value);
        _expected = _current; Populate(_current);
        foreach (var (field, value) in edits) field.Value = value;
        _current = null; HasCurrentVersion = false; RequiresReload = false; Error = "Правки перенесены. Проверьте поля и новую цену перед сохранением.";
        OnPropertyChanged(nameof(StoredPriceText)); OnPropertyChanged(nameof(MetadataText));
    }
}
