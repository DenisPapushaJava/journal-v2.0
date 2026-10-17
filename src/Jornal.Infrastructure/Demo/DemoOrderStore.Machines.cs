using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Demo;

public sealed partial class DemoOrderStore
{
    private readonly List<CuttingMachine> _machines = [];
    public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<CuttingMachine>>(_machines.ToArray()); }
    }
    private Task<CuttingMachine> CreateMachineAsync(MachineCreation creation, CancellationToken cancellationToken)
    {
        var snapshot = creation.Snapshot();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_machines.OrderBy(machine => machine.TableName).SequenceEqual(snapshot.ExpectedMachines.OrderBy(machine => machine.TableName))) throw MachineCreation.Conflict();
            _prices.Add(snapshot.Machine.TableName, new(snapshot.Machine.TableName, true, snapshot.Rates));
            _machines.Add(snapshot.Machine); return Task.FromResult(snapshot.Machine);
        }
    }
}
