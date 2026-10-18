using System.Data;
using System.Data.OleDb;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task NativeRenamePreservesColumnPropertiesKeyHistoryOtherTablesAndBacksUpTheWholeDatabase()
    {
        using var database = Database.Create();
        database.Execute("CREATE INDEX [RateIndex] ON [PriceG] ([2,5])");
        var editor = new OleDbPriceEditor(database.Path);
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.GasOxygen, TestContext.Current.CancellationToken);
        var history = await database.Read(); var schema = database.Schema(); var properties = PriceColumnProperties(database, original.Name);
        var bytes = File.ReadAllBytes(database.Path);
        var changed = original with { Rates = original.Rates.Select(rate => rate.ThicknessMillimeters == 2.5m ? new(2.8m, "2,8", 125) : rate).ToArray() };
        changed = changed with { Rates = changed.Rates.Append(new(28m, "28", 0)).ToArray() };
        var draft = new PriceTableChange(original, changed);
        var usage = await editor.ReadThicknessUsageAsync(draft, TestContext.Current.CancellationToken);
        var saved = await editor.SaveAsync(draft with { ConfirmedUsage = usage.ToArray() }, TestContext.Current.CancellationToken);
        Assert.True(PriceTableChange.Matches(changed, saved));
        Assert.Equal(125, database.Scalar("SELECT [2,8] FROM [PriceG]")); Assert.Equal(0, database.Scalar("SELECT [28] FROM [PriceG]"));
        var actual = PriceColumnProperties(database, original.Name);
        Assert.All(properties, field => Assert.Contains(field.Replace("2,5|", "2,8|", StringComparison.Ordinal), actual));
        Assert.Equal(history, await database.Read()); Assert.Equal(schema, database.Schema());
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-price-edit-*.bak"))));
        using var connection = database.Connection();
        var index = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, [null, null, "RateIndex", null, original.Name])!;
        Assert.Equal("2,8", Assert.Single(index.Rows.Cast<DataRow>())["COLUMN_NAME"]);
    }

    [Fact]
    public async Task FailureAfterNativeRenameRollsBackNamesNullabilityIndexesAndAllPriceChanges()
    {
        using var database = Database.Create(); database.Execute("DROP TABLE [PriceP]");
        database.Execute("CREATE TABLE [PriceP] ([Код] COUNTER PRIMARY KEY, [2] LONG NOT NULL, [3] LONG)");
        database.Execute("INSERT INTO [PriceP] ([2],[3]) VALUES (100,90)");
        database.Execute("CREATE INDEX [OriginalRateIndex] ON [PriceP] ([3])");
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken);
        var before = PriceColumnProperties(database, original.Name);
        var change = new PriceTableChange(original, original with { Rates = [new(2m, "2", null), new(3.1m, "3,1", 45)] });
        var editor = new OleDbPriceEditor(database.Path);
        var usage = await editor.ReadThicknessUsageAsync(change, TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(change with { ConfirmedUsage = usage.ToArray() }, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.True(PriceTableChange.Matches(original, await OleDbPriceReader.ReadAsync(database.Path, original.Name, TestContext.Current.CancellationToken)));
        Assert.Equal(before, PriceColumnProperties(database, original.Name));
    }

    [Fact]
    public async Task OpenConnectionStalePriceAndChangedUsagePreventRenames()
    {
        using var database = Database.Create(); database.Execute("DELETE FROM [PriceHL_id2] WHERE [2,5]=999");
        var editor = new OleDbPriceEditor(database.Path);
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.HansLaserId2, TestContext.Current.CancellationToken);
        var change = new PriceTableChange(original, original with { Rates = original.Rates.Select(rate => rate.ThicknessMillimeters == 2.5m ? new(2.8m, "2,8", 23) : rate).ToArray() });
        var usage = await editor.ReadThicknessUsageAsync(change, TestContext.Current.CancellationToken); change = change with { ConfirmedUsage = usage.ToArray() };
        using (var other = database.Connection())
        {
            var failure = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(change, TestContext.Current.CancellationToken));
            Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
            Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-price-edit-*.bak"));
        }
        database.Execute("UPDATE [Orders] SET [Толщина]=2.5, [Вид_резки]='HansLaser', [Коэффициент]='1|1|1|2|N2'");
        var stale = change with { ConfirmedUsage = [new("2,5", 999)] };
        Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(stale, TestContext.Current.CancellationToken))).Failure);
        database.Execute("UPDATE [PriceHL_id2] SET [2,5]=18");
        Assert.Equal(OrderWriteFailure.Conflict, (await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(change, TestContext.Current.CancellationToken))).Failure);
        Assert.Contains((await OleDbPriceReader.ReadAsync(database.Path, original.Name, TestContext.Current.CancellationToken)).Rates, rate => rate.ThicknessMillimeters == 2.5m);
    }

    private static string[] PriceColumnProperties(Database database, string name)
    {
        using var connection = database.Connection();
        using var columns = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, name, null])!;
        return columns.Rows.Cast<DataRow>().OrderBy(row => Convert.ToInt32(row["ORDINAL_POSITION"]))
            .Select(row => string.Join('|', new[] { "COLUMN_NAME", "DATA_TYPE", "IS_NULLABLE", "ORDINAL_POSITION", "COLUMN_DEFAULT", "COLUMN_HASDEFAULT" }.Select(key => row[key]))).ToArray();
    }
}
