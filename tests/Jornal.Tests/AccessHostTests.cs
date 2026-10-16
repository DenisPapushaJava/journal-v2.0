using System.Runtime.InteropServices;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class AccessHostTests
{
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
    }

    [Fact]
    public async Task MissingHostDoesNotTurnAFailedStartIntoAnUnknownCommit()
    {
        var client = new AccessHostClient("unused.mdb", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"));
        var error = await Assert.ThrowsAsync<OrderWriteException>(() => client.SetColorAsync([Row()], OrderColors.Work, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, error.Failure);
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
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotStartAHost()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var client = new AccessHostClient("unused.mdb", "missing.exe");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SetColorAsync([Row()], OrderColors.Work, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadPriceTableAsync(PriceTables.Laser, cancellation.Token));
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
            var client = new AccessHostClient(path, host);
            var original = await client.ReadAsync(TestContext.Current.CancellationToken);
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
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static OrderSummary Row() => new(42, null, "test", "", "", "", "", null, null, null);
}
