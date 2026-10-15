using System.Data;
using System.Data.OleDb;
using System.Runtime.Versioning;
using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

[SupportedOSPlatform("windows")]
public static class OleDbOrderReader
{
    public static Task<IReadOnlyList<OrderSummary>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
        // OLE DB синхронен: соединение и reader живут на одном рабочем потоке, UI не блокируется.
        Task.Run<IReadOnlyList<OrderSummary>>(() => Read(path, cancellationToken), cancellationToken);

    public static string? FindProvider()
    {
        foreach (var provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" })
            if ((provider != "Microsoft.Jet.OLEDB.4.0" || !Environment.Is64BitProcess) && Type.GetTypeFromProgID(provider) is not null)
                return provider;
        return null;
    }

    private static IReadOnlyList<OrderSummary> Read(string path, CancellationToken cancellationToken)
    {
        var provider = FindProvider() ?? throw new OrderReadException(
            $"Не найден драйвер Access для приложения {(Environment.Is64BitProcess ? "x64" : "x86")}. Нужен совместимый Microsoft Access Runtime. Выбор и установку проверяем отдельно с учётом Office.");
        var connectionString = new OleDbConnectionStringBuilder
        {
            Provider = provider, DataSource = Path.GetFullPath(path), PersistSecurityInfo = false
        };
        connectionString["Mode"] = "Read|Share Deny None";
        connectionString["OLE DB Services"] = -2; // Не удерживаем файловое соединение в пуле после обновления.
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = new OleDbConnection(connectionString.ConnectionString);
            connection.Open();
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM [Orders]";
            command.CommandTimeout = 30;
            using var reader = command.ExecuteReader(CommandBehavior.SingleResult)!;
            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            AccessOrderMapper.ValidateColumns(columns);
            var orders = new List<OrderSummary>();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                orders.Add(AccessOrderMapper.Map(columns.Select((name, index) => (name, value: reader.GetValue(index)))
                    .ToDictionary(item => item.name, item => (object?)item.value, StringComparer.Ordinal)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return orders;
        }
        catch (Exception exception) when (exception is OleDbException or InvalidOperationException)
        {
            throw new OrderReadException("Access не удалось прочитать Orders. Проверьте доступ к файлу, структуру базы, пароль и совместимость драйвера. Список не заменён.", exception);
        }
    }
}
