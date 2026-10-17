using System.Globalization;
using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

public sealed class AccessOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidAndUnavailableDatabasesKeepOpeningStageWithoutLeakingInput(bool missing)
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-header-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "SECRET-CUSTOMER.mdb");
        var bytes = "Synthetic invalid database"u8.ToArray();
        if (!missing) await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);
        try
        {
            var error = await Assert.ThrowsAsync<OrderReadException>(() => new AccessOrderReader(path).ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(DiagnosticStage.OpenDatabase, DiagnosticStages.Of(error));
            var report = DiagnosticReport.Create(error, DiagnosticOperation.OrderRead, Guid.NewGuid().ToString("N"), "2026.1.0-alpha.6");
            var json = System.Text.Json.JsonSerializer.Serialize(report, DiagnosticReport.Json);
            Assert.DoesNotContain("SECRET", json);
            Assert.Equal(DiagnosticStage.OpenDatabase, report.Stage);
            if (!missing) Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            else Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ReadsNamedColumnsAndPreservesHistoricalValues()
    {
        var values = Row();
        values["Дата"] = new DateTime(2026, 1, 2, 8, 31, 42);
        values["Стоимость"] = 922337203685477.5807m;
        values["Толщина"] = 2.5d;
        values["Кол_во_листов"] = "02 / комплект";
        values["Вес"] = "0012.350";
        values["БЛЦБ"] = "БЛЦБ-'А',\nвторая строка";
        values["Коэффициент"] = " 1,25 | 20 | 340 | 2 | N2 ";
        values["Color"] = -16711936;
        var order = AccessOrderMapper.Map(values.Reverse().ToDictionary());
        Assert.Equal(new DateOnly(2026, 1, 2), order.OrderDate);
        Assert.Equal(values["Дата"], order.StoredOrderDateTime);
        Assert.Equal(922337203685477.5807m, order.StoredCuttingPrice);
        Assert.Equal(2.5m, order.ThicknessMillimeters);
        Assert.Equal("02 / комплект", order.SheetCount);
        Assert.Equal("0012.350", order.WeightKilograms);
        Assert.Equal(values["БЛЦБ"], order.DeliveryNumber);
        Assert.Equal(values["Коэффициент"], order.StoredCoefficient);
        Assert.Equal(1.25m, order.Coefficient);
        Assert.Equal(20, order.PartCount);
        Assert.Equal(340, order.PunchCount);
        Assert.Equal("2", order.MaterialId);
        Assert.Equal("N2", order.CuttingGas);
        Assert.Empty(new OrderRowViewModel(order).StatusText);
        Assert.False(new OrderRowViewModel(order).HasStandardStatus);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("1.2", "1.2", null)]
    [InlineData("1,25 | 10 | 200", "1.25", 10)]
    [InlineData("-1|-1|-1|-1|-1", null, null)]
    [InlineData("старое неизвестное значение", null, null)]
    public void ReadsOldCoefficientsWithoutInventingDefaults(string? raw, string? number, int? parts)
    {
        var values = Row(); values["Коэффициент"] = raw;
        var order = AccessOrderMapper.Map(values);
        Assert.Equal(raw, order.StoredCoefficient);
        Assert.Equal(number is null ? null : decimal.Parse(number, CultureInfo.InvariantCulture), order.Coefficient);
        Assert.Equal(parts, order.PartCount);
        Assert.Equal("", order.MaterialId);
        Assert.Equal("", order.CuttingGas);
    }

    [Fact]
    public void NullsRemainUnknownAndAreDifferentFromZero()
    {
        var values = Row();
        foreach (var column in new[] { "Дата", "Стоимость", "Толщина", "Ход", "Color", "Коэффициент" }) values[column] = DBNull.Value;
        var order = AccessOrderMapper.Map(values);
        Assert.Null(order.OrderDate); Assert.Null(order.ThicknessMillimeters); Assert.Null(order.StoredCuttingPrice);
        Assert.Null(order.CutLengthMeters); Assert.Null(order.Color); Assert.Null(order.Coefficient);
        var row = new OrderRowViewModel(order);
        Assert.Equal("—", row.DateText); Assert.Equal("—", row.ThicknessText); Assert.Equal("Не рассчитана", row.StoredPriceText);
        Assert.Empty(row.StatusText);
        Assert.False(row.HasStandardStatus);
        values["Стоимость"] = 0m; values["Толщина"] = 0d; values["Ход"] = 0;
        var zero = AccessOrderMapper.Map(values);
        Assert.Equal(0, zero.StoredCuttingPrice); Assert.Equal(0, zero.ThicknessMillimeters); Assert.Equal(0, zero.CutLengthMeters);
    }

    [Theory]
    [InlineData("Код", null)]
    [InlineData("Код", "1.5")]
    [InlineData("Дата", "дата не указана")]
    [InlineData("Стоимость", "1,234.56")]
    [InlineData("Ход", "2147483648")]
    [InlineData("Толщина", "NaN")]
    public void InvalidTypedFieldsRejectTheRow(string name, object? value)
    {
        var values = Row(); values[name] = value;
        Assert.Contains(name, Assert.Throws<OrderReadException>(() => AccessOrderMapper.Map(values)).Message);
    }

    [Fact]
    public void CsvPreservesQuotesNewlinesUnicodeEmptyAndNullText()
    {
        var values = Row();
        values["Менеджер"] = "NULL";
        values["Заказчик"] = "Очень длинное имя, \"детали\"\r\nвторая строка 🏭 " + new string('А', 150);
        values["Материал"] = "Игнорируемая дополнительная колонка";
        values["Марка_стали"] = "Амг3";
        values["БЛЦБ"] = "";
        values["Коэффициент"] = null;
        var csv = Csv(values);
        var row = Assert.Single(MdbToolsOrderReader.ReadExport(new StringReader(csv), TestContext.Current.CancellationToken));
        Assert.Equal(values["Заказчик"], row.CustomerName); Assert.Equal("NULL", row.ManagerName);
        Assert.Equal("Амг3", row.MaterialName); Assert.Equal("", row.DeliveryNumber); Assert.Null(row.StoredCoefficient);
        Assert.Equal(1234.5678m, row.StoredCuttingPrice);
    }

    [Fact]
    public void CsvHandlesEmptyTableAndRejectsIncompleteOrDifferentSchemas()
    {
        var header = string.Join(',', AccessOrderMapper.Columns.Select(Quote));
        Assert.Empty(MdbToolsOrderReader.ReadExport(new StringReader(header + "\r\n"), TestContext.Current.CancellationToken));
        Assert.Throws<OrderReadException>(() => MdbToolsOrderReader.ReadExport(new StringReader("Код,Заказчик\n"), TestContext.Current.CancellationToken));
        Assert.Throws<OrderReadException>(() => MdbToolsOrderReader.ReadExport(new StringReader(header + "\n1,2\n"), TestContext.Current.CancellationToken));
        Assert.Throws<OrderReadException>(() => MdbToolsOrderReader.ReadExport(new StringReader(header + "\n\"не завершено"), TestContext.Current.CancellationToken));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => MdbToolsOrderReader.ReadExport(new StringReader(header), canceled.Token));
    }

    [Fact]
    public async Task MissingFilesInvalidFilesAndMissingToolsHaveActionableErrors()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "OrderDB.mdb");
            Assert.Contains("недоступен", (await Assert.ThrowsAsync<OrderReadException>(() => new AccessOrderReader(path).ReadAsync(TestContext.Current.CancellationToken))).Message);
            await File.WriteAllTextAsync(path, "Это не Access", TestContext.Current.CancellationToken);
            Assert.Contains("Jet MDB", (await Assert.ThrowsAsync<OrderReadException>(() => new AccessOrderReader(path).ReadAsync(TestContext.Current.CancellationToken))).Message);
            Assert.Contains("mdbtools", (await Assert.ThrowsAsync<OrderReadException>(() =>
                new MdbToolsOrderReader(Path.Combine(folder, "нет-команды")).ReadAsync(path, TestContext.Current.CancellationToken))).Message);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task OptionalNativeAccessFixturePreservesTypesAndNulls()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_SYNTHETIC_MDB");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Искусственная MDB для нативного адаптера не подготовлена.");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path!, TestContext.Current.CancellationToken));
        var orders = await new AccessOrderReader(path!).ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, orders.Count);
        var full = orders.Single(row => row.Id == 42);
        Assert.Equal(new DateTime(2026, 1, 2, 8, 31, 42), full.StoredOrderDateTime);
        Assert.Equal("Пример 'А', детали", full.CustomerName);
        Assert.Equal(1234.5678m, full.StoredCuttingPrice);
        Assert.Equal(2.5m, full.ThicknessMillimeters);
        Assert.Equal("02 / комплект", full.SheetCount);
        Assert.Equal("0012.350", full.WeightKilograms);
        Assert.Equal(-16711936, full.Color);
        Assert.Equal("БЛЦБ-000042", full.DeliveryNumber);
        Assert.Equal(" 1,25 | 20 | 340 | 2 | N2 ", full.StoredCoefficient);
        Assert.Equal(1.25m, full.Coefficient); Assert.Equal(340, full.PunchCount); Assert.Equal("N2", full.CuttingGas);
        var empty = orders.Single(row => row.Id == 43);
        Assert.Null(empty.OrderDate); Assert.Null(empty.ThicknessMillimeters); Assert.Null(empty.StoredCuttingPrice);
        Assert.Null(empty.Color); Assert.Null(empty.Coefficient); Assert.Null(empty.CutLengthMeters);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path!, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task OptionalLocalCopyIsReadWithoutChangingItsBytes()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_TEST_MDB");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(path), "Локальная копия MDB не задана через JOURNAL_TEST_MDB.");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(path!, TestContext.Current.CancellationToken));
        var reader = new AccessOrderReader(path!);
        var rows = await reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(rows);
        Assert.Equal(rows.Count, rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(rows, await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(path!, TestContext.Current.CancellationToken)));
    }

    internal static Dictionary<string, object?> Row() => new(StringComparer.Ordinal)
    {
        ["Код"] = 42, ["Дата"] = new DateTime(2026, 1, 2), ["Менеджер"] = "Менеджер", ["Спецификация"] = "0102-42",
        ["Заказчик"] = "Пример", ["Вид_резки"] = "Лазер", ["Марка_стали"] = "5083H111", ["Размер_листа"] = "1500x6000",
        ["Кол_во_листов"] = "2", ["Стоимость"] = 1234.5678m, ["Толщина"] = 2.5d, ["Ход"] = 31,
        ["Вес"] = "120.50", ["БЛЦБ"] = null, ["Color"] = -1, ["Коэффициент"] = null
    };
    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    internal static string Csv(Dictionary<string, object?> values) => string.Join(',', values.Keys.Select(Quote)) + "\r\n" +
        string.Join(',', values.Values.Select(value => value switch
        {
            null or DBNull => "NULL", string text => Quote(text), DateTime date => Quote(date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        })) + "\r\n";
}
