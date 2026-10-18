using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

public sealed partial class NativeAccessTests
{
    [Fact]
    public async Task ConcurrentJournalReadersAndAnotherClientWriterKeepAllOrderFields()
    {
        using var database = Database.Create();
        var token = TestContext.Current.CancellationToken;
        var day = new DateOnly(2026, 10, 18);
        var created = await new OleDbManualOrderCreator(database.Path).CreateAsync(CreationDraft() with
        { OrderDate = day, SpecificationNumber = "concurrent-refresh", HasBending = true, Comment = "Проверка", CuttingDurationMinutes = 123 }, token);
        var schema = database.Schema();
        IOrderReader reader = new AccessOrderReader(database.Path);
        var original = (await reader.ReadDayAsync(day, token)).Single(row => row.Id == created.Id);
        var color = unchecked((int)0xFFFF9DFF);
        var reads = Enumerable.Range(0, 8).Select(_ => reader.ReadDayAsync(day, token)).ToArray();
        var write = new OleDbOrderBatchEditor(database.Path).SetColorAsync([original], color, token);
        await Task.WhenAll(reads.Cast<Task>().Append(write));
        foreach (var rows in await Task.WhenAll(reads))
        {
            var row = rows.Single(value => value.Id == created.Id);
            Assert.Equal(original, row with { Color = original.Color });
        }
        var actual = (await reader.ReadDayAsync(day, token)).Single(row => row.Id == created.Id);
        Assert.Equal(original with { Color = color }, actual); Assert.Equal(schema, database.Schema());
    }

    [Fact]
    public async Task DayReadIncludesWholeDayAndExtrasWithoutChangingDatabase()
    {
        using var database = Database.Create();
        var creator = new OleDbManualOrderCreator(database.Path);
        var day = new DateOnly(2026, 10, 18);
        var first = await creator.CreateAsync(CreationDraft() with
        { OrderDate = day, SpecificationNumber = "period-midnight", HasBending = true, Comment = "Гибка", CuttingDurationMinutes = 1786 }, TestContext.Current.CancellationToken);
        var last = await creator.CreateAsync(CreationDraft() with
        { OrderDate = day, SpecificationNumber = "period-last-second", CuttingDurationMinutes = 1 }, TestContext.Current.CancellationToken);
        database.Execute($"UPDATE [Orders] SET [Дата]=#10/18/2026 23:59:59# WHERE [Код]={last.Id}");
        await creator.CreateAsync(CreationDraft() with
        { OrderDate = day.AddDays(1), SpecificationNumber = "period-next-day" }, TestContext.Current.CancellationToken);
        database.Execute($"UPDATE [Orders] SET [Дата]=NULL WHERE [Код] NOT IN ({first.Id},{last.Id}) AND [Спецификация]<>'period-next-day'");
        var hash = SHA256.HashData(File.ReadAllBytes(database.Path)); var schema = database.Schema();
        var expected = (await database.Read()).Where(row => row.OrderDate == day).OrderBy(row => row.Id).ToArray();
        IOrderReader reader = new AccessOrderReader(database.Path);
        var actual = (await reader.ReadDayAsync(day, TestContext.Current.CancellationToken)).OrderBy(row => row.Id).ToArray();
        Assert.Equal(expected, actual); Assert.Equal([first.Id, last.Id], actual.Select(row => row.Id).ToArray());
        Assert.True(actual[0].HasBending); Assert.Equal("Гибка", actual[0].Comment); Assert.Equal(1786, actual[0].CuttingDurationMinutes);
        Assert.Empty(await reader.ReadDayAsync(day.AddYears(-1), TestContext.Current.CancellationToken));
        Assert.Equal(schema, database.Schema()); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(database.Path)));
    }
}
