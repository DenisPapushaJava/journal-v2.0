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
            await AccessDatabaseFile.ValidateAsync(path, cancellationToken);
            IReadOnlyList<OrderSummary> orders;
            if (AccessHostClient.IsBundled) orders = await new AccessHostClient(path).ReadAsync(cancellationToken);
            else if (OperatingSystem.IsWindows()) orders = await OleDbOrderReader.ReadAsync(path, cancellationToken);
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
