using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

public sealed class MdbToolsPriceReader(string? executable = null)
{
    public async Task<PriceTable> ReadAsync(string path, string tableName, CancellationToken cancellationToken = default)
    {
        PriceTables.ValidateName(tableName);
        try
        {
            // MDB Tools не экранирует запятые в числовых заголовках (0,5; 2,5).
            // Табуляция разделяет поля без попыток угадать исходные имена колонок.
            return await new MdbToolsTableExport(executable).ReadAsync(path, tableName,
                (reader, token) => AccessPriceMapper.ReadExport(tableName, reader, token, '\t'), cancellationToken, '\t');
        }
        catch (OrderReadException exception) { throw new PriceReadException("Не удалось прочитать прайс " + tableName + ". " + exception.Message, exception); }
    }
}
