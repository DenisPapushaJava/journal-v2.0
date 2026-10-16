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

    private static AccessHostClient Client(AccessHostResponse response)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response)));
        return new("unused.mdb", "synthetic-host", process =>
        {
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
