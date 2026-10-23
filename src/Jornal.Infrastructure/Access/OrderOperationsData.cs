using System.Globalization;
using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

public static class OrderOperationsData
{
    public const string TableName = "JournalOrderOperations";
    public static readonly string[] Columns = ["OrderId", "OperationCode", "IdentityHash", "FormatVersion"];
    public static (int Id, OrderOperations Value) Map(object id, object code, object hash, object version)
    {
        try
        {
            var number = Convert.ToDecimal(id, CultureInfo.InvariantCulture);
            if (number <= 0 || number != decimal.Truncate(number) || Convert.ToDecimal(version, CultureInfo.InvariantCulture) != 1 ||
                code is not string name || name != "Chamfer") throw new FormatException();
            var value = new OrderOperations(OrderOperation.Chamfer, (string)hash); value.Validate();
            return (checked((int)number), value);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or OrderWriteException)
        { throw new OrderReadException("Не удалось прочитать дополнительные операции. Неизвестная операция не заменена пустой.", exception); }
    }
    public static IReadOnlyDictionary<int, OrderOperations> ReadExport(TextReader reader, CancellationToken token = default)
    {
        using var rows = MdbToolsTableExport.ReadCsv(reader, token, tableName: TableName).GetEnumerator();
        if (!rows.MoveNext()) throw new OrderReadException("Не прочитана структура дополнительных операций.");
        var names = rows.Current.Select(value => value.Text).ToArray();
        if (names.Length != Columns.Length || !names.ToHashSet(StringComparer.Ordinal).SetEquals(Columns))
            throw new OrderReadException("Неизвестная структура дополнительных операций.");
        var result = new Dictionary<int, OrderOperations>();
        while (rows.MoveNext())
        {
            if (rows.Current.Count != Columns.Length) throw new OrderReadException("Повреждена строка дополнительных операций.");
            string Field(string name) => rows.Current[Array.IndexOf(names, name)].Text;
            var (id, value) = Map(Field("OrderId"), Field("OperationCode"), Field("IdentityHash"), Field("FormatVersion"));
            if (!result.TryAdd(id, value)) throw new OrderReadException("Повторяющаяся дополнительная операция.");
        }
        return result;
    }
}
