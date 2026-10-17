using System.Data;
using System.Data.OleDb;
using System.Runtime.Versioning;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

[SupportedOSPlatform("windows")]
public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task PriceEditPreservesOriginalSchemaKeysOrdersAndOtherTariffs()
    {
        using var database = Database.Create();
        database.Execute("DELETE FROM [PriceLL500] WHERE [2,5]=999");
        var originalOrders = await database.Read(); var ordersSchema = database.Schema(); var priceSchema = PriceSchema(database, PriceTables.Laser);
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Laser, TestContext.Current.CancellationToken);
        var values = original with { Rates = original.Rates.Select(rate => rate.ColumnName == "2,5" ? rate with { RublesPerMeter = 51 } : rate).ToArray() };
        var saved = await new OleDbPriceEditor(database.Path).SaveAsync(new(original, values), TestContext.Current.CancellationToken);
        Assert.True(PriceTableChange.Matches(values, saved));
        Assert.Equal(51, database.Scalar("SELECT [2,5] FROM [PriceLL500]")); // Прежний способ чтения.
        Assert.Equal(1, database.Scalar("SELECT [Код] FROM [PriceLL500]"));
        Assert.Equal(originalOrders, await database.Read()); Assert.Equal(ordersSchema, database.Schema());
        Assert.Equal(priceSchema, PriceSchema(database, PriceTables.Laser));
        Assert.Equal(87, database.Scalar("SELECT TOP 1 [1,5] FROM [PriceP]"));
    }

    [Fact]
    public async Task NullZeroAndMaximumLongIntegerRoundTripAndAStaleSnapshotCannotOverwriteAnyRate()
    {
        using var database = Database.Create(); database.Execute("DELETE FROM [PriceP] WHERE [1,5]=999");
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken);
        var values = original with { Rates = original.Rates.Select(rate => rate with { RublesPerMeter = rate.ColumnName switch {
            "1,5" => null, "2" => int.MaxValue, "2,5" => 0, _ => 4 } }).ToArray() };
        var editor = new OleDbPriceEditor(database.Path);
        Assert.True(PriceTableChange.Matches(values, await editor.SaveAsync(new(original, values), TestContext.Current.CancellationToken)));
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(new(original, original), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.True(PriceTableChange.Matches(values, await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task MultipleAndEmptyPriceRowsAreNeverUpdatedOrCreated()
    {
        using var database = Database.Create(); var editor = new OleDbPriceEditor(database.Path);
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken);
        var values = original with { Rates = original.Rates.Select(rate => rate with { RublesPerMeter = 12 }).ToArray() };
        var multiple = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(new(original, values), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, multiple.Failure);
        Assert.Equal(2, database.Scalar("SELECT COUNT(*) FROM [PriceP]")); Assert.Equal(87, database.Scalar("SELECT TOP 1 [1,5] FROM [PriceP]"));
        database.Execute("DELETE FROM [PriceP]");
        var empty = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SaveAsync(new(original, values), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, empty.Failure); Assert.Equal(0, database.Scalar("SELECT COUNT(*) FROM [PriceP]"));
    }

    [Fact]
    public async Task RequiredRateCannotBeClearedAndTheWholeChangeIsRolledBack()
    {
        using var database = Database.Create(); database.Execute("DROP TABLE [PriceP]");
        database.Execute("CREATE TABLE [PriceP] ([2] LONG NOT NULL, [3] LONG)");
        database.Execute("INSERT INTO [PriceP] ([2],[3]) VALUES (100,90)");
        var schema = PriceSchema(database, PriceTables.Plasma);
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken);
        var values = original with { Rates = [new(2m, "2", null), new(3m, "3", 45)] };
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbPriceEditor(database.Path).SaveAsync(new(original, values), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, error.Failure);
        Assert.True(PriceTableChange.Matches(original, await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken)));
        Assert.Equal(schema, PriceSchema(database, PriceTables.Plasma));
    }

    [Fact]
    public async Task CompetingRateChangeIsNotLostAndPreCancelledRequestsNeverOpenTheFile()
    {
        using var database = Database.Create(); database.Execute("DELETE FROM [PriceP] WHERE [1,5]=999");
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken);
        using var other = database.Connection(); using var transaction = other.BeginTransaction(); using var update = other.CreateCommand();
        update.Transaction = transaction; update.CommandText = "UPDATE [PriceP] SET [3]=77"; update.ExecuteNonQuery();
        var values = original with { Rates = original.Rates.Select(rate => rate.ColumnName == "2" ? rate with { RublesPerMeter = 12 } : rate).ToArray() };
        var saving = new OleDbPriceEditor(database.Path).SaveAsync(new(original, values), TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken); transaction.Commit();
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => saving);
        Assert.Contains(error.Failure, new[] { OrderWriteFailure.Conflict, OrderWriteFailure.NotSaved });
        Assert.Equal(77, database.Scalar("SELECT [3] FROM [PriceP]")); Assert.Equal(100, database.Scalar("SELECT [2] FROM [PriceP]"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OleDbPriceEditor("missing.mdb").SaveAsync(new(original, values), cancelled.Token));
    }

    [Fact]
    public async Task AddedColumnsUseLegacyNamesAndLongIntegersAndBackupPreservesTheWholeOriginalDatabase()
    {
        using var database = Database.Create();
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.GasOxygen, TestContext.Current.CancellationToken);
        var orders = await database.Read(); var orderSchema = database.Schema(); var schema = PriceSchema(database, original.Name);
        var bytes = File.ReadAllBytes(database.Path);
        var values = original with { Rates = original.Rates.Concat(new PriceRate[] { new(28m, "28", 87), new(29.5m, "29,5", 55) }).ToArray() };
        var saved = await new OleDbPriceEditor(database.Path).SaveAsync(new(original, values), TestContext.Current.CancellationToken);
        Assert.True(PriceTableChange.Matches(values, saved));
        Assert.Equal(87, database.Scalar("SELECT [28] FROM [PriceG]")); Assert.Equal(55, database.Scalar("SELECT [29,5] FROM [PriceG]"));
        Assert.Equal(1, database.Scalar("SELECT [Код] FROM [PriceG]"));
        Assert.Equal(orders, await database.Read()); Assert.Equal(orderSchema, database.Schema());
        Assert.All(schema, old => Assert.Contains(old, PriceSchema(database, original.Name)));
        using (var connection = database.Connection())
        {
            var added = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, "PriceG", "29,5"])!;
            Assert.Equal((short)OleDbType.Integer, Convert.ToInt16(Assert.Single(added.Rows.Cast<DataRow>())["DATA_TYPE"]));
        }
        var backup = Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-price-expansion-*.bak"));
        Assert.Equal(bytes, File.ReadAllBytes(backup)); Assert.Equal(87, database.Scalar("SELECT TOP 1 [1,5] FROM [PriceP]"));
    }

    [Fact]
    public async Task StatementFailureAfterAddingAColumnRollsBackSchemaAndAllPrices()
    {
        using var database = Database.Create(); database.Execute("DROP TABLE [PriceP]");
        database.Execute("CREATE TABLE [PriceP] ([2] LONG NOT NULL, [3] LONG)");
        database.Execute("INSERT INTO [PriceP] ([2],[3]) VALUES (100,90)");
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.Plasma, TestContext.Current.CancellationToken);
        var schema = PriceSchema(database, original.Name);
        var values = original with { Rates = [new(2m, "2", null), new(3m, "3", 45), new(2.8m, "2,8", 50)] };
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbPriceEditor(database.Path).SaveAsync(new(original, values), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(schema, PriceSchema(database, original.Name));
        Assert.True(PriceTableChange.Matches(original, await OleDbPriceReader.ReadAsync(database.Path, original.Name, TestContext.Current.CancellationToken)));
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-price-expansion-*.bak"));
    }

    [Fact]
    public async Task OpenDatabaseConnectionPreventsExpansionWithoutCreatingABackupOrChangingSchema()
    {
        using var database = Database.Create();
        var original = await OleDbPriceReader.ReadAsync(database.Path, PriceTables.GasOxygen, TestContext.Current.CancellationToken);
        var schema = PriceSchema(database, original.Name);
        using var other = database.Connection();
        var values = original with { Rates = original.Rates.Append(new(28m, "28", 87)).ToArray() };
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbPriceEditor(database.Path).SaveAsync(new(original, values), TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, failure.Failure);
        Assert.Equal(schema, PriceSchema(database, original.Name));
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(database.Path)!, "*.before-price-expansion-*.bak"));
    }

    private static string[] PriceSchema(Database database, string name)
    {
        using var connection = database.Connection();
        var schema = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, name, null])!;
        return schema.Rows.Cast<DataRow>().Select(row => string.Join('|', new[] { "COLUMN_NAME", "DATA_TYPE", "IS_NULLABLE", "ORDINAL_POSITION" }
            .Select(key => row[key]))).Order(StringComparer.Ordinal).ToArray();
    }
}
