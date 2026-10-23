using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Runtime.Versioning;
using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

[SupportedOSPlatform("windows")]
public sealed class OleDbOrderOperations(string path) : IOrderOperationsInitializer
{
    public Task PrepareOperationsAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            var backupMade = false;
            bool Ready()
            {
                using var probe = OleDbAccessConnection.Create(path, writable: false); probe.Open();
                if (!Exists(probe)) return false;
                Validate(probe, null); return true;
            }
            void Prepare()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!backupMade) { Backup(cancellationToken); backupMade = true; }
                using var connection = OleDbAccessConnection.Create(path, writable: true, exclusive: true); connection.Open();
                if (Exists(connection)) { Validate(connection, null); return; }
                using var check = Command(connection, null); check.CommandText = "SELECT * FROM [Orders] WHERE 1=0";
                using (var reader = check.ExecuteReader()!)
                {
                    if (reader.FieldCount != AccessOrderMapper.Columns.Count) throw new OrderReadException("Нужна исходная Orders из 16 полей.");
                    AccessOrderMapper.ValidateColumns(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
                }
                cancellationToken.ThrowIfCancellationRequested();
                using var create = Command(connection, null);
                create.CommandText = $"CREATE TABLE [{OrderOperationsData.TableName}] ([OrderId] LONG NOT NULL, [OperationCode] TEXT(32) NOT NULL, " +
                    "[IdentityHash] TEXT(64) NOT NULL, [FormatVersion] SHORT NOT NULL, CONSTRAINT [PK_JournalOrderOperations] PRIMARY KEY ([OrderId],[OperationCode]))";
                create.ExecuteNonQuery(); Validate(connection, null);
            }
            await SharedSchemaPreparation.EnsureAsync(Ready, Prepare, AccessSchemaContention.IsBusy, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OleDbException or IOException or UnauthorizedAccessException or OrderReadException or InvalidOperationException)
        {
            throw new OrderWriteException(OrderWriteFailure.NotSaved,
                DataSourceAccessErrors.Describe(path, exception,
                    "Не удалось подготовить хранение фаски. Обновите список (F5). Если база занята, повторите после завершения работы других программ; проверьте права на базу и её папку."), exception);
        }
    }, cancellationToken);

    private void Backup(CancellationToken token)
    {
        var backup = Path.GetFullPath(path) + ".before-journal-operations-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".bak";
        var partial = backup + ".partial";
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { source.CopyTo(target); target.Flush(flushToDisk: true); }
            token.ThrowIfCancellationRequested(); File.Move(partial, backup);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    internal static bool Exists(OleDbConnection connection)
    {
        using var tables = connection.GetSchema("Tables", [null, null, OrderOperationsData.TableName, "TABLE"]);
        return tables.Rows.Count > 0;
    }
    private static void Validate(OleDbConnection connection, OleDbTransaction? transaction)
    {
        using var command = Command(connection, transaction); command.CommandText = $"SELECT * FROM [{OrderOperationsData.TableName}] WHERE 1=0";
        using var reader = command.ExecuteReader()!;
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        Type[] types = [typeof(int), typeof(string), typeof(string), typeof(short)];
        if (names.Length != OrderOperationsData.Columns.Length || !names.ToHashSet(StringComparer.Ordinal).SetEquals(OrderOperationsData.Columns) ||
            OrderOperationsData.Columns.Where((name, index) => reader.GetFieldType(reader.GetOrdinal(name)) != types[index]).Any())
            throw new OrderReadException("Таблица дополнительных операций имеет неизвестную структуру. Она не изменена.");
        reader.Close();
        using var primary = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, [null, null, OrderOperationsData.TableName]);
        if (primary is null || primary.Rows.Count != 2 || !primary.Rows.Cast<DataRow>().Select(row => (string)row["COLUMN_NAME"]).ToHashSet(StringComparer.Ordinal).SetEquals(["OrderId", "OperationCode"]))
            throw new OrderReadException("Неизвестный ключ дополнительных операций.");
        using var columns = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, OrderOperationsData.TableName, null]);
        foreach (var (name, size) in new[] { ("OperationCode", 32), ("IdentityHash", 64) })
        {
            var column = columns?.Rows.Cast<DataRow>().SingleOrDefault(row => (string)row["COLUMN_NAME"] == name);
            if (column is null || Convert.ToInt32(column["CHARACTER_MAXIMUM_LENGTH"], CultureInfo.InvariantCulture) != size)
                throw new OrderReadException("Неизвестная длина поля дополнительных операций.");
        }
    }
    internal static IReadOnlyDictionary<int, OrderOperations> ReadAll(OleDbConnection connection, OleDbTransaction? transaction = null, DateOnly? date = null)
    {
        if (!Exists(connection)) return new Dictionary<int, OrderOperations>();
        Validate(connection, transaction);
        using var command = Command(connection, transaction);
        command.CommandText = date is null ? $"SELECT * FROM [{OrderOperationsData.TableName}]" :
            $"SELECT [e].* FROM [{OrderOperationsData.TableName}] AS [e] INNER JOIN [Orders] AS [o] ON [e].[OrderId]=[o].[Код]" + OrderDayQuery.Where(command, date, "[o].[Дата]");
        using var reader = command.ExecuteReader()!; var result = new Dictionary<int, OrderOperations>();
        while (reader.Read())
        {
            var (id, value) = Map(reader);
            if (!result.TryAdd(id, value)) throw new OrderReadException("Повторяющаяся дополнительная операция.");
        }
        return result;
    }
    internal static OrderOperations? ReadOne(OleDbConnection connection, OleDbTransaction? transaction, int id)
        => new Scope(connection, transaction).ReadOne(id);

    // Живёт только внутри одной операции/транзакции. Не переносится между
    // подключениями: новая операция снова проверяет фактическую схему MDB.
    internal sealed class Scope(OleDbConnection connection, OleDbTransaction? transaction)
    {
        private bool? _tableExists;
        private bool HasTable()
        {
            if (_tableExists is { } exists) return exists;
            exists = Exists(connection);
            if (exists) Validate(connection, transaction);
            _tableExists = exists;
            return exists;
        }
        internal OrderOperations? ReadOne(int id)
        {
            if (!HasTable()) return null;
            using var command = Command(connection, transaction); command.CommandText = $"SELECT * FROM [{OrderOperationsData.TableName}] WHERE [OrderId]=?";
            Add(command, OleDbType.Integer, id);
            using var reader = command.ExecuteReader()!;
            if (!reader.Read()) return null;
            var (_, value) = Map(reader); if (reader.Read()) throw new OrderReadException("Неизвестные или повторяющиеся дополнительные операции."); return value;
        }
        internal void Write(int id, OrderOperations? expected, OrderOperations? value)
        {
            if (transaction is null) throw new InvalidOperationException("Запись операций требует транзакции.");
            value?.Validate();
            if (!HasTable())
            {
                if (expected is null && value is null) return;
                throw new OrderWriteException(OrderWriteFailure.NotSaved, "Хранение фаски ещё не подготовлено. Обновите список (F5).");
            }
            if (ReadOne(id) != expected) throw OrderWriteException.Conflict();
            if (value == expected) return;
            using var command = Command(connection, transaction);
            if (expected is not null)
            {
                command.CommandText = $"DELETE FROM [{OrderOperationsData.TableName}] WHERE [OrderId]=? AND [OperationCode]='Chamfer' AND StrComp([IdentityHash],?,0)=0 AND [FormatVersion]=1";
                Add(command, OleDbType.Integer, id); Add(command, OleDbType.VarWChar, expected.IdentityHash, 64);
                if (command.ExecuteNonQuery() != 1) throw OrderWriteException.Conflict();
                command.Parameters.Clear();
            }
            if (value is null) return;
            command.CommandText = $"INSERT INTO [{OrderOperationsData.TableName}] ([OrderId],[OperationCode],[IdentityHash],[FormatVersion]) VALUES (?,'Chamfer',?,1)";
            Add(command, OleDbType.Integer, id); Add(command, OleDbType.VarWChar, value.IdentityHash, 64);
            if (command.ExecuteNonQuery() != 1) throw OrderWriteException.Conflict();
        }
    }
    private static (int, OrderOperations) Map(OleDbDataReader reader) => OrderOperationsData.Map(reader["OrderId"], reader["OperationCode"], reader["IdentityHash"], reader["FormatVersion"]);
    private static OleDbCommand Command(OleDbConnection connection, OleDbTransaction? transaction)
    { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 10; return command; }
    private static void Add(OleDbCommand command, OleDbType type, object value, int size = 32)
    { var parameter = command.Parameters.Add("p" + command.Parameters.Count, type); if (type == OleDbType.VarWChar) parameter.Size = size; parameter.Value = value; }
}
