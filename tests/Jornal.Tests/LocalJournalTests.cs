using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class LocalJournalTests
{
    [Fact]
    public async Task RoundTripKeepsHistoricalValuesNullsRawCoefficientAndEveryPrice()
    {
        using var fixture = new LocalJournalFixture();
        await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        Assert.Equal(LocalJournalFixture.Orders, await store.ReadAsync(TestContext.Current.CancellationToken));
        foreach (var expected in LocalJournalFixture.Prices)
        {
            var actual = await store.ReadAsync(expected.Name, TestContext.Current.CancellationToken);
            Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.HasRow, actual.HasRow);
            Assert.Equal(expected.Rates, actual.Rates);
        }
    }

    [Fact]
    public async Task CreationCalculatesFromCopiedPricesAndSurvivesReopeningWithoutRepricingHistory()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var created = await store.CreateAsync(LocalJournalFixture.Draft, TestContext.Current.CancellationToken);
        Assert.Equal(43, created.Id);
        Assert.Equal((decimal)Math.Round(17d * 25 * 2.3d, MidpointRounding.ToEven), created.StoredCuttingPrice);
        Assert.Equal(977m, created.StoredCuttingPrice);
        Assert.Equal("2.3|-1|-1|2|Азот", created.StoredCoefficient);
        var reopened = await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken);
        Assert.Contains(created, reopened);
        foreach (var original in LocalJournalFixture.Orders) Assert.Contains(original, reopened);
        var next = await new LocalJournalStore(fixture.Path).CreateAsync(LocalJournalFixture.Draft, TestContext.Current.CancellationToken);
        Assert.Equal(44, next.Id);
    }

    [Fact]
    public async Task MissingTariffAndCancellationDoNotSaveIncompleteOrders()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var before = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        var store = new LocalJournalStore(fixture.Path);
        await Assert.ThrowsAsync<PriceCalculationException>(() => store.CreateAsync(LocalJournalFixture.Draft with { ThicknessMillimeters = 6m }, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateAsync(LocalJournalFixture.Draft, cancelled.Token));
        await Assert.ThrowsAsync<OrderWriteException>(() => store.CreateAsync(LocalJournalFixture.Draft with { CustomerName = "" }, TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.tmp"));
    }

    [Fact]
    public async Task BatchesKeepOtherFieldsAndRejectTheEntireStaleSelection()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var initial = await store.ReadAsync(TestContext.Current.CancellationToken);
        var updated = await store.SetColorAsync(initial, OrderColors.Ready, TestContext.Current.CancellationToken);
        Assert.Equal(initial.Select(row => row with { Color = OrderColors.Ready }).OrderBy(row => row.Id), updated.OrderBy(row => row.Id));
        var beforeConflict = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        var stale = new[] { updated.Single(row => row.Id == 42), initial.Single(row => row.Id == 7) };
        var conflict = await Assert.ThrowsAsync<OrderWriteException>(() => store.SetColorAsync(stale, OrderColors.Work, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, conflict.Failure);
        Assert.Equal(beforeConflict, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
        var numbered = await store.SetDeliveryNumberAsync(updated, " БЛЦБ-'12' ", TestContext.Current.CancellationToken);
        Assert.Equal(updated.Select(row => row with { DeliveryNumber = " БЛЦБ-'12' " }), numbered);
        var cleared = await store.SetDeliveryNumberAsync(numbered, "", TestContext.Current.CancellationToken);
        Assert.All(cleared, row => Assert.Null(row.DeliveryNumber));
        Assert.Equal(cleared.OrderBy(row => row.Id), (await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken)).OrderBy(row => row.Id));
    }

    [Fact]
    public async Task TwoStoreInstancesDoNotLoseChangesToDifferentFieldsOrDuplicateIds()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var first = new LocalJournalStore(fixture.Path); var second = new LocalJournalStore(fixture.Path);
        var expected = new[] { (await first.ReadAsync(TestContext.Current.CancellationToken))[0] };
        await Task.WhenAll(first.SetColorAsync(expected, OrderColors.Work, TestContext.Current.CancellationToken), second.SetDeliveryNumberAsync(expected, "БЛЦБ-12", TestContext.Current.CancellationToken));
        var row = (await first.ReadAsync(TestContext.Current.CancellationToken)).Single(row => row.Id == expected[0].Id);
        Assert.Equal(OrderColors.Work, row.Color); Assert.Equal("БЛЦБ-12", row.DeliveryNumber);
        var created = await Task.WhenAll(first.CreateAsync(LocalJournalFixture.Draft, TestContext.Current.CancellationToken), second.CreateAsync(LocalJournalFixture.Draft, TestContext.Current.CancellationToken));
        Assert.Equal(2, created.Select(order => order.Id).Distinct().Count());
        Assert.Equal(4, (await second.ReadAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task ExistingCopyCorruptFileAndForeignFileAreNeverOverwritten()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var original = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => fixture.InitializeAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
        var book = JsonNode.Parse(original)!; book["Version"] = 99;
        await File.WriteAllTextAsync(fixture.Path, book.ToJsonString(), TestContext.Current.CancellationToken);
        var corrupt = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<OrderReadException>(() => new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<OrderWriteException>(() => new LocalJournalStore(fixture.Path).CreateAsync(LocalJournalFixture.Draft, TestContext.Current.CancellationToken));
        Assert.Equal(corrupt, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => new LocalJournalStore(System.IO.Path.Combine(fixture.Directory, "OrderDB.mdb")));
    }

    [Fact]
    public async Task ImportReadsAllTablesFromOnePrivateSnapshotAndLeavesMdbBytesUntouched()
    {
        using var fixture = new LocalJournalFixture();
        var source = await fixture.MakeSourceAsync();
        var before = await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken);
        string? snapshot = null;
        var prices = new StubPrices();
        var importer = new LocalJournalImporter(path =>
        {
            Assert.NotEqual(source, path); Assert.Equal(before, File.ReadAllBytes(path));
            if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(System.IO.Path.GetDirectoryName(path)!));
            snapshot = path; return new StubOrders();
        }, path => { Assert.Equal(snapshot, path); return prices; });
        await importer.ImportAsync(source, fixture.Path, TestContext.Current.CancellationToken);
        Assert.Equal(PriceTables.All, prices.Requests);
        Assert.Equal(LocalJournalFixture.Orders, await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(snapshot));
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.Path));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path, TestContext.Current.CancellationToken))!;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(before)), (string?)json["SourceSha256"]);
    }

    [Fact]
    public async Task FailedImportDoesNotPublishPartialCopyOrReplaceAnExistingCopy()
    {
        using var fixture = new LocalJournalFixture(); var source = await fixture.MakeSourceAsync();
        var importer = new LocalJournalImporter(_ => new StubOrders(), _ => new StubPrices { Fail = true });
        await Assert.ThrowsAsync<PriceReadException>(() => importer.ImportAsync(source, fixture.Path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(fixture.Path));
        await fixture.InitializeAsync(); var before = await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => importer.ImportAsync(source, fixture.Path, TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(fixture.Directory, "*.tmp"));
    }

    private sealed class StubOrders : IOrderReader
    {
        public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderSummary>>(LocalJournalFixture.Orders);
    }
    private sealed class StubPrices : IPriceReader
    {
        public bool Fail { get; init; }
        public List<string> Requests { get; } = [];
        public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
        {
            Requests.Add(tableName);
            if (Fail && tableName == PriceTables.Plasma) throw new PriceReadException("Прайс отсутствует.");
            return Task.FromResult(LocalJournalFixture.Prices.Single(table => table.Name == tableName));
        }
    }
}

