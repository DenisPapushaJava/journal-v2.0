using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Demo;

public sealed partial class DemoOrderStore : IMachineSettingsReader, IMachineSettingsEditor
{
    private readonly Dictionary<string, string> _machineLabels = new(StringComparer.Ordinal);
    public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default)
    { lock (_gate) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(MachineSettings.Build(_machines, _machineLabels)); } }
    public Task<MachineSettings> UpdateMachineSettingsAsync(MachineSettingsChange change, CancellationToken cancellationToken = default)
    {
        var snapshot = change.Snapshot();
        if (!snapshot.HasChanges) return Task.FromResult(snapshot.Expected);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); snapshot.Verify(MachineSettings.Build(_machines, _machineLabels));
            if (snapshot.Expected.CanEditImport)
            {
                var index = _machines.FindIndex(machine => machine.TableName == snapshot.Expected.Key);
                _machines[index] = _machines[index] with { SpecificationProfileId = snapshot.SpecificationProfileId };
            }
            if (snapshot.DisplayName == snapshot.Expected.Name) _machineLabels.Remove(snapshot.Expected.Key);
            else _machineLabels[snapshot.Expected.Key] = snapshot.DisplayName;
            return Task.FromResult(snapshot.Values);
        }
    }
}
