using System.Security.Cryptography;
using System.Text.Json;
using Jornal.Core.Pricing;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class WaterjetPdfParserTests
{
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1.5, 30, 80)]
    [InlineData(0.65, 10, 15)]
    public void UsesInternalFilenameAndLabelledSummaryRegardlessOfObjectOrderAndScale(double scale, double dx, double dy)
    {
        var source = Document();
        var doc = source with { Pages = source.Pages.Select(page => page with { Width = page.Width * scale + dx, Height = page.Height * scale + dy,
            Words = page.Words.Reverse().Select(word => word with { Left = word.Left * scale + dx, Right = word.Right * scale + dx,
                Bottom = word.Bottom * scale + dy, Top = word.Top * scale + dy }).ToArray() }).ToArray() };
        var report = SpecificationProfileCatalog.Default.Parse(doc, TestContext.Current.CancellationToken);
        Assert.Equal(WaterjetPdfSpecificationParser.Profile, report.Profile); Assert.True(report.IsComplete, string.Join("; ", report.Issues.Select(issue => issue.Message)));
        var preview = SpecificationImportPreview.Create("2030-01-01-9999.pdf", report);
        Assert.Equal("08.10.2026", preview.Fields["OrderDate"].Value); Assert.Equal("1008-1700", preview.Fields["SpecificationNumber"].Value);
        Assert.Equal("1500x1300", preview.Fields["SheetSize"].Value); Assert.Equal("1", preview.Fields["SheetCount"].Value);
        Assert.Equal("162", preview.Fields["CutLength"].Value); Assert.Equal("10:41", preview.Fields["CuttingTime"].Value);
        Assert.Equal("1", preview.Fields["Coefficient"].Value); Assert.Equal("Сталь 20/09Г2С", preview.Fields["Material"].Value);
        Assert.Contains(report.Issues, issue => issue.Code == "MaterialNeedsReview");
    }

    [Fact]
    public void UnknownPdfCannotBecomeAWaterjetOrderAndInvalidHeaderDoesNotUsePrintDate()
    {
        var source = Document(); var page = source.Pages[0];
        var invalid = source with { Pages = [page with { Words = page.Words.Select(word => word.Text.Contains("2026-10-08-1700")
            ? word with { Text = word.Text.Replace("2026-10-08-1700", "2026-02-30-1700", StringComparison.Ordinal) } : word).ToArray() }] };
        var report = new WaterjetPdfSpecificationParser().Parse(invalid, TestContext.Current.CancellationToken);
        Assert.True(report.IsRecognized); Assert.Null(report.Header);
        var unrelated = source with { Pages = [page with { Words = page.Words.Where(word => word.Bottom > 500).ToArray() }] };
        Assert.False(new WaterjetPdfSpecificationParser().Parse(unrelated, TestContext.Current.CancellationToken).IsRecognized);
        Assert.Throws<DocumentRejectedException>(() => SpecificationProfileCatalog.Default.Parse(unrelated, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("6.00:19:41", "144:20")]
    [InlineData("3.11:37:29", "83:38")]
    [InlineData("168:00:01", "168:01")]
    public void MultiSheetTotalsAreNotMultipliedAndCanvasIsNotThePhysicalSheet(string duration, string expected)
    {
        var source = Document(); var page = source.Pages[0];
        var words = page.Words.Select(word => word.Text switch
        {
            "10:40:57" => word with { Text = duration },
            "1500x1300" => word with { Text = "6090x2000" },
            "Раскрой:1500х1300mm" => word with { Text = "Раскрой:1500х2000mm" },
            "1" when word.Bottom == 318 => word with { Text = "4" },
            _ => word
        }).ToArray();
        var report = new WaterjetPdfSpecificationParser().Parse(source with { Pages = [page with { Words = words }] }, TestContext.Current.CancellationToken);
        Assert.True(report.IsComplete);
        var preview = SpecificationImportPreview.Create("ignored.pdf", report);
        Assert.Equal("1500x2000", preview.Fields["SheetSize"].Value);
        Assert.Equal("4", preview.Fields["SheetCount"].Value);
        Assert.Equal("75", preview.Fields["Weight"].Value);
        Assert.Equal("162", preview.Fields["CutLength"].Value);
        Assert.Equal(expected, preview.Fields["CuttingTime"].Value);
    }

    [Fact]
    public void MissingLabelProducesPartialImportAndMaterialNotesAreBoundToTheirCell()
    {
        var source = Document(); var page = source.Pages[0];
        var words = page.Words.Where(word => word.Text is not ("Сталь" or "20/09Г2С" or "Толщина")).ToArray();
        var note = new PdfTextAnnotation("5083H111", 400, 672, 450, 680);
        var doc = source with { Pages = [page with { Words = words, TextAnnotations = [note,
            new("Неверная марка", 400, 100, 450, 108), new("Сломанная пометка", 450, 672, 400, 680)] }] };
        var parser = new WaterjetPdfSpecificationParser(); var report = parser.Parse(doc, TestContext.Current.CancellationToken);
        Assert.True(report.IsRecognized); Assert.False(report.IsComplete); Assert.Null(report.Dimensions);
        Assert.Equal("5083H111", report.Material!.Value);
        Assert.Contains(report.Issues, issue => issue.Code == "MaterialNeedsReview");
        var conflict = parser.Parse(doc with { Pages = [doc.Pages[0] with { TextAnnotations = [note, note with { Text = "АМг3" }] }] }, TestContext.Current.CancellationToken);
        Assert.Contains(conflict.Issues, issue => issue.Code == "ConflictingMaterialNotes");
        Assert.Empty(SpecificationImportPreview.Create("ignored.pdf", conflict).Fields["Material"].Value);
    }

    [Fact]
    public async Task CandidateMaterialMustBeReviewedAndAmbiguousMachineIsNeverChosenAutomatically()
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(machine), TestContext.Current.CancellationToken);
        var form = new NewOrderViewModel(store); await form.LoadMachinesAsync(TestContext.Current.CancellationToken);
        form.ApplyImport(SpecificationImportPreview.Create("anything.pdf", new WaterjetPdfSpecificationParser().Parse(Document(), TestContext.Current.CancellationToken)));
        Assert.Equal(machine.Name, form.CuttingType.Value); Assert.Equal("Сталь 20/09Г2С", form.Material.Value);
        Assert.True(form.Material.NeedsReview); Assert.False(form.SaveCommand.CanExecute(null));
        form.Material.ConfirmReviewCommand.Execute(null); Assert.False(form.Material.NeedsReview); Assert.True(form.SaveCommand.CanExecute(null));
        form.ApplyImport(SpecificationImportPreview.Create("replacement.pdf", new WaterjetPdfSpecificationParser().Parse(Document(), TestContext.Current.CancellationToken)));
        Assert.True(form.Material.NeedsReview); form.Material.Value = "09Г2С"; Assert.False(form.Material.NeedsReview);
        var second = MachineCatalogTests.Machine("Второй гидроабразив");
        await store.Prices.CreateMachineAsync(MachineCatalogTests.Creation(second, machine), TestContext.Current.CancellationToken);
        var other = new NewOrderViewModel(store); await other.LoadMachinesAsync(TestContext.Current.CancellationToken);
        other.ApplyImport(SpecificationImportPreview.Create("ambiguous.pdf", new WaterjetPdfSpecificationParser().Parse(Document(), TestContext.Current.CancellationToken)));
        Assert.Empty(other.CuttingType.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddingSecondFilePreservesFirstMaterialReviewAndRequiresReviewOfTheNewPosition(bool confirmed)
    {
        var store = new DemoOrderStore(); var machine = MachineCatalogTests.Machine();
        var creation = MachineCatalogTests.Creation(machine);
        await store.Prices.CreateMachineAsync(creation with { Rates = [.. creation.Rates, new(5m, "5", 170)] }, TestContext.Current.CancellationToken);
        var preview = SpecificationImportPreview.Create("Первый.pdf", new WaterjetPdfSpecificationParser().Parse(Document(), TestContext.Current.CancellationToken));
        var form = new NewOrderViewModel(store) { ImportReader = new PreviewReader(preview) };
        await form.LoadMachinesAsync(TestContext.Current.CancellationToken); form.ApplyImport(preview);
        if (confirmed) form.Material.ConfirmReviewCommand.Execute(null);
        await form.AddBatchFilesAsync(["Второй.pdf"], TestContext.Current.CancellationToken);
        Assert.True(form.IsBatchMode); Assert.Equal(2, form.BatchItems.Count);
        Assert.Equal(!confirmed, form.BatchItems[0].Form.Material.NeedsReview);
        Assert.True(form.BatchItems[1].Form.Material.NeedsReview); Assert.False(form.SaveCommand.CanExecute(null));
        foreach (var item in form.BatchItems) item.Form.Material.ConfirmReviewCommand.Execute(null);
        await form.WaitForThicknessOptionsAsync(TestContext.Current.CancellationToken);
        Assert.True(form.BatchItems.All(item => item.IsReady)); Assert.True(form.SaveCommand.CanExecute(null));
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
    }
    private sealed class PreviewReader(SpecificationImportPreview preview) : ISpecificationImportService
    {
        public Task<SpecificationImportPreview> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(preview with { FileName = path });
    }

    [Fact]
    public async Task OptionalPrivateCorpusMatchesIndependentPopplerObservationsWithoutChangingSources()
    {
        var manifest = Environment.GetEnvironmentVariable("JOURNAL_WATERJET_SPEC_MANIFEST");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(manifest), "Частный корпус AWJC не задан.");
        using var data = JsonDocument.Parse(await File.ReadAllTextAsync(manifest!, TestContext.Current.CancellationToken));
        foreach (var item in data.RootElement.EnumerateArray())
        {
            var file = item.GetProperty("Path").GetString()!;
            var before = SHA256.HashData(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
            Assert.Equal(item.GetProperty("Sha256").GetString(), Convert.ToHexStringLower(before));
            var report = SpecificationProfileCatalog.Default.Parse(await new DocumentReader().ReadAsync(file, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
            Assert.True(report.IsComplete, Path.GetFileName(file) + ": " + string.Join("; ", report.Issues.Select(issue => issue.Code + " " + issue.Message)));
            var preview = SpecificationImportPreview.Create("ignored.pdf", report);
            foreach (var expected in item.GetProperty("Fields").EnumerateObject()) Assert.Equal(expected.Value.GetString(), preview.Fields[expected.Name].Value);
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken)));
        }
    }

    internal static SourceDocument Document()
    {
        var words = new List<PdfWord>();
        void Text(string text, double x, double y)
        {
            foreach (var word in text.Split(' ')) { var width = word.Length * 4.0; words.Add(new(word, x, y, x + width, y + 8)); x += width + 4; }
        }
        Text("Дата", 50, 720); Text("2030-12-31 15:00", 160, 720);
        Text("Имя файла", 50, 690); Text("5 мм 2026-10-08-1700--AWJC.dig", 160, 690);
        Text("Материал", 50, 672); Text("Mild Steel/Standard 5 mm Сталь 20/09Г2С", 160, 672);
        Text("Резка", 50, 400); Text("Материал", 330, 400);
        Text("Время резки", 50, 370); Text("10:40:57 (10,683)", 180, 370); Text("Общий раскрой", 330, 370); Text("1500x1300 mm", 460, 370);
        Text("Длинна реза", 50, 350); Text("161253 mm", 180, 350); Text("Толщина", 330, 350); Text("5,0", 460, 350);
        Text("Раскрой:1500х1300mm", 50, 330); Text("Количество", 190, 330); Text("листов: 1", 190, 318);
        Text("Общий вес", 330, 330); Text("75", 460, 330);
        var misleading = new PdfPage(2, 600, 800, [new("Материал 99 мм 2099-01-01-9999--AWJC.dig", 50, 700, 400, 708)]);
        return new(DocumentKind.Pdf, null, [], [new(1, 600, 800, words), misleading]);
    }
}
