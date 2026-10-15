using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Jornal.Core.Orders;

namespace Jornal.Infrastructure.Access;

// Linux: чтение для разработки. Не участвует в записи и транзакциях Jet/ACE.
public sealed class MdbToolsOrderReader(string? executable = null)
{
    public async Task<IReadOnlyList<OrderSummary>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var start = new ProcessStartInfo(executable ?? Environment.GetEnvironmentVariable("JOURNAL_MDB_EXPORT") ?? "mdb-export")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-D", "%Y-%m-%dT%H:%M:%S", "-T", "%Y-%m-%dT%H:%M:%S", "-0", "NULL", Path.GetFullPath(path), "Orders" })
            start.ArgumentList.Add(argument);
        start.Environment["LC_ALL"] = "C";
        start.Environment["MDBICONV"] = "UTF-8";
        using var process = new Process { StartInfo = start };
        try
        {
            token.ThrowIfCancellationRequested();
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new OrderReadException("Для чтения MDB на Linux нужен пакет mdbtools (команда mdb-export). Установите его и повторите подключение.", exception);
        }
        using var cancellation = token.Register(() => Stop(process));
        try
        {
            var errors = process.StandardError.ReadToEndAsync(token);
            var orders = await Task.Run(() => ReadExport(process.StandardOutput, token), token);
            await process.WaitForExitAsync(token);
            var error = await errors;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(error))
                throw new OrderReadException("MDB Tools не удалось полностью прочитать Orders. Проверьте файл и структуру базы; список не заменён.",
                    new IOException($"mdb-export: exit={process.ExitCode}; {error}"));
            return orders;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OrderReadException("Превышено время чтения MDB. Проверьте доступ к общей папке и повторите обновление.");
        }
        finally { Stop(process); }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
    }

    public static IReadOnlyList<OrderSummary> ReadExport(TextReader reader, CancellationToken cancellationToken = default)
    {
        var records = ReadCsv(reader, cancellationToken).GetEnumerator();
        using (records)
        {
            if (!records.MoveNext()) throw new OrderReadException("MDB Tools не вернул структуру Orders. Проверьте файл базы.");
            var columns = records.Current.Select(value => value.Text).ToArray();
            AccessOrderMapper.ValidateColumns(columns);
            if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
                throw new OrderReadException("MDB Tools вернул повторяющиеся имена полей Orders.");
            var orders = new List<OrderSummary>();
            while (records.MoveNext())
            {
                var row = records.Current;
                if (row.Count != columns.Length) throw new OrderReadException("Нарушена структура строки Orders; список не заменён.");
                var values = columns.Select((name, index) => (name, field: row[index]))
                    .ToDictionary(item => item.name, item => item.field is { Quoted: false, Text: "NULL" } ? null : (object?)item.field.Text,
                        StringComparer.Ordinal);
                orders.Add(AccessOrderMapper.Map(values));
            }
            return orders;
        }
    }

    private sealed record CsvField(string Text, bool Quoted);

    private static IEnumerable<IReadOnlyList<CsvField>> ReadCsv(TextReader reader, CancellationToken cancellationToken)
    {
        // Поля текста всегда заключены mdb-export в кавычки. Поэтому NULL отличается от "NULL" и "".
        while (reader.Peek() >= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new List<CsvField>();
            while (true)
            {
                var quoted = reader.Peek() == '"';
                var value = new StringBuilder();
                if (quoted)
                {
                    reader.Read();
                    while (true)
                    {
                        var ch = reader.Read();
                        if (ch == -1) throw new OrderReadException("Незавершённое текстовое поле Orders; список не заменён.");
                        if (ch == '"')
                        {
                            if (reader.Peek() != '"') break;
                            reader.Read();
                        }
                        value.Append((char)ch);
                    }
                }
                else
                {
                    while (reader.Peek() is var ch && ch is not (-1 or ',' or '\r' or '\n'))
                        value.Append((char)reader.Read());
                }
                fields.Add(new(value.ToString(), quoted));
                var separator = reader.Read();
                if (separator == ',') continue;
                if (separator == '\r') { if (reader.Peek() == '\n') reader.Read(); }
                else if (separator is not (-1 or '\n')) throw new OrderReadException("Некорректный разделитель в Orders; список не заменён.");
                break;
            }
            yield return fields;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
