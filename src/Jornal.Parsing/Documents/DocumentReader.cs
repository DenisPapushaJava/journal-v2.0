using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace Jornal.Parsing.Documents;

public sealed class DocumentReader
{
    public async Task<SourceDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => await ReadHtmlAsync(path, cancellationToken),
            ".pdf" => await Task.Run(() => ReadPdf(path, cancellationToken), cancellationToken),
            _ => throw new NotSupportedException("Поддерживаются только HTML и текстовые PDF.")
        };
    }

    private static async Task<SourceDocument> ReadHtmlAsync(string path, CancellationToken cancellationToken)
    {
        // Let the HTML parser detect encoding from BOM/meta instead of assuming UTF-8.
        await using var stream = File.OpenRead(path);
        using var document = await new HtmlParser().ParseDocumentAsync(stream, cancellationToken);
        foreach (var element in document.QuerySelectorAll("script, style"))
        {
            element.Remove();
        }

        var tables = document.QuerySelectorAll("table").OfType<IHtmlTableElement>()
            .Select(table => new HtmlTable(table.Rows
                .Where(row => ReferenceEquals(row.Closest("table"), table))
                .Select(row => new HtmlRow(row.Cells.Select(cell => new HtmlCell(
                    cell.TextContent, cell.ColumnSpan, cell.RowSpan)).ToArray()))
                .ToArray()))
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        return new SourceDocument(DocumentKind.Html, document.Body?.TextContent, tables, []);
    }

    private static SourceDocument ReadPdf(string path, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(path);
        var pages = new List<PdfPage>();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var words = page.GetWords(NearestNeighbourWordExtractor.Instance).Select(word => new PdfWord(
                word.Text, word.BoundingBox.Left, word.BoundingBox.Bottom,
                word.BoundingBox.Right, word.BoundingBox.Top)).ToArray();
            pages.Add(new PdfPage(page.Number, page.Width, page.Height, words));
        }

        return new SourceDocument(DocumentKind.Pdf, null, [], pages);
    }
}
