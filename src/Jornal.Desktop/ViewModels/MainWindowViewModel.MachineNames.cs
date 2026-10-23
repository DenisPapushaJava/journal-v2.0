using Jornal.Core.Pricing;
using Jornal.Infrastructure.Diagnostics;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private IReadOnlyDictionary<string, string> _machineNames = new Dictionary<string, string>();
    private IMachineSettingsReader? _machineNamesSource;
    private Func<string, string>? _machineNameFormatter;
    private int _machineNamesVersion, _appliedMachineNamesVersion;
    // Порядок запросов не меняет версию данных и не перестраивает таблицу.
    private long _machineNamesRequest;
    private Func<string, string> MachineNameFormatter => _machineNameFormatter ??= MachineName;
    private bool NeedsMachineNameRefresh => !ReferenceEquals(_machineNamesSource, Prices as IMachineSettingsReader) || _machineNames.Count == 0;
    private string MachineName(string name) => _machineNames.GetValueOrDefault(name) ?? name;
    public async Task RefreshMachineNamesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = ++_machineNamesRequest;
        var source = Prices as IMachineSettingsReader;
        var (names, loaded) = await ReadMachineNamesAsync(source, cancellationToken);
        if (request != _machineNamesRequest || !ReferenceEquals(source, Prices as IMachineSettingsReader)) return;
        ApplyMachineNames(source, names, loaded);
    }

    private static async Task<(IReadOnlyDictionary<string, string> Names, bool Loaded)> ReadMachineNamesAsync(
        IMachineSettingsReader? source, CancellationToken cancellationToken)
    {
        var names = (IReadOnlyDictionary<string, string>)new Dictionary<string, string>();
        var loaded = false;
        try { if (source is not null) names = MachineSettings.Names(await source.ReadMachineSettingsAsync(cancellationToken)); loaded = true; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { AppDiagnostics.Capture(DiagnosticOperation.PriceRead, exception); }
        cancellationToken.ThrowIfCancellationRequested();
        return (names, loaded);
    }

    private void ApplyMachineNames(IMachineSettingsReader? source, IReadOnlyDictionary<string, string> names, bool loaded)
    {
        _machineNamesSource = loaded ? source : null;
        if (names.Count == _machineNames.Count && names.All(pair => _machineNames.GetValueOrDefault(pair.Key) == pair.Value)) return;
        _machineNames = names;
        _machineNamesVersion++;
        foreach (var row in _rowCache.Values.ToArray()) row.NotifyMachineName();
        if (_ordersLoaded && !IsLoadingOrders && !IsRefreshingOrders) ApplyReadSnapshot(_allOrders, _loadedDate, preserve: true);
        else NotifyFilterContextChanged();
    }
}