internal sealed class LocalJournalFixture : IDisposable
{
    public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "journal-local-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(Directory, "OrderDB" + LocalJournalStore.Extension);
    public static OrderSummary[] Orders =>
    [
        new(42, new(2026, 3, 27), "0327-1001", "Иван", "Тест 'А'\nновая строка", "HansLaser", "5083H111", 2.5m, 1234.5678m, -16711936)
        {
            SheetSize = "1500x3000", SheetCount = "2", CutLengthMeters = 25, WeightKilograms = "38.50", DeliveryNumber = null,
            StoredCoefficient = " 2.3 | 12 | 30 | 2 | Азот ", Coefficient = 2.3m, MaterialId = "2", CuttingGas = "Азот",
            PartCount = 12, PunchCount = 30, StoredOrderDateTime = new(2026, 3, 27, 8, 5, 19, DateTimeKind.Unspecified)
        },
        new(7, null, "старый номер", "", "Тест Б", "Лазер", "Амг3", null, null, null)
            { SheetCount = "", DeliveryNumber = "", StoredCoefficient = null, Coefficient = null, CutLengthMeters = null }
    ];
    public static PriceTable[] Prices => PriceTables.All.Select(name => new PriceTable(name, true,
        [new(2.5m, "2,5", 17), new(6m, "6", null), new(10m, "10", 0)])).ToArray();
    public static ManualOrderDraft Draft => new(new(2026, 3, 28), "0328-1002", "Иван", "Тест В", "HansLaser", "Амг3", 2.5m,
        "1500x3000", "2", 25, "38,5", 2.3m, "2", "Азот");
    public Task InitializeAsync() => LocalJournalStore.InitializeAsync(Path, Orders, Prices, "OrderDB.mdb", new string('a', 64));
    public async Task<string> MakeSourceAsync()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = System.IO.Path.Combine(Directory, "Original.mdb");
        var bytes = new byte[64]; Encoding.ASCII.GetBytes("Standard Jet DB\0").CopyTo(bytes, 4);
        await File.WriteAllBytesAsync(path, bytes); return path;
    }
    public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
}
