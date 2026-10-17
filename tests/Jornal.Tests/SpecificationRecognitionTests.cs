using System.Text.Json;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class SpecificationRecognitionTests
{
    [Theory]
    [InlineData("<html><body>Спецификация № 6 мм 2026-05-14-0078--ЛАЗЕР.rcam Материал Вес Длина реза</body></html>")]
    [InlineData("<table><tr><td>Спецификация № 2026-05-14-0078</td></tr><tr><td>Материал</td><td>Ст3</td></tr></table>")]
    [InlineData("<table><tr><td>Листов:</td><td>3</td><td>Габариты:</td><td>6x4000x1500</td></tr></table>")]
    [InlineData("<table><tr><td>Всего по заказу:</td><td>Вес:</td><td>5</td></tr></table>")]
    [InlineData("<html><script>Спецификация № 2026-05-14-0078</script><body>Обычная страница</body></html>")]
    public async Task ForeignHtmlAndIsolatedKeywordsAreRejectedWithoutOpeningForm(string html)
    {
        using var sample = new SpecificationImportTests.Sample(html);
        var exception = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService().ReadAsync(sample.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.UnrecognizedSpecification, exception.Reason);
        var store = new DemoOrderStore(); var model = new MainWindowViewModel(store, store) { ResultText = "Письмо" };
        Assert.Null(await model.ReadSpecificationAsync([sample.Path], new SpecificationImportService(), TestContext.Current.CancellationToken));
        Assert.True(model.HasSpecificationImportError); Assert.True(model.ShowSpecificationRejection);
        Assert.Null(model.ErrorMessage); Assert.Equal("Письмо", model.ResultText);
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Theory]
    [InlineData("<td>Вес:(кг)</td><td>48.2</td>")]
    [InlineData("<td>Пробивки:</td><td>31</td>")]
    [InlineData("Спецификация № 6 мм 2026-05-14-0078--ЛАЗЕР.rcam")]
    public async Task RecognizedStructureDoesNotRequireEveryFieldOrValidHeader(string missing)
    {
        using var sample = new SpecificationImportTests.Sample(SpecificationImportTests.Sample.Html.Replace(missing, ""));
        var preview = await new SpecificationImportService().ReadAsync(sample.Path, TestContext.Current.CancellationToken);
        Assert.True(preview.Report.IsRecognized); Assert.False(preview.Report.IsComplete);
        Assert.NotEmpty(preview.Diagnostics);
    }

    [Fact]
    public void PdfWithOneKeywordOrCopiedOrderIdentifierIsNotHansLaser()
    {
        var document = new SourceDocument(DocumentKind.Pdf, null, [], [new(1, 600, 850,
            [new("HANSLASER", 10, 10, 100, 20), new("Материал", 10, 30, 100, 40), new("2026-05-14--0078--HANSLASER.Dsp", 10, 50, 300, 60)])]);
        Assert.False(new HansPdfSpecificationParser().Parse(document, TestContext.Current.CancellationToken).IsRecognized);
        Assert.Equal(DocumentRejection.UnrecognizedSpecification,
            Assert.Throws<DocumentRejectedException>(() => SpecificationProfileCatalog.Default.Parse(document, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public void MissingTextLayerAndInvalidCoordinatesHaveSeparateReasons()
    {
        var empty = new SourceDocument(DocumentKind.Pdf, null, [], [new(1, 600, 850, [])]);
        Assert.Equal(DocumentRejection.MissingTextLayer, Assert.Throws<DocumentRejectedException>(() => SpecificationProfileCatalog.Default.Parse(empty, TestContext.Current.CancellationToken)).Reason);
        var broken = HansSample.Map(HansSample.Document(), word => word with { Left = double.NaN });
        Assert.Equal(DocumentRejection.InvalidDocument, Assert.Throws<DocumentRejectedException>(() => SpecificationProfileCatalog.Default.Parse(broken, TestContext.Current.CancellationToken)).Reason);
    }

    [Theory]
    [InlineData("48.2", "неизвестно")]
    [InlineData("2026-05-14--0078--HANSLASER.Dsp", "неизвестный номер")]
    [InlineData("N2", "")]
    public void PartialHansIsRecognizedWithoutInventingValues(string before, string after)
    {
        var document = HansSample.Replace(HansSample.Document(), before, after);
        var result = SpecificationProfileCatalog.Default.Parse(document, TestContext.Current.CancellationToken);
        Assert.True(result.IsRecognized); Assert.False(result.IsComplete);
    }

    [AvaloniaFact]
    public async Task RealForeignAndScanPdfAreRejectedBeforeForm()
    {
        using var pdf = new HansPdfFile(new SourceDocument(DocumentKind.Pdf, null, [],
            [new(1, 600, 850, [new("Счёт на оплату", 20, 750, 200, 760), new("Материал Вес", 20, 700, 200, 710)])]));
        var rejected = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService().ReadAsync(pdf.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.UnrecognizedSpecification, rejected.Reason);
        using var empty = new HansPdfFile(new SourceDocument(DocumentKind.Pdf, null, [], [new(1, 600, 850, [])]));
        var noText = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService().ReadAsync(empty.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.MissingTextLayer, noText.Reason);
    }

    [AvaloniaFact]
    public async Task ResourceLimitsBrokenAndUnavailableFilesHaveExpectedRejections()
    {
        using var html = new SpecificationImportTests.Sample();
        var bytes = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService(limits: new(MaximumBytes: 32)).ReadAsync(html.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.TooLarge, bytes.Reason);
        using var pdf = new HansPdfFile();
        var pages = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService(limits: new(MaximumPdfPages: 1)).ReadAsync(pdf.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.TooManyPages, pages.Reason);
        File.WriteAllText(pdf.Path, "This is not PDF");
        var broken = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService().ReadAsync(pdf.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.InvalidDocument, broken.Reason);
        File.Delete(pdf.Path);
        var missing = await Assert.ThrowsAsync<DocumentRejectedException>(() => new SpecificationImportService().ReadAsync(pdf.Path, TestContext.Current.CancellationToken));
        Assert.Equal(DocumentRejection.FileUnavailable, missing.Reason);
    }

    [Fact]
    public void NewProfileCanShareExistingContainerAndAmbiguityDoesNotPickFirst()
    {
        var profile = new OtherHtmlProfile();
        var catalog = new SpecificationProfileCatalog([new BlockHtmlSpecificationParser(), profile]);
        var document = new SourceDocument(DocumentKind.Html, "OTHER-SPEC", [], []);
        var report = catalog.Parse(document, TestContext.Current.CancellationToken);
        Assert.Equal(profile.Id, report.Profile);
        Assert.Contains("Другой отчёт", catalog.SupportedDescription);
        Assert.Null(SpecificationImportService.CheckFiles(["future.HTML"], catalog));
        Assert.Equal("77", SpecificationImportPreview.Create("future.html", report, catalog).Fields["CutLength"].Value);
        var ambiguous = new SpecificationProfileCatalog([profile, new OtherHtmlProfile("other-html/v2")]);
        Assert.Equal(DocumentRejection.AmbiguousSpecification,
            Assert.Throws<DocumentRejectedException>(() => ambiguous.Parse(document, TestContext.Current.CancellationToken)).Reason);
    }

    [Fact]
    public async Task RegisteredProfileWorksThroughFileReaderAndPreviewWithoutChangingDropRouting()
    {
        using var file = new SpecificationImportTests.Sample("<html><body>OTHER-SPEC</body></html>");
        var catalog = new SpecificationProfileCatalog([new BlockHtmlSpecificationParser(), new OtherHtmlProfile()]);
        var preview = await new SpecificationImportService(catalog).ReadAsync(file.Path, TestContext.Current.CancellationToken);
        Assert.Equal("other-html/v1", preview.Report.Profile);
        Assert.Equal("77", preview.Fields["CutLength"].Value);
    }

    [Fact]
    public async Task RejectedReplacementPreservesEveryFieldQuoteAndPreviousImport()
    {
        using var good = new SpecificationImportTests.Sample();
        var form = new NewOrderViewModel(new DemoOrderStore());
        await form.RequestImportAsync(good.Path, TestContext.Current.CancellationToken);
        var previous = form.ImportPreview;
        var fields = form.Fields.Select(field => field.Value).ToArray();
        var price = form.PriceText;
        using var other = new SpecificationImportTests.Sample("<html>Другой документ</html>");
        await form.RequestImportAsync(other.Path, TestContext.Current.CancellationToken);
        await form.ReplaceImportCommand.ExecuteAsync(null);
        Assert.Equal(fields, form.Fields.Select(field => field.Value));
        Assert.Same(previous, form.ImportPreview); Assert.Equal(price, form.PriceText);
        Assert.True(form.HasImportError);
    }

    [Fact]
    public async Task OptionalPrivateCorpusRemainsRecognizedWithoutWriting()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_RECOGNITION_CORPUS");
        Assert.SkipUnless(!string.IsNullOrEmpty(path), "Частный корпус распознавания не задан.");
        var paths = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken))!;
        var reader = new SpecificationImportService();
        for (var index = 0; index < paths.Length; index++)
        {
            var before = SHA256.HashData(await File.ReadAllBytesAsync(paths[index], TestContext.Current.CancellationToken));
            var report = await reader.ReadAsync(paths[index], TestContext.Current.CancellationToken);
            Assert.True(report.Report.IsRecognized, $"Документ корпуса {index + 1} не распознан.");
            var after = SHA256.HashData(await File.ReadAllBytesAsync(paths[index], TestContext.Current.CancellationToken));
            Assert.Equal(before, after);
        }
    }

    private sealed class OtherHtmlProfile(string id = "other-html/v1") : ISpecificationProfile
    {
        public string Id => id;
        public string DisplayName => "Другой отчёт";
        public DocumentKind DocumentKind => DocumentKind.Html;
        public int NormalizeCutLengthMeters(decimal millimeters) => 77;
        public SpecificationParseResult Parse(SourceDocument document, CancellationToken cancellationToken = default) =>
            new(Id, null, null, null, null, null, new(1000, []), null, null, null, null, []) { IsRecognized = document.HtmlText == "OTHER-SPEC" };
    }
}
