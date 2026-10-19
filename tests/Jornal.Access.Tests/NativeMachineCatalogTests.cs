using System.Runtime.Versioning;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

[SupportedOSPlatform("windows")]
public sealed partial class NativeAccessTests
{
    private static CuttingMachine CustomMachine(string name = "Гидроабразив 'А'") => new(name, "PriceMachine_" + Guid.NewGuid().ToString("N"), CuttingMachine.WaterjetProfile);
    private static MachineCreation NewMachine(CuttingMachine machine, params CuttingMachine[] expected) =>
        new(machine, [new(2.5m, "2,5", 170), new(70m, "70", 600)], expected);

    [Fact]
    public async Task NativeMachineImportChangesOnlyItsProfileAndRejectsAStaleWriter()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var catalog = new OleDbMachineCatalog(database.Path);
        var machine = CustomMachine(); await catalog.CreateMachineAsync(NewMachine(machine), token);
        var before = await database.Read(); var schema = database.Schema();
        var price = await OleDbPriceReader.ReadAsync(database.Path, machine.TableName, token);
        foreach (var profile in new string?[] { CuttingMachine.VintechProfile, CuttingMachine.HansProfile, null, CuttingMachine.WaterjetProfile })
        {
            var change = new MachineImportChange(machine, profile);
            var saved = await catalog.UpdateMachineImportAsync(change, token);
            Assert.Equal(change.Values, saved); Assert.Equal(saved, Assert.Single(await catalog.ReadMachinesAsync(token)));
            var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => catalog.UpdateMachineImportAsync(new(machine, machine.SpecificationProfileId == CuttingMachine.HansProfile ? null : CuttingMachine.HansProfile), token));
            Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
            machine = saved;
        }
        Assert.Equal(before, await database.Read()); Assert.Equal(schema, database.Schema());
        Assert.Equal(price.Rates, (await OleDbPriceReader.ReadAsync(database.Path, machine.TableName, token)).Rates);
        Assert.Equal(machine.Name, database.Scalar("SELECT [Name] FROM [JournalMachines]"));
        Assert.Equal(machine.TableName, database.Scalar("SELECT [PriceTableName] FROM [JournalMachines]"));
    }

    [Fact]
    public async Task NativeMachineCreationUsesLegacyColumnsPreservesOrdersAndAllowsCreateAndEdit()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var original = await database.Read(); var schema = database.Schema(); var laserSchema = PriceSchema(database, PriceTables.Laser);
        var catalog = new OleDbMachineCatalog(database.Path);
        Assert.Empty(await catalog.ReadMachinesAsync(token));
        var bytes = File.ReadAllBytes(database.Path); var machine = CustomMachine();
        Assert.Equal(machine, await catalog.CreateMachineAsync(NewMachine(machine), token));
        Assert.Equal(machine, Assert.Single(await catalog.ReadMachinesAsync(token)));
        Assert.Equal(170, database.Scalar($"SELECT [2,5] FROM [{machine.TableName}]"));
        Assert.Equal(1, database.Scalar($"SELECT COUNT(*) FROM [{machine.TableName}]"));
        Assert.Equal(1, database.Scalar($"SELECT [Код] FROM [{machine.TableName}]"));
        Assert.Equal(original, await database.Read()); Assert.Equal(schema, database.Schema()); Assert.Equal(laserSchema, PriceSchema(database, PriceTables.Laser));
        var backups = Directory.GetFiles(Path.GetDirectoryName(database.Path)!, "*.before-price-expansion-*.bak");
        Assert.Equal(bytes, File.ReadAllBytes(Assert.Single(backups)));
        var creator = new OleDbManualOrderCreator(database.Path);
        var draft = new ManualOrderDraft(new(2026, 10, 7), "1007-0952", "Менеджер", "Заказчик", machine.Name, "Сталь 20/09Г2С",
            70m, "1500x2000", "4", 2, "1957", 1m, "", "") { Machine = machine };
        var quote = await new CuttingPriceService(creator).CalculateAsync(draft.PriceRequest, token); Assert.Equal(1200m, quote.StoredPrice);
        var created = await creator.CreateAsync(draft with { ExpectedQuote = quote }, token);
        Assert.Equal(draft.ToOrder(created.Id, 1200m), created);
        var maintenance = new OleDbOrderMaintenance(database.Path); var record = (await maintenance.ReadRecordAsync(created.Id, token))!;
        var saved = await maintenance.UpdateAsync(new(record, record with { CutLength = 3 }) { Machine = machine }, token);
        Assert.Equal(1800m, saved.Price); Assert.Equal(schema, database.Schema());
        Assert.Equal(original, (await database.Read()).Where(row => row.Id != created.Id));
    }

    [Fact]
    public async Task NativeCatalogRejectsConcurrentSnapshotsAndLocksWithoutCreatingOrReplacingTables()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var catalog = new OleDbMachineCatalog(database.Path); var first = CustomMachine();
        await catalog.CreateMachineAsync(NewMachine(first), token);
        var other = CustomMachine("Другой станок");
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => catalog.CreateMachineAsync(NewMachine(other), token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Equal(first, Assert.Single(await catalog.ReadMachinesAsync(token)));
        using (var connection = database.Connection())
        {
            var locked = await Assert.ThrowsAsync<OrderWriteException>(() => catalog.CreateMachineAsync(NewMachine(other, first), token));
            Assert.Equal(OrderWriteFailure.NotSaved, locked.Failure);
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.CreateMachineAsync(NewMachine(other, first), cancelled.Token));
        Assert.Equal(first, Assert.Single(await catalog.ReadMachinesAsync(token)));
        using var check = database.Connection(); Assert.False(OleDbMachineCatalogTableExists(check, other.TableName));
    }
    private static bool OleDbMachineCatalogTableExists(System.Data.OleDb.OleDbConnection connection, string name)
    {
        using var tables = connection.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Tables, [null, null, name, "TABLE"]);
        return tables!.Rows.Count > 0;
    }
}
