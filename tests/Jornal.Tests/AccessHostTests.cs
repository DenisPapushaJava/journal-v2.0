using System.Runtime.InteropServices;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Diagnostics;
using Xunit;

namespace Jornal.Tests;

public sealed class AccessHostTests
{
    [Fact]
    public void SafeHostDiagnosticPreservesOperationIdAndWriteOutcomeWithoutRequestData()
    {
        var id = Guid.NewGuid().ToString("N"); var session = Guid.NewGuid().ToString("N");
        var failure = new IOException("SECRET-CUSTOMER SECRET-MDB");
        DiagnosticStages.Tag(failure, DiagnosticStage.CommitTransaction);
        var report = DiagnosticReport.Create(failure, DiagnosticOperation.OrderEdit,
            session, "2026.1.0-dev", component: DiagnosticComponent.AccessHost, eventId: id) with { WriteFailure = OrderWriteFailure.OutcomeUnknown };
        var response = new AccessHostResponse(1, Error: "Запись не подтверждена.", Failure: OrderWriteFailure.OutcomeUnknown, Diagnostic: report);
        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("SECRET", json);
        var restored = JsonSerializer.Deserialize<AccessHostResponse>(json)!;
        restored.Diagnostic!.Validate();
        Assert.Equal(id, restored.Diagnostic.EventId); Assert.Equal(session, restored.Diagnostic.SessionId);
        Assert.Equal(DiagnosticOperation.OrderEdit, restored.Diagnostic.Operation);
        Assert.Equal(restored.Failure, restored.Diagnostic.WriteFailure);
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, restored.Failure);
        Assert.Equal(DiagnosticStage.CommitTransaction, restored.Diagnostic.Stage);
        Assert.Equal(report.BuildCommit, restored.Diagnostic.BuildCommit);
        Assert.Equal(report.BuildDirty, restored.Diagnostic.BuildDirty);
        Assert.Null(JsonSerializer.Deserialize<AccessHostResponse>("{\"Protocol\":1,\"Error\":\"old host\"}")!.Diagnostic);
    }

    [Theory]
    [InlineData(4551)]
    [InlineData(1260)]
    [InlineData(577)]
    public async Task BlockedLaunchNamesWindowsAndNeverClaimsARequestWasWritten(int code)
    {
        var client = new AccessHostClient("unused.mdb", "blocked.exe", _ => throw new System.ComponentModel.Win32Exception(code));
        var read = await Assert.ThrowsAsync<OrderReadException>(() => client.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Windows", read.Message);
        Assert.Contains(code.ToString(), read.Message);
        Assert.True(AccessHostBlockedException.Contains(read));
        Assert.Equal(DiagnosticStage.LaunchAccessHost, DiagnosticStages.Of(read));
        var price = await Assert.ThrowsAsync<PriceReadException>(() => client.ReadPriceTableAsync(PriceTables.Laser, TestContext.Current.CancellationToken));
        Assert.True(AccessHostBlockedException.Contains(price));
        Assert.Equal(DiagnosticStage.LaunchAccessHost, DiagnosticStages.Of(price));
        Assert.True(AccessHostBlockedException.Contains(await Assert.ThrowsAsync<OrderReadException>(() => client.ReadRecordAsync(42, TestContext.Current.CancellationToken))));
        foreach (var operation in new Func<Task>[] {
            async () => await client.SetColorAsync([Row()], OrderColors.Work, TestContext.Current.CancellationToken),
            async () => await client.SetDeliveryNumberAsync([Row()], "номер", TestContext.Current.CancellationToken),
            async () => await client.CreateAsync(Draft(), TestContext.Current.CancellationToken),
            async () => await client.CreateBatchAsync([Draft(), Draft()], TestContext.Current.CancellationToken),
            async () => await client.UpdateAsync(new(OrderMaintenanceTests.Record(), OrderMaintenanceTests.Record() with { Customer = "Новая" }), TestContext.Current.CancellationToken),
            async () => await client.DeleteAsync(OrderMaintenanceTests.Record(), TestContext.Current.CancellationToken),
            async () => await client.RestoreAsync(OrderMaintenanceTests.Record(), TestContext.Current.CancellationToken) })
        {
            var write = await Assert.ThrowsAsync<OrderWriteException>(operation);
            Assert.Equal(OrderWriteFailure.NotSaved, write.Failure);
            Assert.False(write.RequiresReload);
            Assert.Contains("не отправлен", write.Message);
            Assert.True(AccessHostBlockedException.Contains(write));
            Assert.Equal(DiagnosticStage.LaunchAccessHost, DiagnosticStages.Of(write));
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(193)]
    public async Task OtherLaunchFailuresDoNotClaimASecurityPolicy(int code)
    {
        var client = new AccessHostClient("unused.mdb", "missing.exe", _ => throw new System.ComponentModel.Win32Exception(code));
        var error = await Assert.ThrowsAsync<OrderReadException>(() => client.ReadAsync(TestContext.Current.CancellationToken));
        Assert.False(AccessHostBlockedException.Contains(error));
    }

    public static bool RunsOnWindows => OperatingSystem.IsWindows();
    [Theory(Skip = "Проверка полного Windows exit code через одноразовый дочерний cmd.exe.", SkipUnless = nameof(RunsOnWindows))]
    [InlineData(-1058471934)]
    [InlineData(-1073740760)]
    public async Task SecurityExitKeepsAnAlreadyDispatchedWriteUnconfirmed(int code)
    {
        var client = new AccessHostClient("unused.mdb", Path.Combine(Environment.SystemDirectory, "cmd.exe"), process =>
        {
            process.StartInfo.ArgumentList.Add("/d"); process.StartInfo.ArgumentList.Add("/c");
            process.StartInfo.ArgumentList.Add("exit " + code.ToString(System.Globalization.CultureInfo.InvariantCulture));
            process.Start();
            Assert.True(process.WaitForExit(10000));
        });
        var read = await Assert.ThrowsAsync<OrderReadException>(() => client.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(AccessHostBlockedException.Contains(read));
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => client.SetColorAsync([Row()], OrderColors.Work, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        Assert.True(error.RequiresReload); Assert.True(AccessHostBlockedException.Contains(error));
    }

    [Fact]
    public void ProtocolPreservesDecimalsDatesNullsAndUnicode()
    {
        var row = new OrderSummary(42, new(2026, 1, 2), "0102--42", "Имя", "Заказчик 'А'", "Лазер", "5083H111", 2.5m,
            922337203685477.5807m, null) { StoredOrderDateTime = new(2026, 1, 2, 8, 31, 42), StoredCoefficient = " 1,25 | 20 | 340 | 2 | N2 ",
            DeliveryNumber = null, SheetCount = "02 / комплект" };
        var request = new AccessHostRequest(1, AccessHostOperation.SetColor, @"C:\Общая папка\OrderDB.mdb", [row], OrderColors.Work);
        Assert.Equal(row, JsonSerializer.Deserialize<AccessHostRequest>(JsonSerializer.Serialize(request))!.Expected![0]);
        Assert.Equal(row, JsonSerializer.Deserialize<AccessHostResponse>(JsonSerializer.Serialize(new AccessHostResponse(1, [row])))!.Orders![0]);
        var table = new PriceTable(PriceTables.Laser, true, [new(2.5m, "2,5", null), new(3m, "3", 0)]);
        var roundTrip = JsonSerializer.Deserialize<AccessHostResponse>(JsonSerializer.Serialize(new AccessHostResponse(1, PriceTable: table)))!.PriceTable!;
        roundTrip.Validate();
        Assert.Equal(table.Name, roundTrip.Name);
        Assert.Equal(table.Rates, roundTrip.Rates);
        Assert.Equal(4, (int)AccessHostOperation.ReadPriceTable);
        Assert.Equal(5, (int)AccessHostOperation.CreateOrder);
        Assert.Equal(6, (int)AccessHostOperation.ReadOrder);
        Assert.Equal(7, (int)AccessHostOperation.UpdateOrder);
        Assert.Equal(8, (int)AccessHostOperation.DeleteOrder);
        Assert.Equal(9, (int)AccessHostOperation.RestoreOrder);
        Assert.Equal(10, (int)AccessHostOperation.PrepareCuttingTime);
        Assert.Equal(11, (int)AccessHostOperation.CreateOrders);
        var batchDrafts = new[] { Draft(), Draft() with { SpecificationNumber = "0102-45" } };
        Assert.Equal(batchDrafts, JsonSerializer.Deserialize<AccessHostRequest>(JsonSerializer.Serialize(
            new AccessHostRequest(1, AccessHostOperation.CreateOrders, "unused.mdb", Drafts: batchDrafts)))!.Drafts);
        var record = OrderMaintenanceTests.Record();
        Assert.Equal(record, JsonSerializer.Deserialize<AccessHostResponse>(JsonSerializer.Serialize(new AccessHostResponse(1, Record: record, MaintenanceCompleted: true)))!.Record);
        var draft = Draft() with { ExpectedQuote = new(PriceTables.Laser, "2,5", 17, 25, 2.3m, 977m) };
        Assert.Equal(draft, JsonSerializer.Deserialize<AccessHostRequest>(JsonSerializer.Serialize(
            new AccessHostRequest(1, AccessHostOperation.CreateOrder, @"C:\Orders.mdb", Draft: draft)))!.Draft);
    }

    [Fact]
    public async Task MissingHostDoesNotTurnAFailedStartIntoAnUnknownCommit()
    {
        var client = new AccessHostClient("unused.mdb", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"));
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => client.SetColorAsync([Row()], OrderColors.Work, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, error.Failure);
        Assert.Equal(DiagnosticStage.LaunchAccessHost, DiagnosticStages.Of(error));
        Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => client.CreateAsync(Draft(), TestContext.Current.CancellationToken))).Failure);
        await Assert.ThrowsAsync<OrderReadException>(() => client.ReadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<PriceReadException>(() => client.ReadPriceTableAsync(PriceTables.Laser, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BrokenHostResponseAfterDispatchRequiresReloadBeforeRetry()
    {
        // dotnet без аргументов выдаёт справку, а не JSON: имитация повреждённого/чужого хоста.
        var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var client = new AccessHostClient("unused.mdb", dotnet);
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => client.SetColorAsync([Row()], OrderColors.Work, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        Assert.True(error.RequiresReload);
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, (await Assert.ThrowsAsync<OrderWriteException>(() => client.CreateAsync(Draft(), TestContext.Current.CancellationToken))).Failure);
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, (await Assert.ThrowsAsync<OrderWriteException>(() => client.CreateBatchAsync([Draft(), Draft()], TestContext.Current.CancellationToken))).Failure);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotStartAHost()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var client = new AccessHostClient("unused.mdb", "missing.exe");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SetColorAsync([Row()], OrderColors.Work, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadPriceTableAsync(PriceTables.Laser, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateAsync(Draft(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CreateBatchAsync([Draft(), Draft()], cancellation.Token));
    }

    public static bool HasNativeHost => OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("JOURNAL_ACCESS_HOST") is not null;

    [Fact(Skip = "Нужен опубликованный AccessHost x86 и искусственная MDB; обязательно в Windows installer CI.", SkipUnless = nameof(HasNativeHost))]
    public async Task NativeX64ClientReadsAndWritesThroughThePackagedX86Host()
    {
        Assert.True(Environment.Is64BitProcess);
        var host = Environment.GetEnvironmentVariable("JOURNAL_ACCESS_HOST")!;
        Assert.True(File.Exists(host));
        var fixture = Environment.GetEnvironmentVariable("JOURNAL_SYNTHETIC_MDB");
        Assert.True(File.Exists(fixture), "Не подготовлена искусственная MDB для обязательной проверки хоста.");
        var root = Path.Combine(Path.GetTempPath(), "journal-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Order's copy; Mode=Share.mdb");
        File.Copy(fixture, path);
        try
        {
            var invalid = Path.Combine(root, "invalid.mdb");
            File.WriteAllText(invalid, "Synthetic invalid database");
            var failure = await Assert.ThrowsAsync<OrderReadException>(() => new AccessHostClient(invalid, host).ReadAsync(TestContext.Current.CancellationToken));
            Assert.Equal(DiagnosticStage.OpenDatabase, DiagnosticStages.Of(failure));
            Assert.Contains("JR-", AppDiagnostics.Describe(DiagnosticOperation.OrderRead, failure, "Ошибка чтения."));
            var client = new AccessHostClient(path, host);
            var original = await client.ReadAsync(TestContext.Current.CancellationToken);
            await client.PrepareCuttingTimeAsync(TestContext.Current.CancellationToken);
            await client.PrepareAnnotationsAsync(TestContext.Current.CancellationToken);
            var first = original.Single(row => row.Id == 42);
            Assert.Equal(1234.5678m, first.StoredCuttingPrice);
            Assert.Equal("БЛЦБ-000042", first.DeliveryNumber);
            Assert.Equal(" 1,25 | 20 | 340 | 2 | N2 ", first.StoredCoefficient);
            var laser = await client.ReadPriceTableAsync(PriceTables.Laser, TestContext.Current.CancellationToken);
            Assert.Equal(17, laser.Rates.Single(rate => rate.ThicknessMillimeters == 2.5m).RublesPerMeter);
            Assert.Equal(977m, CuttingPriceCalculator.Calculate(new("Лазер", 2.5m, 25, 2.3m), laser).StoredPrice);
            var plasma = await client.ReadPriceTableAsync(PriceTables.Plasma, TestContext.Current.CancellationToken);
            Assert.Null(plasma.Rates.Single(rate => rate.ThicknessMillimeters == 2.5m).RublesPerMeter);
            Assert.Equal(0, plasma.Rates.Single(rate => rate.ThicknessMillimeters == 3m).RublesPerMeter);
            Assert.Equal(original, await client.ReadAsync(TestContext.Current.CancellationToken));
            var saved = Assert.Single(await client.SetColorAsync([first], OrderColors.Ready, TestContext.Current.CancellationToken));
            Assert.Equal(first with { Color = OrderColors.Ready }, saved);
            var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => client.SetColorAsync(original, OrderColors.Shipped, TestContext.Current.CancellationToken));
            Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
            var reloaded = await client.ReadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(saved, reloaded.Single(row => row.Id == 42));
            Assert.Equal(original.Single(row => row.Id == 43), reloaded.Single(row => row.Id == 43));
            saved = Assert.Single(await client.SetDeliveryNumberAsync([saved], "БЛЦБ-'А' №42", TestContext.Current.CancellationToken));
            Assert.Equal(first with { Color = OrderColors.Ready, DeliveryNumber = "БЛЦБ-'А' №42" }, saved);
            saved = Assert.Single(await client.SetDeliveryNumberAsync([saved], "", TestContext.Current.CancellationToken));
            Assert.Null(saved.DeliveryNumber);
            Assert.Equal(saved, (await client.ReadAsync(TestContext.Current.CancellationToken)).Single(row => row.Id == 42));
            var history = await client.ReadAsync(TestContext.Current.CancellationToken);
            var draft = Draft() with { InitialColor = -25089, PartCount = 14, PunchCount = 31,
                SheetSize = "1500x3000, 1000x2000", CuttingDurationMinutes = 1786, HasBending = true, Comment = "Гнуть по чертежу №2" };
            var quote = await new CuttingPriceService(client).CalculateAsync(draft.PriceRequest, TestContext.Current.CancellationToken);
            var mismatch = await Assert.ThrowsAsync<OrderPriceChangedException>(() => client.CreateAsync(draft with {
                ExpectedQuote = quote with { RublesPerMeter = 1, StoredPrice = 57m } }, TestContext.Current.CancellationToken));
            Assert.Equal(quote, mismatch.Quote);
            Assert.Equal(history, await client.ReadAsync(TestContext.Current.CancellationToken));
            var created = await client.CreateAsync(draft with { ExpectedQuote = quote }, TestContext.Current.CancellationToken);
            Assert.Equal(draft.ToOrder(created.Id, 977m), created);
            Assert.Equal(created, (await client.ReadAsync(TestContext.Current.CancellationToken)).Single(row => row.Id == created.Id));
            Assert.Equal(history, (await client.ReadAsync(TestContext.Current.CancellationToken)).Where(row => row.Id != created.Id));
            Assert.Equal(1786L, created.CuttingDurationMinutes);
            Assert.True(created.HasBending); Assert.Equal(draft.Comment, created.Comment);
            var timed = (await client.ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
            var timedValues = timed with { CuttingTime = OrderCuttingTime.For(timed.ToSummary(), 1787) };
            var timedSaved = await client.UpdateAsync(new(timed, timedValues), TestContext.Current.CancellationToken);
            Assert.Equal(1787L, timedSaved.ToSummary().CuttingDurationMinutes);
            await client.DeleteAsync(timedSaved, TestContext.Current.CancellationToken);
            Assert.Equal(timedSaved, await client.RestoreAsync(timedSaved, TestContext.Current.CancellationToken));
            var batchDrafts = new[] { draft with { SpecificationNumber = "пакет-1" }, draft with { SpecificationNumber = "пакет-2", CuttingDurationMinutes = 2207 } };
            var batchHistory = await client.ReadAsync(TestContext.Current.CancellationToken);
            var stale = await Assert.ThrowsAsync<OrderBatchPriceChangedException>(() => client.CreateBatchAsync([batchDrafts[0], batchDrafts[1] with {
                ExpectedQuote = quote with { RublesPerMeter = 1, StoredPrice = 57m } }], TestContext.Current.CancellationToken));
            Assert.Equal(1, stale.Index); Assert.Equal(quote, stale.Quote);
            Assert.Equal(batchHistory, await client.ReadAsync(TestContext.Current.CancellationToken));
            var batchCreated = await client.CreateBatchAsync(batchDrafts, TestContext.Current.CancellationToken);
            Assert.Equal(2, batchCreated.Count); Assert.Equal(2207L, batchCreated[1].CuttingDurationMinutes);
            var batchReopened = await client.ReadAsync(TestContext.Current.CancellationToken);
            Assert.All(batchCreated, row => Assert.Contains(row, batchReopened));
            Assert.Equal(batchHistory, batchReopened.Where(row => !batchCreated.Any(createdRow => createdRow.Id == row.Id)));
            var raw = (await client.ReadRecordAsync(42, TestContext.Current.CancellationToken))!;
            var changed = await client.UpdateAsync(new(raw, raw with { Customer = "Правка через x86 'А'" }), TestContext.Current.CancellationToken);
            Assert.Equal(raw with { Customer = "Правка через x86 'А'", Price = 659m }, changed);
            await client.DeleteAsync(changed, TestContext.Current.CancellationToken);
            Assert.Null(await client.ReadRecordAsync(42, TestContext.Current.CancellationToken));
            Assert.Equal(changed, await client.RestoreAsync(changed, TestContext.Current.CancellationToken));
            Assert.Equal(changed, await client.ReadRecordAsync(42, TestContext.Current.CancellationToken));
            var beforePrices = await client.ReadAsync(TestContext.Current.CancellationToken);
            var gas = await client.ReadPriceTableAsync(PriceTables.GasOxygen, TestContext.Current.CancellationToken);
            var gasValues = PriceEditingTests.Replace(gas, "20", 37);
            Assert.True(PriceTableChange.Matches(gasValues, await client.SaveAsync(new(gas, gasValues), TestContext.Current.CancellationToken)));
            Assert.True(PriceTableChange.Matches(gasValues, await client.ReadPriceTableAsync(gas.Name, TestContext.Current.CancellationToken)));
            Assert.Equal(OrderWriteFailure.Conflict, (await Assert.ThrowsAsync<OrderWriteException>(() => client.SaveAsync(new(gas, gas), TestContext.Current.CancellationToken))).Failure);
            Assert.Equal(beforePrices, await client.ReadAsync(TestContext.Current.CancellationToken));
            var multiplePrices = await Assert.ThrowsAsync<OrderWriteException>(() => client.SaveAsync(new(plasma, plasma), TestContext.Current.CancellationToken));
            Assert.Equal(OrderWriteFailure.NotSaved, multiplePrices.Failure);
            Assert.True((await client.ReadPriceTableAsync(PriceCatalog.UnusedHansLaser, TestContext.Current.CancellationToken)).HasRow);
            var expandedGas = PriceExpansionTests.Append(gasValues, new PriceRate(28m, "28", 87));
            Assert.True(PriceTableChange.Matches(expandedGas, await client.SaveAsync(new(gasValues, expandedGas), TestContext.Current.CancellationToken)));
            Assert.True(PriceTableChange.Matches(expandedGas, await client.ReadPriceTableAsync(gasValues.Name, TestContext.Current.CancellationToken)));
            Assert.Equal(87m, CuttingPriceCalculator.Calculate(new("Газо-кислородная", 28m, 1, 1), expandedGas).StoredPrice);
            Assert.Equal(beforePrices, await client.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(root, "*.before-price-expansion-*.bak"));
            Assert.Empty(await client.ReadMachinesAsync(TestContext.Current.CancellationToken));
            var machine = MachineCatalogTests.Machine("Гидроабразив 'А'"); var machineCreation = MachineCatalogTests.Creation(machine);
            Assert.Equal(machine, await client.CreateMachineAsync(machineCreation, TestContext.Current.CancellationToken));
            Assert.Equal(machine, Assert.Single(await client.ReadMachinesAsync(TestContext.Current.CancellationToken)));
            Assert.True(PriceTableChange.Matches(new(machine.TableName, true, machineCreation.Rates),
                await client.ReadPriceTableAsync(machine.TableName, TestContext.Current.CancellationToken)));
            Assert.Equal(beforePrices, await client.ReadAsync(TestContext.Current.CancellationToken));
            var waterjetDraft = MachineCatalogTests.Draft(machine);
            var waterjetQuote = await new CuttingPriceService(client).CalculateAsync(waterjetDraft.PriceRequest, TestContext.Current.CancellationToken);
            var waterjetCreated = await client.CreateAsync(waterjetDraft with { ExpectedQuote = waterjetQuote }, TestContext.Current.CancellationToken);
            Assert.Equal(waterjetDraft.ToOrder(waterjetCreated.Id, 1200m), waterjetCreated);
            var waterjetOriginal = (await client.ReadRecordAsync(waterjetCreated.Id, TestContext.Current.CancellationToken))!;
            var waterjetSaved = await client.UpdateAsync(new(waterjetOriginal, waterjetOriginal with { CutLength = 3 }) { Machine = machine }, TestContext.Current.CancellationToken);
            Assert.Equal(1800m, waterjetSaved.Price);
            var historyBeforeRename = await client.ReadAsync(TestContext.Current.CancellationToken);
            var priceBeforeRename = await client.ReadPriceTableAsync(PriceTables.GasOxygen, TestContext.Current.CancellationToken);
            var renamedPrice = priceBeforeRename with { Rates = priceBeforeRename.Rates.Select(rate => rate.ThicknessMillimeters == 2.5m
                ? new PriceRate(2.8m, "2,8", 125) : rate).ToArray() };
            var renaming = new PriceTableChange(priceBeforeRename, renamedPrice);
            var usage = await client.ReadThicknessUsageAsync(renaming, TestContext.Current.CancellationToken);
            Assert.Equal("2,5", Assert.Single(usage).ColumnName);
            Assert.True(PriceTableChange.Matches(renamedPrice, await client.SaveAsync(renaming with { ConfirmedUsage = usage.ToArray() }, TestContext.Current.CancellationToken)));
            Assert.True(PriceTableChange.Matches(renamedPrice, await client.ReadPriceTableAsync(priceBeforeRename.Name, TestContext.Current.CancellationToken)));
            Assert.Equal(historyBeforeRename, await client.ReadAsync(TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(root, "*.before-price-edit-*.bak"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static OrderSummary Row() => new(42, null, "test", "", "", "", "", null, null, null);
    private static ManualOrderDraft Draft() => new(new(2026, 1, 2), "0102--44", "Имя 'А'", "Клиент", "Лазер", "Амг3", 2.5m,
        "1500x3000", "2", 25, "38,5", 2.3m, "", "");
}
