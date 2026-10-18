using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

public sealed class AccessPriceReader(string path) : IPriceReader, IMachineCatalogReader
{
    public Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
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
        { throw new PriceReadException("Не удалось прочитать каталог станков.", exception); }

    }, cancellationToken);
    public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
    {
        PriceTables.ValidateName(tableName);
        return Task.Run(async () =>
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
                throw new PriceReadException("Не удалось прочитать прайс " + tableName + ". " + exception.Message, exception);
            }
        }, cancellationToken);
    }
}
