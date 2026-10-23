using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

public static class MachineLabelsData
{
    public const string TableName = "JournalMachineLabels";
    public static readonly string[] Columns = ["MachineKey", "DisplayName", "FormatVersion"];
    public static KeyValuePair<string, string> Map(object key, object name, object version)
    {
        if (key is not string id || id.Length > 64 || name is not string label || !MachineSettings.IsValidName(label) ||
            version is null or DBNull || Convert.ToString(version, System.Globalization.CultureInfo.InvariantCulture) != "1")
            throw new PriceReadException("Неизвестная запись названия станка. Справочник не изменён.");
        return new(id, label);
    }
    public static IReadOnlyDictionary<string, string> ReadExport(TextReader reader, CancellationToken token = default)
    {
        using var rows = MdbToolsTableExport.ReadCsv(reader, token, tableName: TableName).GetEnumerator();
        if (!rows.MoveNext()) throw new PriceReadException("Не прочитана структура названий станков.");
        var names = rows.Current.Select(cell => cell.Text).ToArray();
        if (names.Length != 3 || !names.ToHashSet(StringComparer.Ordinal).SetEquals(Columns)) throw new PriceReadException("Неизвестная структура названий станков.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (rows.MoveNext())
        {
            if (result.Count >= 132) throw new PriceReadException("Слишком много записей названий станков.");
            if (rows.Current.Count != 3) throw new PriceReadException("Повреждена запись названия станка.");
            string Field(string name) => rows.Current[Array.IndexOf(names, name)].Text;
            var pair = Map(Field("MachineKey"), Field("DisplayName"), Field("FormatVersion"));
            if (!result.TryAdd(pair.Key, pair.Value)) throw new PriceReadException("Повторяющийся ключ станка.");
        }
        return result;
    }
}
