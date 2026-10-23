using Jornal.Core.Pricing;
using Jornal.Infrastructure.Diagnostics;
using Jornal.Desktop.DataSources;

namespace Jornal.Desktop.ViewModels;

public partial class NewOrderViewModel
{
    private IReadOnlyList<CuttingMachine> _machines = [];
    private IReadOnlyDictionary<string, string> _machineNames = new Dictionary<string, string>();
    private CuttingMachine? SelectedMachine => _machines.SingleOrDefault(machine => machine.Name == CuttingType.Value);
    internal void SetMachines(IReadOnlyList<CuttingMachine> machines)
    {
        _machines = CuttingMachine.Snapshot(machines);
        CuttingType.Choices = [.. CuttingMachine.LegacyNames, .. _machines.Select(machine => machine.Name)];
        CuttingType.SetChoiceNames(_machineNames);
        foreach (var item in BatchItems) { item.Form._machineNames = _machineNames; item.Form.SetMachines(machines); }
        OnPropertyChanged(nameof(HasPricing)); CalculatePriceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsHansLaser));
        ThicknessOptions.SourceChanged();
    }
    public async Task LoadMachinesAsync(CancellationToken cancellationToken = default)
    {
        if (_creator is not IMachineCatalogReader catalog) return;
        try
        {
            var snapshot = await MachineCatalogSnapshot.ReadAsync(catalog, cancellationToken);
            if (snapshot.NamesError is { } error) AppDiagnostics.Capture(DiagnosticOperation.PriceRead, error);
            _machineNames = snapshot.Names;
            SetMachines(snapshot.Machines);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { ImportError = AppDiagnostics.Describe(DiagnosticOperation.PriceRead, exception, "Не удалось загрузить каталог станков."); }
    }
}
