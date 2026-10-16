using System.Security.Cryptography;
using System.Text.Json;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class HansPdfParserTests
{
    [Fact]
    public void UsesOrderCellTotalsAndSheetColumnsWithPdfEvidence()
    {
        var report = Parse(HansSample.Document());
        Assert.True(report.IsComplete, Messages(report)); Assert.Empty(report.Issues);
        var actual = HansSpecificationNormalizer.Normalize(report);
        Assert.Equal(new NormalizedHansSpecification(new(2026, 5, 14), "0514-0078", "HansLaser", "Сплав_Х77", "2", "N2",
            6, "1500x4000, 1000x3000", "3", 33, "48.2", 14, 31, 1.1m), actual);
        Assert.Equal(TimeSpan.FromHours(342) + TimeSpan.FromMinutes(50) + TimeSpan.FromSeconds(16), report.CuttingDuration!.Value);
        Assert.Equal(2, report.SheetLayouts.Count);
        var source = Assert.IsType<PdfValueSource>(Assert.Single(report.CutLengthMillimeters!.Sources));
        Assert.Equal(1, source.Page); Assert.Equal("32500.5", source.RawText); Assert.True(source.Right > source.Left);
        Assert.Equal(32500.5m, report.CutLengthMillimeters.Value);
    }

    [Theory]
    [InlineData(0.6, 100, 23)]
    [InlineData(1.7, 5, 60)]
    public void ObjectOrderScaleAndPagePositionDoNotChangeFields(double scale, double dx, double dy)
    {
        var original = HansSample.Document();
        var shifted = original with { Pages = original.Pages.Select(page => page with
        {
            Width = page.Width * scale + dx, Height = page.Height * scale + dy,
            Words = page.Words.Reverse().Select(word => word with { Left = word.Left * scale + dx, Right = word.Right * scale + dx,
                Bottom = word.Bottom * scale + dy, Top = word.Top * scale + dy }).ToArray()
        }).ToArray() };
        Assert.Equal(HansSpecificationNormalizer.Normalize(Parse(original)), HansSpecificationNormalizer.Normalize(Parse(shifted)));
    }

    [Fact]
    public void ReorderedColumnsAndExtraUnderscoresKeepTheirMeaning()
    {
        var doc = HansSample.Map(HansSample.Document(), word =>
        {
            if (word.Top > 650 || word.Bottom < 560) return word;
            var shift = word.Left is >= 285 and < 365 ? 90 : word.Left is >= 365 and < 425 ? -90 : 0;
            return word with { Left = word.Left + shift, Right = word.Right + shift };
        });
        Assert.Equal(HansSpecificationNormalizer.Normalize(Parse(HansSample.Document())), HansSpecificationNormalizer.Normalize(Parse(doc)));
    }

    [Fact]
    public void WrappedOrderIdentifierUsesItsOwnDateAndKeepsLeadingZeroes()
    {
        var doc = HansSample.Document(); var page = doc.Pages[0];
        page = page with { Words = page.Words.Where(word => word.Text != "2026-05-14--0078--HANSLASER.Dsp")
            .Concat([new PdfWord("2026-05-14--", 130, 765, 200, 772), new PdfWord("0078--HANSLASER.Dsp", 130, 755, 250, 762)]).ToArray() };
        var report = Parse(doc with { Pages = [page] });
        Assert.True(report.IsComplete, Messages(report));
        Assert.Equal("0514-0078", report.Header!.Value.JournalNumber); Assert.Equal(new(2026, 5, 14), report.Header.Value.OrderDate);
    }

    [Theory]
    [InlineData("Заказ без распознаваемого номера")]
    [InlineData("2026-02-30-0078--HANSLASER.Dsp")]
    public void MissingOrInvalidOrderDateCannotFallBackToPrintDateOrFilename(string order)
    {
        var report = Parse(HansSample.Replace(HansSample.Document(), "2026-05-14--0078--HANSLASER.Dsp", order));
        Assert.Null(report.Header);
        var preview = Jornal.Desktop.Import.SpecificationImportPreview.Create("2040-12-31-9999--HANSLASER.pdf", report);
        Assert.Empty(preview.Fields["OrderDate"].Value);
        Assert.Empty(preview.Fields["SpecificationNumber"].Value);
        Assert.Contains(report.Issues, issue => issue.Field == SpecificationField.Header && issue.Severity == SpecificationIssueSeverity.Error);
    }

    [Fact]
    public void SheetColumnWordsInsideDetailsCannotStartAnotherSheetTable()
    {
        var doc = HansSample.Document(); var page = doc.Pages[0];
        var misleading = page.Words.Where(word => word.Bottom is >= 580 and <= 640).Select(word => word with
        { Bottom = word.Bottom - 200, Top = word.Top - 200, Text = word.Text == "Сплав_Х77_2" ? "Другая_0" : word.Text });
        var report = Parse(doc with { Pages = [page with { Words = page.Words.Concat(misleading).ToArray() }, doc.Pages[1]] });
        Assert.True(report.IsComplete, Messages(report)); Assert.Equal("Сплав_Х77", report.Material!.Value);
        Assert.Equal(2, report.SheetLayouts.Count);
    }

    [Fact]
    public void ContinuedSheetTablesAreReadButDetailPagesCannotReplaceTotals()
    {
        var source = HansSample.Document(); var first = source.Pages[0];
        var continuation = first with { Number = 2, Words = first.Words.Where(word => word.Bottom <= 665 && word.Bottom > 560)
            .Where(word => word.Text != "1, 1").ToArray() };
        var report = Parse(source with { Pages = [first, continuation, HansSample.DetailsPage()] });
        Assert.Contains(report.Issues, issue => issue.Code == "SheetCountMismatch");
        Assert.Empty(Jornal.Desktop.Import.SpecificationImportPreview.Create("unrelated.pdf", report).Fields["SheetCount"].Value);
        Assert.Equal(14, report.PartCount!.Value); Assert.Equal(31, report.PunchCount!.Value);
        var corrected = HansSample.Replace(source, "3", "6");
        var complete = Parse(corrected with { Pages = [corrected.Pages[0], continuation, HansSample.DetailsPage() with { Number = 3 }] });
        Assert.True(complete.IsComplete, Messages(complete)); Assert.Equal(4, complete.SheetLayouts.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("-1")]
    public void MaterialSuffixIsSeparateAndGasIsNotGuessedFromIt(string id)
    {
        var doc = HansSample.Replace(HansSample.Document(), "Сплав_Х77_2", "Материал_ещё_X_" + id);
        doc = HansSample.Replace(doc, "N2", "Ar");
        var actual = HansSpecificationNormalizer.Normalize(Parse(doc));
        Assert.Equal("Материал_ещё_X", actual.Material); Assert.Equal(id, actual.MaterialId); Assert.Equal("Ar", actual.CuttingGas);
    }

    [Theory]
    [InlineData("Вес", "Масса", SpecificationField.Weight)]
    [InlineData("48.2", "неизвестно", SpecificationField.Weight)]
    [InlineData("32500.5", "99999999999999999999999999999", SpecificationField.CutLength)]
    [InlineData("1", "1.3", SpecificationField.SheetCount)]
    [InlineData("Сплав_Х77_2", "Сплав_Х77", SpecificationField.MaterialId)]
    [InlineData("Сплав_Х77_2", "Сплав_Х77_99", SpecificationField.MaterialId)]
    [InlineData("N2", "", SpecificationField.CuttingGas)]
    public void MissingInvalidOrOutOfRangeValuesAreNotSilentlyReplaced(string before, string after, SpecificationField field)
    {
        var report = Parse(HansSample.Replace(HansSample.Document(), before, after));
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, issue => issue.Field == field && issue.Severity == SpecificationIssueSeverity.Error);
        Assert.Throws<ArgumentException>(() => HansSpecificationNormalizer.Normalize(report));
    }

    [Theory]
    [InlineData("Сплав_Х77_1", "N2", "6", SpecificationField.MaterialId)]
    [InlineData("Другая_2", "N2", "6", SpecificationField.Material)]
    [InlineData("Сплав_Х77_2", "O2", "6", SpecificationField.CuttingGas)]
    [InlineData("Сплав_Х77_2", "N2", "8", SpecificationField.Dimensions)]
    public void ConflictingSheetRowsLeaveTheAffectedFieldForManualReview(string material, string gas, string thickness, SpecificationField field)
    {
        var doc = HansSample.Map(HansSample.Document(), word => word.Bottom == 580 ? word with
        { Text = word.Text == "Сплав_Х77_2" ? material : word.Text == "N2" ? gas : word.Left == 440 ? thickness : word.Text } : word);
        var report = Parse(doc);
        Assert.False(report.IsComplete);
        Assert.Contains(report.Issues, issue => issue.Field == field && issue.Severity == SpecificationIssueSeverity.Error);
    }

    [Fact]
    public void ConflictingOrderCellsAcrossPagesAreNotResolvedUsingTheFirstPage()
    {
        var doc = HansSample.Document();
        var other = HansSample.Replace(doc, "2026-05-14--0078--HANSLASER.Dsp", "2026-05-15-0001--HANSLASER.Dsp").Pages[0] with { Number = 2 };
        var report = Parse(doc with { Pages = [doc.Pages[0], other] });
        Assert.Null(report.Header); Assert.Null(report.CuttingType);
        Assert.Contains(report.Issues, issue => issue.Field == SpecificationField.Header && issue.Code == "ConflictingValues");
    }

    [Fact]
    public void InvalidDurationIsAWarningAndCannotEraseReliableDatabaseFields()
    {
        var report = Parse(HansSample.Replace(HansSample.Document(), "342:50:16", "342:99:16"));
        Assert.True(report.IsComplete, Messages(report)); Assert.Null(report.CuttingDuration);
        Assert.Contains(report.Issues, issue => issue.Field == SpecificationField.CuttingDuration && issue.Severity == SpecificationIssueSeverity.Warning);
        Assert.Equal("3", HansSpecificationNormalizer.Normalize(report).SheetCount);
    }

    [Fact]
    public void UnknownDocumentsScansAndCancellationCannotProduceAnOrder()
    {
        Assert.Contains(Parse(new(DocumentKind.Pdf, null, [], [new(1, 600, 850, [])])).Issues, issue => issue.Code == "MissingTextLayer");
        Assert.False(Parse(new(DocumentKind.Html, "@", [], [])).IsComplete);
        Assert.False(Parse(new(DocumentKind.Pdf, null, [], [HansSample.DetailsPage()])).IsComplete);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new HansPdfSpecificationParser().Parse(HansSample.Document(), cancellation.Token));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1000", 1)]
    [InlineData("1000.001", 2)]
    [InlineData("32500.5", 33)]
    public void LengthUsesTheOriginalHansCeilingInsteadOfBlockPlusOne(string millimeters, int expected) =>
        Assert.Equal(expected, HansSpecificationNormalizer.LegacyCutLengthMeters(decimal.Parse(millimeters, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task OptionalPrivateCorpusMatchesIndependentPopplerObservationsAndDoesNotChangeSources()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_HANS_SPEC_MANIFEST");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(path), "Частный корпус HansLaser не задан.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken));
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var file = item.GetProperty("Path").GetString()!;
            var before = SHA256.HashData(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
            Assert.Equal(item.GetProperty("Sha256").GetString(), Convert.ToHexStringLower(before));
            var report = Parse(await new DocumentReader().ReadAsync(file, TestContext.Current.CancellationToken));
            Assert.True(report.IsComplete, item.GetProperty("Id").GetString() + ": " + Messages(report));
            Assert.Equal(item.GetProperty("Expected").Deserialize<NormalizedHansSpecification>(), HansSpecificationNormalizer.Normalize(report));
            var expected = item.GetProperty("Raw");
            Assert.Equal(expected.GetProperty("Length").GetDecimal(), report.CutLengthMillimeters!.Value);
            Assert.Equal(expected.GetProperty("Weight").GetDecimal(), report.WeightKilograms!.Value);
            Assert.Equal(expected.GetProperty("DurationSeconds").GetInt64(), (long)report.CuttingDuration!.Value.TotalSeconds);
            Assert.Equal(expected.GetProperty("Layouts").GetInt32(), report.SheetLayouts.Count);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken)));
        }
    }

    private static SpecificationParseResult Parse(SourceDocument source) => new HansPdfSpecificationParser().Parse(source, TestContext.Current.CancellationToken);
    private static string Messages(SpecificationParseResult report) => string.Join("; ", report.Issues.Select(issue => issue.Message));
}

