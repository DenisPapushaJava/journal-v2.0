using Jornal.Core.Pricing;
using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

public sealed partial class AccessHostClient
{
    public async Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync(new(1, AccessHostOperation.ReadMachineSettings, System.IO.Path.GetFullPath(path)), cancellationToken)).Settings!;
    public async Task PrepareMachineLabelsAsync(CancellationToken cancellationToken = default)
    {
        try { await SendAsync(new(1, AccessHostOperation.PrepareMachineLabels, System.IO.Path.GetFullPath(path)), cancellationToken); }
        catch (OrderWriteException exception)
        { throw new OrderWriteException(OrderWriteFailure.NotSaved, "Не удалось подготовить названия станков. Изменение параметров не отправлено. " + exception.Message, exception); }
    }
    public async Task<MachineSettings> UpdateMachineSettingsAsync(MachineSettingsChange change, CancellationToken cancellationToken = default)
    {
        var snapshot = change.Snapshot();
        if (!snapshot.HasChanges) return snapshot.Expected;
        await PrepareMachineLabelsAsync(cancellationToken);
        return (await SendAsync(new(1, AccessHostOperation.UpdateMachineSettings, System.IO.Path.GetFullPath(path), MachineSettingsChange: snapshot), cancellationToken)).Settings![0];
    }
    public async Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync(new(1, AccessHostOperation.ReadMachines, System.IO.Path.GetFullPath(path)), cancellationToken)).Machines!;
    public async Task<CuttingMachine> UpdateMachineImportAsync(MachineImportChange change, CancellationToken cancellationToken = default)
    {
        var snapshot = change.Snapshot();
        if (!snapshot.HasChanges) return snapshot.Values;
        return (await SendAsync(new(1, AccessHostOperation.UpdateMachineImport, System.IO.Path.GetFullPath(path), MachineImportChange: snapshot), cancellationToken)).Machine!;
    }
    public async Task<CuttingMachine> CreateMachineAsync(MachineCreation creation, CancellationToken cancellationToken = default)
    {
        var snapshot = creation.Snapshot();
        return (await SendAsync(new(1, AccessHostOperation.CreateMachine, System.IO.Path.GetFullPath(path), MachineCreation: snapshot), cancellationToken)).Machine!;
    }
}
