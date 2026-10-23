using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Diagnostics;

namespace Jornal.Desktop.ViewModels;

public sealed partial class MachineImportViewModel : ObservableObject
{
    private readonly IMachineSettingsEditor _editor;
    private readonly IMachineSettingsReader _catalog;
    private MachineSettings _expected;
    private MachineSettingsChange? _attempt;
    public MachineImportViewModel(MachineSettings machine, IMachineSettingsEditor editor, IMachineSettingsReader catalog)
    {
        _name = machine.DisplayName; _expected = machine; _editor = editor; _catalog = catalog;
        _profile = Profiles.Single(choice => choice.Id == machine.SpecificationProfileId);
    }
    private string _name;
    public string Name { get => _name; set { if (SetProperty(ref _name, value ?? "")) { Error = null; Notify(); } } }
    public bool CanChangeImport => CanEdit && _expected.CanEditImport;
    public string CalculationText => "Тариф по толщине × длина реза × коэффициент";
    public IReadOnlyList<MachineProfileChoice> Profiles => MachineProfileChoice.Available;
    [ObservableProperty] private MachineProfileChoice _profile;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isReading;
    [ObservableProperty] private bool _requiresReload;
    [ObservableProperty] private bool _confirmDiscard;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsBusy => IsSaving || IsReading;
    public bool CanEdit => !IsBusy && !RequiresReload && SavedMachine is null;
    public bool HasChanges => Profile is not null && (Name.Trim() != _expected.DisplayName || Profile.Id != _expected.SpecificationProfileId);
    public bool CanSave => CanEdit && HasChanges && MachineSettings.IsValidName(Name.Trim()) && Profiles.Contains(Profile) &&
        (_expected.CanEditImport || Profile.Id == _expected.SpecificationProfileId);
    public MachineSettings? SavedMachine { get; private set; }
    public event EventHandler? Saved;
    public event EventHandler? SavingChanged;
    partial void OnProfileChanged(MachineProfileChoice value) { Error = null; Notify(); }
    partial void OnIsSavingChanged(bool value) { Notify(); SavingChanged?.Invoke(this, EventArgs.Empty); }
    partial void OnIsReadingChanged(bool value) => Notify();
    partial void OnRequiresReloadChanged(bool value) => Notify();
    private void Notify()
    {
        foreach (var property in new[] { nameof(IsBusy), nameof(CanEdit), nameof(HasChanges), nameof(CanSave), nameof(CanChangeImport) }) OnPropertyChanged(property);
        SaveCommand.NotifyCanExecuteChanged(); ReloadCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave) return;
        Error = null; IsSaving = true;
        try
        {
            _attempt = new MachineSettingsChange(_expected, Name.Trim(), Profile.Id).Snapshot();
            var saved = await _editor.UpdateMachineSettingsAsync(_attempt);
            if (saved != _attempt.Values) throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Сохранение не подтверждено. Обновите станок.");
            SavedMachine = saved;
        }
        catch (Exception exception)
        {
            RequiresReload = exception is OrderWriteException { Failure: OrderWriteFailure.Conflict or OrderWriteFailure.OutcomeUnknown };
            Error = AppDiagnostics.Describe(DiagnosticOperation.AccessHost, exception, "Не удалось изменить параметры станка.");
        }
        finally { IsSaving = false; }
        if (SavedMachine is not null) Saved?.Invoke(this, EventArgs.Empty);
    }
    private bool CanReload => !IsBusy && SavedMachine is null;
    [RelayCommand(CanExecute = nameof(CanReload))]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var nameEdited = Name.Trim() != _expected.DisplayName;
        var profileEdited = Profile.Id != _expected.SpecificationProfileId;
        Error = null; IsReading = true;
        try
        {
            var machines = await _catalog.ReadMachineSettingsAsync(cancellationToken);
            var current = machines.SingleOrDefault(machine => machine.Key == _expected.Key);
            if (current is null || current.Name != _expected.Name) throw new PriceReadException("Станок недоступен. Закройте окно и обновите прайсы.");
            if (_attempt is { } attempt && current == attempt.Values)
            { SavedMachine = current; Saved?.Invoke(this, EventArgs.Empty); return; }
            _expected = current;
            if (!nameEdited) Name = current.DisplayName;
            if (!profileEdited) Profile = Profiles.Single(choice => choice.Id == current.SpecificationProfileId);
            RequiresReload = false; Notify();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { Error = AppDiagnostics.Describe(DiagnosticOperation.PriceRead, exception, "Не удалось обновить станок."); }
        finally { IsReading = false; }
    }
}
