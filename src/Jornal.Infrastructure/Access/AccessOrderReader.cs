using System.Text;
using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

public sealed class AccessOrderReader(string path) : IOrderReader
{
    public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
        // Само открытие сетевого файла тоже может ждать SMB, поэтому оно не выполняется в потоке UI.
        Task.Run(() => ReadCoreAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<OrderSummary>> ReadCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(path), ".mdb", StringComparison.OrdinalIgnoreCase))
                throw new OrderReadException("Выберите существующий файл Access с расширением .mdb.");
            // Только чтение заголовка; режим совместного доступа не мешает другим клиентам открывать файл.
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous))
            {
                var header = new byte[32];
                var length = await stream.ReadAtLeastAsync(header, 20, throwOnEndOfStream: false, cancellationToken);
                if (length < 20 || Encoding.ASCII.GetString(header, 4, 16) != "Standard Jet DB\0")
                    throw new OrderReadException("Файл не является поддерживаемой базой Jet MDB либо повреждён.");
            }
            IReadOnlyList<OrderSummary> orders;
            if (OperatingSystem.IsWindows()) orders = await OleDbOrderReader.ReadAsync(path, cancellationToken);
            else if (OperatingSystem.IsLinux()) orders = await new MdbToolsOrderReader().ReadAsync(path, cancellationToken);
            else throw new OrderReadException("Чтение MDB реализовано для Windows и Linux.");
            if (orders.Select(order => order.Id).Distinct().Count() != orders.Count)
                throw new OrderReadException("В Orders обнаружены повторяющиеся значения поля «Код». Список не загружен.");
            return orders.OrderByDescending(order => order.OrderDate).ThenByDescending(order => order.Id).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OrderReadException("Файл базы недоступен. Проверьте путь, подключение к общей папке и права на чтение.", exception);
        }
    }
}
