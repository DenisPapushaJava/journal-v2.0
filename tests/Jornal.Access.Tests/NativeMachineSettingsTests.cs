using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task MachineLabelsPreparationIsRepeatableAndPreservesLegacySchemaAndBytesInBackup()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var schema = database.Schema(); var orders = await database.Read(); var hash = SHA256.HashData(File.ReadAllBytes(database.Path));
        var first = new OleDbMachineSettings(database.Path); var second = new OleDbMachineSettings(database.Path);
        Assert.Equal(4, (await first.ReadMachineSettingsAsync(token)).Count);
        await first.PrepareMachineLabelsAsync(token); await second.PrepareMachineLabelsAsync(token);
        Assert.Equal(schema, database.Schema()); Assert.Equal(orders, await database.Read());
        Assert.Equal(17, database.Scalar("SELECT [2,5] FROM [PriceLL500]"));
        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(database.Path)!, "*.before-machine-labels-*.bak"));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(backup)));
    }

    [Fact]
    public async Task TwoMachineLabelsClientsCanPrepareAfterATransientLock()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        using var holder = new FileStream(database.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var first = new OleDbMachineSettings(database.Path).PrepareMachineLabelsAsync(token);
        var second = new OleDbMachineSettings(database.Path).PrepareMachineLabelsAsync(token);
        await Task.Delay(150, token); holder.Dispose(); await Task.WhenAll(first, second);
        Assert.Equal(4, (await new OleDbMachineSettings(database.Path).ReadMachineSettingsAsync(token)).Count);
        Assert.Equal(0, database.Scalar("SELECT Count(*) FROM [JournalMachineLabels]"));
    }

    [Fact]
    public async Task MachineNameAndImportSaveTogetherAndKeepOrdersPricesAndCatalogKeys()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        var machine = new CuttingMachine("ГАР", "PriceMachine_" + Guid.NewGuid().ToString("N"), CuttingMachine.WaterjetProfile);
        await new OleDbMachineCatalog(database.Path).CreateMachineAsync(new(machine, [new(2, "2", 100)], []), token);
        var schema = database.Schema(); var orders = await database.Read(); var store = new OleDbMachineSettings(database.Path);
        var settings = (await store.ReadMachineSettingsAsync(token)).Single(row => row.Key == machine.TableName);
        var saved = await store.UpdateMachineSettingsAsync(new(settings, "Гидроабразив", CuttingMachine.VintechProfile), token);
        Assert.Equal(machine.TableName, saved.Key); Assert.Equal("ГАР", saved.Name);
        var catalog = Assert.Single(await new OleDbMachineCatalog(database.Path).ReadMachinesAsync(token));
        Assert.Equal("ГАР", catalog.Name); Assert.Equal(CuttingMachine.VintechProfile, catalog.SpecificationProfileId);
        Assert.Equal(orders, await database.Read()); Assert.Equal(schema, database.Schema());
        Assert.Equal(100, database.Scalar($"SELECT [2] FROM [{machine.TableName}]"));
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.UpdateMachineSettingsAsync(new(settings, "Чужой снимок", null), token));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Contains(saved, await store.ReadMachineSettingsAsync(token));
        var reset = await store.UpdateMachineSettingsAsync(new(saved, "ГАР", saved.SpecificationProfileId), token);
        Assert.Equal("ГАР", reset.DisplayName); Assert.Equal(0, database.Scalar("SELECT Count(*) FROM [JournalMachineLabels]"));
    }

    [Fact]
    public async Task UnknownMachineLabelsTableIsNeverReplaced()
    {
        using var database = Database.Create(); var token = TestContext.Current.CancellationToken;
        database.Execute("CREATE TABLE [JournalMachineLabels] ([OtherData] TEXT(100))");
        database.Execute("INSERT INTO [JournalMachineLabels] ([OtherData]) VALUES ('keep')");
        var store = new OleDbMachineSettings(database.Path);
        await Assert.ThrowsAsync<OrderWriteException>(() => store.PrepareMachineLabelsAsync(token));
        Assert.Equal("keep", database.Scalar("SELECT [OtherData] FROM [JournalMachineLabels]"));
        Assert.NotEmpty(await database.Read());
    }
}
