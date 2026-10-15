using Jornal.Parsing.Documents;
using Xunit;

namespace Jornal.Tests;

public sealed class DocumentReaderTests
{
    [Fact]
    public async Task PreservesHtmlCellsAndSpansWithoutSearchingInsidePartNames()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jornal-{Guid.NewGuid():N}.htm");
        try
        {
            await File.WriteAllTextAsync(path, """
                <html><head><meta charset="utf-8"></head><body>
                <table><tr><td colspan="2">@2026-03-26-1130--ЛАЗЕР</td></tr>
                <tr><td>Материал</td><td>5083H111</td></tr>
                <tr><td>Время</td><td>168:59</td></tr>
                <tr><td> *@служебный признак</td></tr></table>
                <table><tr><th>Название</th></tr>
                <tr><td>Очень длинная деталь Материал Вес 99 кг.rcam &amp; №</td></tr></table>
                </body></html>
                """, TestContext.Current.CancellationToken);

            var result = await new DocumentReader().ReadAsync(path, TestContext.Current.CancellationToken);

            Assert.Equal(DocumentKind.Html, result.Kind);
            Assert.Equal(2, result.Tables.Count);
            Assert.Equal(2, result.Tables[0].Rows[0].Cells[0].ColumnSpan);
            Assert.Equal("5083H111", result.Tables[0].Rows[1].Cells[1].Text);
            Assert.Equal("168:59", result.Tables[0].Rows[2].Cells[1].Text);
            Assert.Equal(" *@служебный признак", result.Tables[0].Rows[3].Cells[0].Text);
            Assert.Equal("Очень длинная деталь Материал Вес 99 кг.rcam & №", result.Tables[1].Rows[1].Cells[0].Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnsupportedInputIsNotGuessedFromItsName()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => new DocumentReader().ReadAsync(
            "Материал 5083H111.txt", TestContext.Current.CancellationToken));
    }
}
