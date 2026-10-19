using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Demo;
using Jornal.Infrastructure.Local;
using Jornal.Infrastructure.Trash;
using Xunit;

namespace Jornal.Tests;

public sealed class CuttingTimeTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("00:00", 0L)]
    [InlineData("29:46", 1786L)]
    [InlineData("467:13", 28033L)]
    [InlineData(" 1:02 ", 62L)]
    public void EditableDurationIsAnOptionalNumberOfWholeMinutes(string value, long? expected)
    { Assert.True(CuttingDuration.TryParse(value, out var minutes)); Assert.Equal(expected, minutes); }

    [Theory]
    [InlineData("1:2")]
    [InlineData("00:60")]
    [InlineData("-1:00")]
    [InlineData("1:00:00")]
    [InlineData("1,5")]
    [InlineData("999999999999999999999999:00")]
    public void InvalidDurationNeverBecomesZero(string value) => Assert.False(CuttingDuration.TryParse(value, out _));

    [Theory]
    [InlineData(22, "00:01")]
    [InlineData(5544, "01:33")]
    [InlineData(107160, "29:46")]
    [InlineData(1234216, "342:51")]
    [InlineData(0, "00:00")]
    public void ImportRoundsUpExactlyOnceWithoutWrappingDays(long seconds, string expected) =>
        Assert.Equal(expected, CuttingDuration.Format(CuttingDuration.RoundUpMinutes(TimeSpan.FromSeconds(seconds))));

    [Fact]
    public async Task CreateEditAndClearTimePreserveLegacyCoefficientAndProduceRequestedResult()
    {
        var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
        form.CuttingTime.Value = "29:46";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal(1786L, form.CreatedOrder!.CuttingDurationMinutes);
        var original = form.CreatedOrder with { StoredCuttingPrice = 4385 };
        var unchanged = OrderResultFormatter.Format([original with { CuttingTime = null }]);
        var withTime = OrderResultFormatter.Format([original]);
        Assert.Equal(unchanged.Replace(" ₽.", " ₽.     Время резки: 29:46"), withTime);
        var record = (await store.Maintenance.ReadRecordAsync(original.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(record, JsonSerializer.Deserialize<OrderRecord>(JsonSerializer.Serialize(record)));
        var edit = new EditOrderViewModel(record, store.Maintenance, "test", _ => Task.FromResult<OrderRecord?>(record));
        Assert.Equal("29:46", edit.CuttingTime.Value);
        edit.CuttingTime.Value = "36:47";
        var change = edit.BuildChange();
        Assert.Equal(record.Coefficient, change.Values.Coefficient);
        Assert.Equal(2207L, change.Values.ToSummary().CuttingDurationMinutes);
        var saved = await store.Maintenance.UpdateAsync(change, TestContext.Current.CancellationToken);
        var clear = new EditOrderViewModel(saved, store.Maintenance, "test", _ => Task.FromResult<OrderRecord?>(saved));
        clear.CuttingTime.Value = "";
        Assert.Null(clear.BuildChange().Values.CuttingTime);
    }

    [Fact]
    public async Task LegacyGeometryEditInvalidatesTimeButStatusAndDeliveryDoNot()
    {
        var form = ManualOrderTests.ValidForm(new DemoOrderStore()); form.CuttingTime.Value = "467:13";
        await form.SaveCommand.ExecuteAsync(null); var row = form.CreatedOrder!;
        Assert.Equal(28033L, (row with { Color = 123, DeliveryNumber = "changed", ManagerName = "Other" }).CuttingDurationMinutes);
        var changed = row with { CutLengthMeters = row.CutLengthMeters + 1 };
        Assert.True(changed.CuttingTimeNeedsReview); Assert.Null(changed.CuttingDurationMinutes);
        Assert.DoesNotContain("Время резки", OrderResultFormatter.Format([changed with { StoredCuttingPrice = 500 }]));
    }

    [Fact]
    public async Task PreparationFailureKeepsReadOnlyRowsAndCanBeRetried()
    {
        var init = new Initializer(); var store = new DemoOrderStore(); var reader = new OrderDurationPreparingReader(store, init);
        Assert.Equal(await store.ReadAsync(TestContext.Current.CancellationToken), await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(reader.PreparationWarning);
        init.Fail = false;
        await reader.ReadAsync(TestContext.Current.CancellationToken); Assert.Null(reader.PreparationWarning);
        await reader.ReadAsync(TestContext.Current.CancellationToken); Assert.Equal(2, init.Calls);
    }

    [Fact]
    public async Task LocalCopyAndDurableTrashKeepTimeAcrossRestartAndRestore()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var store = new LocalJournalStore(fixture.Path);
        var created = await store.CreateAsync(LocalJournalFixture.Draft with { CuttingDurationMinutes = 1786 }, TestContext.Current.CancellationToken);
        var record = (await new LocalJournalStore(fixture.Path).ReadRecordAsync(created.Id, TestContext.Current.CancellationToken))!;
        var archive = new JsonTrashStore(Path.Combine(fixture.Directory, "Trash"));
        var service = new OrderTrashService(store, new("local", fixture.Path), archive);
        var deleted = await service.DeleteAsync(record, TestContext.Current.CancellationToken);
        var restarted = new OrderTrashService(new LocalJournalStore(fixture.Path), new("local", fixture.Path), archive);
        Assert.Equal(record, await restarted.RestoreAsync(deleted.OperationId, TestContext.Current.CancellationToken));
        var reopened = (await new LocalJournalStore(fixture.Path).ReadAsync(TestContext.Current.CancellationToken)).Single(row => row.Id == record.Id);
        Assert.Equal(1786L, reopened.CuttingDurationMinutes);
        var text = OrderResultFormatter.Format([reopened]);
        var clipboard = ResultClipboardContent.FromText(text);
        Assert.Equal(text, clipboard.Text); Assert.Contains("29:46", clipboard.Html); Assert.Contains("29:46", clipboard.Rtf);
    }

    [Fact]
    public void LinuxExportReadsExactLargeWholeMinutesAndRejectsUnknownVersions()
    {
        var hash = new string('A', 64);
        var csv = "OrderId,DurationMinutes,ParametersHash,FormatVersion\n42,28033,\"" + hash + "\",1\n";
        Assert.Equal(new OrderCuttingTime(28033, hash), OrderExtrasData.ReadExport(new StringReader(csv), TestContext.Current.CancellationToken)[42]);
        Assert.Throws<OrderReadException>(() => OrderExtrasData.ReadExport(new StringReader(csv.Replace(",1\n", ",2\n")), TestContext.Current.CancellationToken));
        Assert.Throws<OrderReadException>(() => OrderExtrasData.ReadExport(new StringReader(csv.Replace("28033", "-1")), TestContext.Current.CancellationToken));
        Assert.Throws<OrderReadException>(() => OrderExtrasData.ReadExport(new StringReader(csv + csv.Split('\n')[1] + "\n"), TestContext.Current.CancellationToken));
    }

    private sealed class Initializer : IOrderDurationInitializer
    {
        public bool Fail = true; public int Calls;
        public Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail ? Task.FromException(new OrderWriteException(OrderWriteFailure.NotSaved, "Database busy")) : Task.CompletedTask;
        }
    }
}
