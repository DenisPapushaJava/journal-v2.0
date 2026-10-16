using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Runtime.Versioning;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

[SupportedOSPlatform("windows")]
public sealed class OleDbManualOrderCreator(string path) : IManualOrderCreator, IPriceReader
{
    public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default) =>
        OleDbPriceReader.ReadAsync(path, tableName, cancellationToken);

    public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(draft, cancellationToken), cancellationToken);

    private OrderSummary Create(ManualOrderDraft draft, CancellationToken cancellationToken)
    {
        draft.Validate();
        if (draft.CuttingDurationMinutes is not null)
            new OleDbOrderExtras(path).PrepareCuttingTimeAsync(cancellationToken).GetAwaiter().GetResult();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        OleDbConnection? connection = null;
        OleDbTransaction? transaction = null;
        var commitStarted = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetExtension(path), ".mdb", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new OrderWriteException(OrderWriteFailure.NotSaved, "Файл MDB недоступен. Проверьте путь и подключение к общей папке.");
            connection = OleDbAccessConnection.Create(path, writable: true);
            connection.Open();
            token.ThrowIfCancellationRequested();
            transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
            try
            {
                var table = OleDbPriceReader.Read(connection, transaction, CuttingPriceCalculator.SelectTable(draft.PriceRequest), token);
                var quote = draft.Calculate(table);
                var prepared = draft.ToOrder(0, quote.StoredPrice);
                token.ThrowIfCancellationRequested();
                using (var insert = Insert(connection, transaction, prepared))
                    if (insert.ExecuteNonQuery() != 1) throw new InvalidOperationException("INSERT не подтвердил одну новую заявку.");
                int id;
                using (var identity = connection.CreateCommand())
                {
                    identity.Transaction = transaction;
                    identity.CommandTimeout = 10;
                    identity.CommandText = "SELECT @@IDENTITY";
                    id = checked(Convert.ToInt32(identity.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
                    if (id <= 0) throw new InvalidOperationException("MDB не вернула код новой заявки.");
                }
                OleDbOrderExtras.Write(connection, transaction, id, null, prepared.CuttingTime);
                // До commit проверяем сохранённое представление. После commit к MDB больше не обращаемся.
                var saved = ReadCreated(connection, transaction, id);
                if (saved != prepared with { Id = id }) throw new InvalidOperationException("MDB изменила значения новой заявки.");
                token.ThrowIfCancellationRequested();
                commitStarted = true;
                transaction.Commit();
                return saved;
            }
            catch (Exception exception)
            {
                var rollbackFailed = false;
                try { transaction.Rollback(); }
                catch (Exception rollback) { rollbackFailed = true; Trace.TraceError("MDB create rollback failed: {0}", rollback); }
                if (commitStarted || rollbackFailed)
                    throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown,
                        "Не удалось подтвердить добавление заявки. Она могла сохраниться. Обновите список и проверьте его перед повтором, чтобы не создать дубликат.", exception);
                throw;
            }
        }
        catch (OrderWriteException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException exception)
        {
            throw new OrderWriteException(OrderWriteFailure.NotSaved, "Превышено время добавления. Заявка не сохранена. Проверьте подключение к общей папке.", exception);
        }
        catch (Exception exception) when (exception is OleDbException or IOException or UnauthorizedAccessException or InvalidOperationException or OrderReadException or OverflowException)
        {
            throw new OrderWriteException(OrderWriteFailure.NotSaved,
                "Не удалось добавить заявку в MDB. Ввод сохранён. Проверьте права, подключение к общей папке и блокировки Access.", exception);
        }
        finally { Release(transaction); Release(connection); }
    }

    private static OleDbCommand Insert(OleDbConnection connection, OleDbTransaction transaction, OrderSummary row)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 10;
        command.CommandText = "INSERT INTO [Orders] ([Дата], [Менеджер], [Спецификация], [Заказчик], [Вид_резки], [Марка_стали], " +
            "[Размер_листа], [Кол_во_листов], [Стоимость], [Толщина], [Ход], [Вес], [БЛЦБ], [Color], [Коэффициент]) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
        Add(command, OleDbType.Date, row.StoredOrderDateTime);
        foreach (var text in new[] { row.ManagerName, row.SpecificationNumber, row.CustomerName, row.CuttingType,
                     row.MaterialName, row.SheetSize, row.SheetCount }) Add(command, OleDbType.VarWChar, text);
        Add(command, OleDbType.Currency, row.StoredCuttingPrice);
        Add(command, OleDbType.Double, (double)row.ThicknessMillimeters!.Value);
        Add(command, OleDbType.Integer, row.CutLengthMeters);
        Add(command, OleDbType.VarWChar, row.WeightKilograms);
        Add(command, OleDbType.VarWChar, row.DeliveryNumber);
        Add(command, OleDbType.Integer, row.Color);
        Add(command, OleDbType.VarWChar, row.StoredCoefficient);
        return command;
    }

    private static void Add(OleDbCommand command, OleDbType type, object? value)
    {
        var parameter = command.Parameters.Add("p" + command.Parameters.Count, type);
        if (type == OleDbType.VarWChar) parameter.Size = 255;
        parameter.Value = value ?? DBNull.Value;
    }

    private static OrderSummary ReadCreated(OleDbConnection connection, OleDbTransaction transaction, int id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 10;
        command.CommandText = "SELECT * FROM [Orders] WHERE [Код] = ?";
        Add(command, OleDbType.Integer, id);
        using var reader = command.ExecuteReader(CommandBehavior.SingleResult)!;
        if (!reader.Read()) throw new InvalidOperationException("Новая заявка не найдена до commit.");
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        AccessOrderMapper.ValidateColumns(columns);
        var row = AccessOrderMapper.Map(columns.Select((name, index) => (name, value: (object?)reader.GetValue(index)))
            .ToDictionary(pair => pair.name, pair => pair.value, StringComparer.Ordinal));
        reader.Close();
        return row with { CuttingTime = OleDbOrderExtras.Read(connection, transaction, id) };
    }

    private static void Release(IDisposable? value)
    {
        try { value?.Dispose(); }
        catch (Exception exception) { Trace.TraceError("MDB create cleanup failed: {0}", exception); }
    }
}
