using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Local;

public sealed partial class LocalJournalStore : IMachineSettingsReader, IMachineSettingsEditor
{
    public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(book => MachineSettings.Build(book.Machines ?? [], book.MachineLabels), cancellationToken);
    public Task<MachineSettings> UpdateMachineSettingsAsync(MachineSettingsChange change, CancellationToken cancellationToken = default)
    {
        var snapshot = change.Snapshot();
        if (!snapshot.HasChanges) return Task.FromResult(snapshot.Expected);
        return WriteAsync(book =>
        {
            snapshot.Verify(MachineSettings.Build(book.Machines ?? [], book.MachineLabels));
            var labels = new Dictionary<string, string>(book.MachineLabels ?? [], StringComparer.Ordinal);
            if (snapshot.DisplayName == snapshot.Expected.Name) labels.Remove(snapshot.Expected.Key);
            else labels[snapshot.Expected.Key] = snapshot.DisplayName;
            var machines = (book.Machines ?? []).Select(machine => machine.TableName == snapshot.Expected.Key
                ? machine with { SpecificationProfileId = snapshot.SpecificationProfileId } : machine).ToArray();
            return (book with { Machines = machines, MachineLabels = labels }, snapshot.Values);
        }, cancellationToken);
    }
}
