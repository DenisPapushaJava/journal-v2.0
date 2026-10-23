using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class MaintenanceProtocolTests
{
    [Fact]
    public async Task FullRecordReadRequiresACompleteEnvelopeAndTheRequestedId()
    {
        var record = OrderMaintenanceTests.Record();
        Assert.Null(await Client(new(1, MaintenanceCompleted: true)).ReadRecordAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal(record, await Client(new(1, Record: record, MaintenanceCompleted: true)).ReadRecordAsync(42, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, Record: record)).ReadRecordAsync(42, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, Record: record with { Id = 99 }, MaintenanceCompleted: true)).ReadRecordAsync(42, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptedSuccessfulWritesAreUnconfirmedAndNeverClaimNotSaved()
    {
        var record = OrderMaintenanceTests.Record();
        var change = new OrderEditChange(record, record with { Customer = "Правка" });
        foreach (var operation in new Func<Task>[]
        {
            async () => await Client(new(1, Record: record with { Id = 0 }, MaintenanceCompleted: true)).RestoreAsync(record, TestContext.Current.CancellationToken),
            async () => await Client(new(1, Record: record with { Price = 999 }, MaintenanceCompleted: true)).RestoreAsync(record, TestContext.Current.CancellationToken),
            async () => await Client(new(1, MaintenanceCompleted: true)).UpdateAsync(change, TestContext.Current.CancellationToken),
            async () => await Client(new(1, Record: record, MaintenanceCompleted: true)).DeleteAsync(record, TestContext.Current.CancellationToken),
            async () => await Client(new(1, [])).DeleteAsync(record, TestContext.Current.CancellationToken)
        })
        {
            var error = await Assert.ThrowsAsync<OrderWriteException>(operation);
            Assert.Equal(OrderWriteFailure.OutcomeUnknown, error.Failure);
        }
    }

    [Fact]
    public async Task UpdateQuoteChangeIsTypedAndAnUnexpectedConfirmedPriceIsUnconfirmed()
    {
        var original = OrderMaintenanceTests.Record();
        var quote = CuttingPriceCalculator.Calculate(new("Лазер", 2.5m, 31, 1.25m), new(PriceTables.Laser, true, [new(2.5m, "2,5", 17)]));
        var change = new OrderEditChange(original, original with { Customer = "Новая" }, quote);
        var changed = await Assert.ThrowsAsync<OrderEditPriceChangedException>(() => Client(new(1, Error: "Прайс изменился", Failure: OrderWriteFailure.NotSaved, ChangedQuote: quote))
            .UpdateAsync(change, TestContext.Current.CancellationToken));
        Assert.Equal(quote, changed.Quote);
        var failure = await Assert.ThrowsAsync<OrderWriteException>(() => Client(new(1, Record: change.Values with { Price = 1 }, MaintenanceCompleted: true))
            .UpdateAsync(change, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.OutcomeUnknown, failure.Failure);
    }

    [Fact]
    public async Task OldHostCannotClaimThatItsMissingAnnotationsAreEmptyOrAcceptAnAnnotatedWrite()
    {
        var token = TestContext.Current.CancellationToken;
        var record = OrderMaintenanceTests.Record();
        var rows = await Client(new(1, [record.ToSummary()]), annotationsSupported: false).ReadAsync(token);
        Assert.True(Assert.Single(rows).AnnotationsUnavailable);
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, Record: record, MaintenanceCompleted: true), annotationsSupported: false).ReadRecordAsync(record.Id, token));
        var calls = 0;
        var client = Client(new(1, Error: "Unknown operation"), annotationsSupported: false, onStart: () => calls++);
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => client.CreateAsync(LocalJournalFixture.Draft with { HasBending = true }, token));
        Assert.Equal(OrderWriteFailure.NotSaved, error.Failure); Assert.Equal(1, calls);
        Assert.Equal(15, (int)AccessHostOperation.PrepareAnnotations);
        Assert.Equal(5, (int)AccessHostOperation.CreateOrder);
    }

    [Fact]
    public async Task PreparationRequiresExplicitCapabilityAndAnUnmixedAcknowledgement()
    {
        var token = TestContext.Current.CancellationToken;
        await Client(new(1, AnnotationsPrepared: true)).PrepareAnnotationsAsync(token);
        foreach (var response in new AccessHostResponse[] { new(1), new(1, [], AnnotationsPrepared: true), new(1, AnnotationsPrepared: false) })
            Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => Client(response).PrepareAnnotationsAsync(token))).Failure);
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, [], AnnotationsPrepared: true)).ReadAsync(token));
        var value = OrderAnnotations.For(LocalJournalFixture.Draft.ToOrder(42, 500), true, "Пометка");
        var encoded = JsonSerializer.Serialize(new AccessHostResponse(1, [LocalJournalFixture.Draft.ToOrder(42, 500) with { Annotations = value }], AnnotationsSupported: true));
        Assert.Equal(value, JsonSerializer.Deserialize<AccessHostResponse>(encoded)!.Orders![0].Annotations);
    }

    [Fact]
    public async Task OperationsRequireExplicitCapabilityAndPreparationAcknowledgement()
    {
        var token = TestContext.Current.CancellationToken;
        var row = LocalJournalFixture.Draft.ToOrder(42, 500);
        var legacy = await Client(new(1, [row]), operationsSupported: false).ReadAsync(token);
        Assert.True(Assert.Single(legacy).OperationsUnavailable);
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, Record: OrderRecord.FromSummary(row), MaintenanceCompleted: true), operationsSupported: false).ReadRecordAsync(42, token));
        await Client(new(1, OperationsPrepared: true)).PrepareOperationsAsync(token);
        foreach (var response in new AccessHostResponse[] { new(1), new(1, OperationsPrepared: false), new(1, [row], OperationsPrepared: true), new(1, AnnotationsPrepared: true, OperationsPrepared: true) })
            Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => Client(response).PrepareOperationsAsync(token))).Failure);
        Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => Client(new(1, OperationsPrepared: true), operationsSupported: false).PrepareOperationsAsync(token))).Failure);
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, [], OperationsPrepared: true)).ReadAsync(token));
        var starts = 0;
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => Client(new(1), onStart: () => starts++).CreateAsync(LocalJournalFixture.Draft with { HasChamfer = true }, token));
        Assert.Equal(OrderWriteFailure.NotSaved, error.Failure); Assert.Equal(1, starts);
        var unavailable = await Assert.ThrowsAsync<OrderWriteException>(() => Client(new(1, Error: "База данных недоступна", Failure: OrderWriteFailure.NotSaved)).PrepareOperationsAsync(token));
        Assert.Contains("База данных недоступна", unavailable.Message); Assert.Contains("Ввод сохранён в форме", unavailable.Message);
        Assert.Equal(19, (int)AccessHostOperation.PrepareOperations); Assert.Equal(18, (int)AccessHostOperation.UpdateMachineImport);
    }

    [Fact]
    public async Task MachineSettingsRequireCapabilitiesCompleteSnapshotsAndExplicitPreparation()
    {
        var token = TestContext.Current.CancellationToken;
        var rows = MachineSettings.Build([]).ToArray();
        Assert.Equal(rows, await Client(new(1, Settings: rows)).ReadMachineSettingsAsync(token));
        await Assert.ThrowsAsync<OrderReadException>(() => Client(new(1, Settings: rows), machineSettingsSupported: false).ReadMachineSettingsAsync(token));
        foreach (var response in new AccessHostResponse[] { new(1), new(1, Settings: []), new(1, Settings: [rows[0], rows[0], rows[2], rows[3]]), new(1, [], Settings: rows) })
            await Assert.ThrowsAsync<OrderReadException>(() => Client(response).ReadMachineSettingsAsync(token));
        await Client(new(1, MachineLabelsPrepared: true)).PrepareMachineLabelsAsync(token);
        foreach (var response in new AccessHostResponse[] { new(1), new(1, MachineLabelsPrepared: false), new(1, Settings: rows, MachineLabelsPrepared: true) })
            Assert.Equal(OrderWriteFailure.NotSaved, (await Assert.ThrowsAsync<OrderWriteException>(() => Client(response).PrepareMachineLabelsAsync(token))).Failure);
        var starts = 0;
        var client = Client(new(1), onStart: () => starts++);
        await client.UpdateMachineSettingsAsync(new(rows[0], rows[0].DisplayName, rows[0].SpecificationProfileId), token); Assert.Equal(0, starts);
        var failed = await Assert.ThrowsAsync<OrderWriteException>(() => client.UpdateMachineSettingsAsync(new(rows[0], "Другое название", rows[0].SpecificationProfileId), token));
        Assert.Equal(OrderWriteFailure.NotSaved, failed.Failure); Assert.Equal(1, starts);
        Assert.Equal(20, (int)AccessHostOperation.ReadMachineSettings); Assert.Equal(22, (int)AccessHostOperation.PrepareMachineLabels);
    }

    private static AccessHostClient Client(AccessHostResponse response, bool annotationsSupported = true, Action? onStart = null, bool operationsSupported = true, bool machineSettingsSupported = true)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response with { AnnotationsSupported = annotationsSupported, OperationsSupported = operationsSupported, MachineSettingsSupported = machineSettingsSupported })));
        return new("unused.mdb", "synthetic-host", process =>
        {
            onStart?.Invoke();
            if (OperatingSystem.IsWindows())
            {
                process.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::In.ReadLine() | Out-Null; [Console]::Out.Write([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "')))" })
                    process.StartInfo.ArgumentList.Add(arg);
            }
            else
            {
                process.StartInfo.FileName = "/bin/sh";
                process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add("IFS= read -r ignored\nprintf %s " + payload + " | base64 -d");
            }
            process.Start();
        });
    }
}
