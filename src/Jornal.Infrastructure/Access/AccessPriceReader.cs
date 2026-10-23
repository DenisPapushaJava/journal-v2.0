using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Diagnostics;

namespace Jornal.Infrastructure.Access;

public sealed class AccessPriceReader(string path) : IPriceReader, IMachineCatalogReader, IMachineSettingsReader
{
    public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        await AccessDatabaseFile.ValidateAsync(path, cancellationToken);
        if (AccessHostClient.IsBundled) return await new AccessHostClient(path).ReadMachineSettingsAsync(cancellationToken);
        if (OperatingSystem.IsWindows()) return await new OleDbMachineSettings(path).ReadMachineSettingsAsync(cancellationToken);
        if (!OperatingSystem.IsLinux()) throw new PriceReadException("Чтение названий станков реализовано для Windows и Linux.");
        var machines = await MdbToolsMachineCatalog.ReadAsync(path, cancellationToken);
        var export = new MdbToolsTableExport();
        var tables = await export.ListTablesAsync(path, cancellationToken);
        var labels = tables.Contains(MachineLabelsData.TableName, StringComparer.OrdinalIgnoreCase)
            ? await export.ReadAsync(path, MachineLabelsData.TableName, MachineLabelsData.ReadExport, cancellationToken) : null;
        return MachineSettings.Build(machines, labels);
    }, cancellationToken);
    public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default) =>
        DiagnosticReads.ObserveAsync(DiagnosticOperation.PriceRead, DiagnosticReadAction.ReadMachineCatalog, path, () => Task.Run(async () =>
    {
        try
        {
            await AccessDatabaseFile.ValidateAsync(path, cancellationToken);
            if (AccessHostClient.IsBundled) return await new AccessHostClient(path).ReadMachinesAsync(cancellationToken);
            if (OperatingSystem.IsWindows()) return await new OleDbMachineCatalog(path).ReadMachinesAsync(cancellationToken);
            if (OperatingSystem.IsLinux()) return await MdbToolsMachineCatalog.ReadAsync(path, cancellationToken);
            throw new PriceReadException("Чтение каталога реализовано для Windows и Linux.");
        }
        catch (Exception exception) when (exception is OrderReadException or IOException or UnauthorizedAccessException or ArgumentException)
        { throw new PriceReadException(DataSourceAccessErrors.Describe(path, exception, "Не удалось прочитать каталог станков."), exception); }

    }, cancellationToken));
    public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
    {
        PriceTables.ValidateName(tableName);
        return DiagnosticReads.ObserveAsync(DiagnosticOperation.PriceRead, DiagnosticReadAction.ReadPriceTable, path, () => Task.Run(async () =>
        {
            try
            {
                await AccessDatabaseFile.ValidateAsync(path, cancellationToken);
                if (AccessHostClient.IsBundled) return await new AccessHostClient(path).ReadPriceTableAsync(tableName, cancellationToken);
                if (OperatingSystem.IsWindows()) return await OleDbPriceReader.ReadAsync(path, tableName, cancellationToken);
                if (OperatingSystem.IsLinux()) return await new MdbToolsPriceReader().ReadAsync(path, tableName, cancellationToken);
                throw new PriceReadException("Чтение прайсов MDB реализовано для Windows и Linux.");
            }
            catch (Exception exception) when (exception is OrderReadException or IOException or UnauthorizedAccessException)
            {
                throw new PriceReadException(DataSourceAccessErrors.Describe(path, exception,
                    "Не удалось прочитать прайс " + tableName + ". " + exception.Message), exception);
            }
        }, cancellationToken));
    }
}