internal static class HansSample
{
    public static SourceDocument Document()
    {
        var words = new List<PdfWord>();
        void Add(string text, double x, double y, double width = 40) => words.Add(new(text, x, y, x + width, y + 7));
        Add("@2026.06.01, 10:00:00", 400, 810, 130);
        Add("Заказ", 35, 780, 25); Add("6 mm", 210, 780, 25); Add("2026-05-14--0078--HANSLASER.Dsp", 130, 765, 155);
        Add("Вес", 310, 780, 20); Add("48.2", 500, 775, 20);
        Add("Длина реза:(мм)", 35, 745, 85); Add("32500.5", 210, 745, 40); Add("Листов", 310, 745, 35); Add("3", 500, 745, 10);
        Add("Общие кол-во деталей:", 35, 723, 100); Add("14", 210, 723, 15);
        Add("Использованная площадь", 310, 725, 85); Add("12.2", 500, 725, 20);
        Add("Пробивки:", 35, 700, 50); Add("31", 210, 700, 15); Add("Время обработки, всего", 310, 700, 100); Add("342:50:16", 500, 700, 40);
        Add("Листы", 35, 660);
        Add("№", 40, 640, 10); Add("Размер X", 100, 640, 40); Add("Размер Y", 210, 640, 40); Add("Материал", 300, 640, 45);
        Add("Gas", 390, 640, 20); Add("Толщ. (мм)", 440, 640, 40); Add("К-во", 525, 640, 20);
        Add("(мм)", 105, 628, 20); Add("(мм)", 215, 628, 20);
        foreach (var (y, x, height, count, index) in new[] { (610, "4000", "1500", "1", "1, 1"), (580, "3000", "1000", "2", "2") })
        {
            Add(index, 40, y, 15); Add(x, 105, y, 25); Add(height, 215, y, 25); Add("Сплав_Х77_2", 300, y, 55);
            Add("N2", 390, y, 15); Add("6", 440, y, 10); Add(count, 525, y, 10);
        }
        Add("Детали", 35, 520); Add("Материал: Вес 9999 2020-01-01-9999--HANSLASER.Dsp", 100, 490, 350);
        Add("Пробивки:", 35, 470); Add("999999", 210, 470); Add("Страница 1 из 2", 450, 30, 100);
        return new(DocumentKind.Pdf, null, [], [new(1, 600, 850, words), DetailsPage()]);
    }
    public static PdfPage DetailsPage() => new(2, 600, 850, [new("№", 30, 790, 40, 797), new("Имя", 100, 790, 120, 797),
        new("Вид", 200, 790, 220, 797), new("Материал", 300, 790, 345, 797),
        new("Заказ", 35, 730, 60, 737), new("1999-01-01-0001--HANSLASER.Dsp", 130, 730, 285, 737)]);
    public static SourceDocument Map(SourceDocument doc, Func<PdfWord, PdfWord> map) => doc with
    { Pages = doc.Pages.Select(page => page with { Words = page.Words.Select(map).ToArray() }).ToArray() };
    public static SourceDocument Replace(SourceDocument doc, string before, string after) => Map(doc, word => word.Text == before ? word with { Text = after } : word);
}
