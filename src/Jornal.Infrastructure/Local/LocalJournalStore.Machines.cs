using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Local;

public sealed partial class LocalJournalStore : IMachineCatalogReader, IMachineCreator
{
    public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<CuttingMachine>>(book => CuttingMachine.Snapshot(book.Machines ?? []), cancellationToken);
    public Task<CuttingMachine> CreateMachineAsync(MachineCreation creation, CancellationToken cancellationToken = default)
    {
        var snapshot = creation.Snapshot();
        return WriteAsync(book =>
        {
            if (!(book.Machines ?? []).OrderBy(machine => machine.TableName, StringComparer.Ordinal)
                    .SequenceEqual(snapshot.ExpectedMachines.OrderBy(machine => machine.TableName, StringComparer.Ordinal))) throw MachineCreation.Conflict();
            return (book with { Machines = [.. book.Machines ?? [], snapshot.Machine],
                Prices = [.. book.Prices, new(snapshot.Machine.TableName, true, snapshot.Rates)] }, snapshot.Machine);
        }, cancellationToken);
    }
}
