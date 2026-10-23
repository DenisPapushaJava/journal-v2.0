using Jornal.Core.Pricing;
using Jornal.Desktop.DataSources;

namespace Jornal.Desktop.ViewModels;

public sealed partial class PriceEditorViewModel
{
    private CuttingMachine[] _machines = [];
    private bool _catalogLoaded;
    public bool IsCustomMachine => _machines.Any(machine => machine.TableName == TableName);
    private IReadOnlyList<MachineSettings> _machineSettings = [];
    private MachineSettings? SelectedMachineSettings => _machineSettings.SingleOrDefault(machine => machine.Name == SelectedGroup.Name);
    public bool ShowMachineSettings => SelectedMachineSettings is not null && editor is IMachineSettingsEditor && reader is IMachineSettingsReader;
    public bool CanEditMachineImport => _catalogLoaded && ShowMachineSettings && !IsEditing && !IsBusy && !RequiresReload;
    public MachineImportViewModel? CreateMachineImportForm()
    {
        if (!CanEditMachineImport) return null;
        var form = new MachineImportViewModel(SelectedMachineSettings!, (IMachineSettingsEditor)editor!, (IMachineSettingsReader)reader);
        form.SavingChanged += (_, _) => IsSaving = form.IsSaving;
        return form;
    }
    public bool CanAddMachine => _catalogLoaded && _machineSettings.Count > 0 && !IsEditing && !IsBusy && !RequiresReload && editor is IMachineCreator && reader is IMachineCatalogReader;
    private async Task LoadMachineCatalogAsync(CancellationToken cancellationToken)
    {
        if (reader is not IMachineCatalogReader catalog) return;
        var snapshot = await MachineCatalogSnapshot.ReadAsync(catalog, cancellationToken);
        var machines = snapshot.Machines.ToArray();
        _machineSettings = snapshot.Settings;
        if (snapshot.NamesError is { } exception)
        {
            Error = Jornal.Infrastructure.Diagnostics.AppDiagnostics.Describe(Jornal.Infrastructure.Diagnostics.DiagnosticOperation.PriceRead,
                exception, "Названия станков недоступны. Показаны исходные названия; параметры станков не изменены.");
        }
        var names = snapshot.Names;
        var selectedName = SelectedGroup.Name;
        var groups = PriceCatalog.Groups.Take(4).Concat(machines.Select(machine =>
            Groups.FirstOrDefault(group => group.Profiles.Count == 1 && group.Profiles[0].TableName == machine.TableName) ??
            new PriceGroup(machine.Name, [new PriceProfile(machine.TableName, "Основной", machine.Name)])))
            .Concat(PriceCatalog.Groups.Skip(4)).Select(group => PresentGroup(group, names)).ToArray();
        // Сохраняем ссылки выбранных групп: обновление каталога не сбрасывает прайс/ввод.
        for (var index = 0; index < groups.Length; index++)
        {
            if (index < Groups.Count && ReferenceEquals(Groups[index], groups[index])) continue;
            var old = Groups.IndexOf(groups[index]);
            if (old >= 0) Groups.Move(old, index); else Groups.Insert(index, groups[index]);
        }
        while (Groups.Count > groups.Length) Groups.RemoveAt(Groups.Count - 1);
        _machines = machines; _catalogLoaded = true;
        _selectedGroup = Groups.FirstOrDefault(group => group.Name == selectedName) ?? Groups[0];
        _selectedProfile = _selectedGroup.Profiles.FirstOrDefault(profile => profile.TableName == _selectedProfile.TableName) ?? _selectedGroup.Profiles[0];
        OnPropertyChanged(nameof(SelectedGroup)); OnPropertyChanged(nameof(SelectedProfile)); OnPropertyChanged(nameof(Profiles)); OnPropertyChanged(nameof(Usage));
    }
    private static PriceGroup PresentGroup(PriceGroup group, IReadOnlyDictionary<string, string> names)
    {
        string Name(string value) => names.GetValueOrDefault(value) ?? value;
        var profiles = group.Profiles.Select(profile =>
        {
            var usage = profile.TableName switch
            {
                PriceTables.Laser => Name("Лазер") + " · " + Name("HansLaser") + " без ID 1/2",
                PriceTables.Plasma => Name("Плазма"), PriceTables.GasOxygen => Name("Газо-кислородная") + " резка",
                PriceTables.HansLaserId1 => Name("HansLaser") + " · ID 1", PriceTables.HansLaserId2 => Name("HansLaser") + " · ID 2",
                _ => CuttingMachine.IsCustomTable(profile.TableName) ? Name(group.Name) : profile.Usage
            };
            return usage == profile.Usage ? profile : profile with { Usage = usage };
        }).ToArray();
        return group.DisplayName == Name(group.Name) && profiles.SequenceEqual(group.Profiles) ? group :
            group with { DisplayName = Name(group.Name), Profiles = profiles };
    }
    public NewMachineViewModel? CreateMachineForm()
    {
        if (!CanAddMachine) return null;
        var form = new NewMachineViewModel((IMachineCreator)editor!, (IMachineCatalogReader)reader, _machines, storage);
        form.SavingChanged += (_, _) => IsSaving = form.IsSaving;
        return form;
    }
    public async Task ShowMachineSettingsAsync(MachineSettings machine)
    {
        await RefreshCommand.ExecuteAsync(null);
        if (Groups.FirstOrDefault(group => group.Name == machine.Name) is { } group) SelectedGroup = group;
    }
    public async Task ShowCreatedMachineAsync(CuttingMachine machine)
    {
        await RefreshCommand.ExecuteAsync(null);
        if (Groups.FirstOrDefault(group => group.Profiles.Any(profile => profile.TableName == machine.TableName)) is { } group) SelectedGroup = group;
    }
}
