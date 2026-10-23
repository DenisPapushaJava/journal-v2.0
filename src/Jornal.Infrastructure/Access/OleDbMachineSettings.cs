using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.Runtime.Versioning;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Diagnostics;

namespace Jornal.Infrastructure.Access;

[SupportedOSPlatform("windows")]
public sealed class OleDbMachineSettings(string path) : IMachineSettingsReader, IMachineSettingsEditor, IMachineLabelsInitializer
{
    public Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OleDbAccessConnection.Create(path, false); connection.Open();
        return Read(connection, null, cancellationToken);
    }, cancellationToken);

    internal static IReadOnlyDictionary<string, string> Labels(OleDbConnection connection, OleDbTransaction? transaction = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!OleDbMachineCatalog.Exists(connection, MachineLabelsData.TableName)) return result;
        Validate(connection, transaction);
        using var command = Command(connection, transaction); command.CommandText = "SELECT * FROM [JournalMachineLabels]";
        using var reader = command.ExecuteReader()!;
        while (reader.Read())
        {
            if (result.Count >= 132) throw new PriceReadException("Слишком много записей названий станков.");
            var pair = MachineLabelsData.Map(reader["MachineKey"], reader["DisplayName"], reader["FormatVersion"]);
            if (!result.TryAdd(pair.Key, pair.Value)) throw new PriceReadException("Повторяющийся ключ названия станка.");
        }
        return result;
    }
    private static IReadOnlyList<MachineSettings> Read(OleDbConnection connection, OleDbTransaction? transaction, CancellationToken token) =>
        MachineSettings.Build(OleDbMachineCatalog.Read(connection, transaction, token), Labels(connection, transaction));

    public Task PrepareMachineLabelsAsync(CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        try
        {
            var backupMade = false;
            bool Ready()
            {
                using var connection = OleDbAccessConnection.Create(path, false); connection.Open();
                if (!OleDbMachineCatalog.Exists(connection, MachineLabelsData.TableName)) return false;
                Validate(connection, null); return true;
            }
            void Prepare()
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!backupMade) { Backup(cancellationToken); backupMade = true; }
                using var connection = OleDbAccessConnection.Create(path, true, exclusive: true); connection.Open();
                if (OleDbMachineCatalog.Exists(connection, MachineLabelsData.TableName)) { Validate(connection, null); return; }
                using var check = Command(connection, null); check.CommandText = "SELECT * FROM [Orders] WHERE 1=0";
                using (var reader = check.ExecuteReader()!)
                {
                    if (reader.FieldCount != AccessOrderMapper.Columns.Count) throw new PriceReadException("Нужна исходная структура Orders.");
                    AccessOrderMapper.ValidateColumns(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
                }
                using var create = Command(connection, null);
                create.CommandText = "CREATE TABLE [JournalMachineLabels] ([MachineKey] TEXT(64) NOT NULL PRIMARY KEY, [DisplayName] TEXT(255) NOT NULL, [FormatVersion] SHORT NOT NULL, CONSTRAINT [UX_JournalMachineLabel] UNIQUE ([DisplayName]))";
                cancellationToken.ThrowIfCancellationRequested(); create.ExecuteNonQuery(); Validate(connection, null);
            }
            await SharedSchemaPreparation.EnsureAsync(Ready, Prepare, AccessSchemaContention.IsBusy, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OleDbException or IOException or UnauthorizedAccessException or PriceReadException or OrderReadException or ArgumentException or InvalidOperationException)
        { throw new OrderWriteException(OrderWriteFailure.NotSaved, DataSourceAccessErrors.Describe(path, exception,
            "Не удалось подготовить названия станков. Обновите список (F5); если база занята, повторите после завершения других операций. Существующие данные не изменены."), exception); }
    }, cancellationToken);

    public async Task<MachineSettings> UpdateMachineSettingsAsync(MachineSettingsChange change, CancellationToken cancellationToken = default)
    {
        var snapshot = change.Snapshot();
        if (!snapshot.HasChanges) return snapshot.Expected;
        await PrepareMachineLabelsAsync(cancellationToken);
        return await Task.Run(() => Write(snapshot, cancellationToken), cancellationToken);
    }
    private MachineSettings Write(MachineSettingsChange change, CancellationToken token)
    {
        using var connection = OleDbAccessConnection.Create(path, true);
        OleDbTransaction? transaction = null; var uncertain = false;
        try
        {
            token.ThrowIfCancellationRequested(); DiagnosticStages.Run(DiagnosticStage.OpenDatabase, connection.Open);
            transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            var current = Read(connection, transaction, token); change.Verify(current);
            using var command = Command(connection, transaction);
            if (change.DisplayName != change.Expected.DisplayName && change.Expected.DisplayName != change.Expected.Name)
            {
                command.CommandText = "DELETE FROM [JournalMachineLabels] WHERE [MachineKey]=? AND StrComp([DisplayName],?,0)=0 AND [FormatVersion]=1";
                command.Parameters.Add("", OleDbType.VarWChar, 64).Value = change.Expected.Key;
                command.Parameters.Add("", OleDbType.VarWChar, 255).Value = change.Expected.DisplayName;
                if (command.ExecuteNonQuery() != 1) throw MachineCreation.Conflict(); command.Parameters.Clear();
            }
            if (change.DisplayName != change.Expected.DisplayName && change.DisplayName != change.Expected.Name)
            {
                command.CommandText = "INSERT INTO [JournalMachineLabels] ([MachineKey],[DisplayName],[FormatVersion]) VALUES (?,?,1)";
                command.Parameters.Add("", OleDbType.VarWChar, 64).Value = change.Expected.Key;
                command.Parameters.Add("", OleDbType.VarWChar, 255).Value = change.DisplayName;
                if (command.ExecuteNonQuery() != 1) throw MachineCreation.Conflict(); command.Parameters.Clear();
            }
            if (change.Expected.CanEditImport && change.Expected.SpecificationProfileId != change.SpecificationProfileId)
            {
                command.CommandText = "UPDATE [JournalMachines] SET [SpecificationProfileId]=? WHERE [PriceTableName]=? AND StrComp([Name],?,0)=0 AND " +
                    (change.Expected.SpecificationProfileId is null ? "[SpecificationProfileId] IS NULL" : "StrComp([SpecificationProfileId],?,0)=0");
                command.Parameters.Add("", OleDbType.VarWChar, 64).Value = (object?)change.SpecificationProfileId ?? DBNull.Value;
                command.Parameters.Add("", OleDbType.VarWChar, 64).Value = change.Expected.Key;
                command.Parameters.Add("", OleDbType.VarWChar, 255).Value = change.Expected.Name;
                if (change.Expected.SpecificationProfileId is { } profile) command.Parameters.Add("", OleDbType.VarWChar, 64).Value = profile;
                if (command.ExecuteNonQuery() != 1) throw MachineCreation.Conflict();
            }
            var saved = Read(connection, transaction, token).Single(row => row.Key == change.Expected.Key);
            if (saved != change.Values) throw MachineCreation.Conflict();
            token.ThrowIfCancellationRequested(); uncertain = true; transaction.Commit(); return saved;
        }
        catch (Exception exception)
        {
            if (transaction is not null) try { transaction.Rollback(); } catch (Exception) { uncertain = true; }
            if (uncertain) throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Сохранение параметров станка не подтверждено. Обновите станок перед повтором.", exception);
            if (exception is OrderWriteException) throw;
            if (exception is OleDbException oledb && oledb.Errors.Cast<OleDbError>().Any(error => error.NativeError == 3022)) throw MachineCreation.Conflict();
            if (exception is OperationCanceledException && token.IsCancellationRequested) throw;
            throw new OrderWriteException(OrderWriteFailure.NotSaved, DataSourceAccessErrors.Describe(path, exception, "Параметры станка не изменены. Проверьте название и повторите сохранение."), exception);
        }
        finally { try { transaction?.Dispose(); } catch (Exception) { } }
    }
    private static OleDbCommand Command(OleDbConnection connection, OleDbTransaction? transaction)
    { var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandTimeout = 10; return command; }
    private static void Validate(OleDbConnection connection, OleDbTransaction? transaction)
    {
        using var command = Command(connection, transaction); command.CommandText = "SELECT * FROM [JournalMachineLabels] WHERE 1=0";
        using var reader = command.ExecuteReader()!;
        if (reader.FieldCount != 3 || !Enumerable.Range(0, 3).Select(reader.GetName).ToHashSet(StringComparer.Ordinal).SetEquals(MachineLabelsData.Columns) ||
            reader.GetFieldType(reader.GetOrdinal("MachineKey")) != typeof(string) || reader.GetFieldType(reader.GetOrdinal("DisplayName")) != typeof(string) || reader.GetFieldType(reader.GetOrdinal("FormatVersion")) != typeof(short))
            throw new PriceReadException("Неизвестная структура названий станков. Она не изменена.");
        reader.Close();
        using var keys = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, [null, null, MachineLabelsData.TableName]);
        if (keys is null || keys.Rows.Count != 1 || (string)keys.Rows[0]["COLUMN_NAME"] != "MachineKey") throw new PriceReadException("Неизвестный ключ названий станков.");
        using var columns = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, MachineLabelsData.TableName, null]);
        foreach (var (name, size) in new[] { ("MachineKey", 64), ("DisplayName", 255) })
            if (columns?.Rows.Cast<DataRow>().SingleOrDefault(row => (string)row["COLUMN_NAME"] == name) is not { } column ||
                Convert.ToInt32(column["CHARACTER_MAXIMUM_LENGTH"], CultureInfo.InvariantCulture) != size) throw new PriceReadException("Неизвестная длина названия станка.");
        using var indexes = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, [null, null, null, null, MachineLabelsData.TableName]);
        if (indexes is null || !indexes.Rows.Cast<DataRow>().Any(row => row["UNIQUE"] is true && row["COLUMN_NAME"] is "DisplayName")) throw new PriceReadException("Неизвестное ограничение названий станков.");
    }
    private void Backup(CancellationToken token)
    {
        var backup = Path.GetFullPath(path) + ".before-machine-labels-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".bak";
        var partial = backup + ".partial";
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using (var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { source.CopyTo(target); target.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(partial, backup);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
