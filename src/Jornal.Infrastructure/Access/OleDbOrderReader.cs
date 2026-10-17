using Jornal.Infrastructure.Diagnostics;
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
        Task.Run<IReadOnlyList<OrderSummary>>(() => DiagnosticStages.Run(DiagnosticStage.ReadOrders, () => Read(path, cancellationToken)), cancellationToken);

    public static string? FindProvider()
    {
        var providers = Environment.Is64BitProcess
            ? new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" }
            : ["Microsoft.Jet.OLEDB.4.0", "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0"];
        foreach (var provider in providers)
            if ((provider != "Microsoft.Jet.OLEDB.4.0" || !Environment.Is64BitProcess) && Type.GetTypeFromProgID(provider) is not null)
                return provider;
        return null;
    }

    private static IReadOnlyList<OrderSummary> Read(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = DiagnosticStages.Run(DiagnosticStage.OpenDatabase, () => OleDbAccessConnection.Create(path, writable: false));
            DiagnosticStages.Run(DiagnosticStage.OpenDatabase, connection.Open);
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
            reader.Close();
            var times = OleDbOrderExtras.ReadAll(connection);
            return orders.Select(row => row with { CuttingTime = times.GetValueOrDefault(row.Id) }).ToArray();
        }
        catch (Exception exception) when (exception is OleDbException or InvalidOperationException)
        {
            throw new OrderReadException("Access не удалось прочитать Orders. Проверьте доступ к файлу, структуру базы, пароль и совместимость драйвера. Список не заменён.", exception);
        }
    }
}
