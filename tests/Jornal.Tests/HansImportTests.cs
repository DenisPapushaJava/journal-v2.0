using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;
using Jornal.Desktop.Import;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Demo;
using Jornal.Parsing.Specifications;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Jornal.Tests;

public sealed class HansImportTests
{
    [AvaloniaFact]
    public async Task RealPdfPipelineUsesContentNotFilenameAndSavesOriginalSegmentsAfterReview()
    {
        using var pdf = new HansPdfFile();
        var bytes = SHA256.HashData(await File.ReadAllBytesAsync(pdf.Path, TestContext.Current.CancellationToken));
        var preview = await new SpecificationImportService().ReadAsync(pdf.Path, TestContext.Current.CancellationToken);
        Assert.True(preview.Report.IsComplete, preview.Diagnostics);
        var store = new DemoOrderStore(); var form = new NewOrderViewModel(store);
        form.ApplyImport(preview);
        Assert.Equal("0514-0078", form.SpecificationNumber.Value); Assert.Equal("14.05.2026", form.OrderDate.Value);
        Assert.Equal("HansLaser", form.CuttingType.Value); Assert.Equal("Сплав_Х77", form.Material.Value);
        Assert.Equal("2", form.MaterialId.Value); Assert.Equal("N2", form.Gas.Value);
        Assert.Contains("Страница 1", form.Gas.ImportEvidence); Assert.Equal("Из спецификации", form.MaterialId.ImportHint);
        Assert.Equal("3", form.SheetCount.Value); Assert.Equal("1500x4000, 1000x3000", form.SheetSize.Value);
        Assert.Equal("33", form.CutLength.Value); Assert.Equal("1.1", form.Coefficient.Value);
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Клиент";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal("1.1|14|31|2|N2", form.CreatedOrder!.StoredCoefficient);
        Assert.Equal(bytes, SHA256.HashData(await File.ReadAllBytesAsync(pdf.Path, TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("99")]
    public async Task MissingOrInvalidIdCannotCalculateUsingDefaultPriceOrSaveUntilExplicitlyFilled(string invalid)
    {
        var report = new HansPdfSpecificationParser().Parse(HansSample.Replace(HansSample.Document(), "Сплав_Х77_2", "Сплав_Х77"), TestContext.Current.CancellationToken);
        var creator = new TrackingCreator(); var form = new NewOrderViewModel(creator);
        form.ApplyImport(SpecificationImportPreview.Create("spec.pdf", report));
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Клиент";
        Assert.Empty(form.MaterialId.Value); Assert.Equal("Сплав_Х77", form.Material.Value);
        form.MaterialId.Value = invalid;
        await form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.Equal(0, creator.PriceReads); Assert.True(form.HasPriceError);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.MaterialId.HasError); Assert.Null(creator.Created);
        form.MaterialId.Value = "2";
        await form.CalculatePriceCommand.ExecuteAsync(null);
        Assert.Equal(PriceTables.HansLaserId2, creator.SelectedTable);
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal("2", creator.Created!.MaterialId);
    }

    [Fact]
    public async Task GasConflictAndCountMismatchLeaveOnlyAffectedInputsBlank()
    {
        var doc = HansSample.Map(HansSample.Document(), word => word.Bottom == 580 && word.Text == "N2" ? word with { Text = "O2" } : word);
        doc = HansSample.Replace(doc, "3", "99");
        var preview = SpecificationImportPreview.Create("spec.pdf", new HansPdfSpecificationParser().Parse(doc, TestContext.Current.CancellationToken));
        var creator = new TrackingCreator(); var form = new NewOrderViewModel(creator); form.ApplyImport(preview);
        Assert.Empty(form.Gas.Value); Assert.Empty(form.SheetCount.Value); Assert.Equal("2", form.MaterialId.Value);
        Assert.Equal("33", form.CutLength.Value); Assert.True(form.HasImportDiagnostics);
        form.Manager.Value = "Менеджер"; form.Customer.Value = "Клиент"; form.SheetCount.Value = "3";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.Gas.HasError); Assert.Null(creator.Created);
        form.Gas.Value = "N2";
        await form.SaveCommand.ExecuteAsync(null);
        Assert.True(form.IsSaved); Assert.Equal("N2", creator.Created!.CuttingGas);
    }

    [AvaloniaFact]
    public async Task BrokenPdfIsAcceptedAsFileButCannotReplaceExistingInputOrWrite()
    {
        using var pdf = new HansPdfFile(); File.WriteAllText(pdf.Path, "broken PDF");
        var store = new DemoOrderStore(); var form = ManualOrderTests.ValidForm(store);
        var original = form.Fields.Select(field => field.Value).ToArray();
        Assert.Null(SpecificationImportService.CheckFiles([pdf.Path]));
        await form.RequestImportAsync(pdf.Path, TestContext.Current.CancellationToken);
        await form.ReplaceImportCommand.ExecuteAsync(null);
        Assert.True(form.HasImportError); Assert.False(form.HasImport);
        Assert.Equal(original, form.Fields.Select(field => field.Value));
        Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task OptionalPrivateCorpusPrefillsAllFieldsAndLeavesStorageUntouched()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_HANS_SPEC_MANIFEST");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(path), "Частный корпус HansLaser не задан.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken));
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            var expected = item.GetProperty("Expected").Deserialize<NormalizedHansSpecification>()!;
            var preview = await new SpecificationImportService().ReadAsync(item.GetProperty("Path").GetString()!, TestContext.Current.CancellationToken);
            var store = new DemoOrderStore(); var form = new NewOrderViewModel(store); form.ApplyImport(preview);
            Assert.Equal(expected.SpecificationNumber, form.SpecificationNumber.Value); Assert.Equal(expected.Material, form.Material.Value);
            Assert.Equal(expected.MaterialId, form.MaterialId.Value); Assert.Equal(expected.CuttingGas, form.Gas.Value);
            Assert.Equal(expected.SheetSize, form.SheetSize.Value); Assert.Equal(expected.SheetCount, form.SheetCount.Value);
            Assert.Equal(expected.WeightKilograms, form.Weight.Value); Assert.Equal(expected.CutLengthMeters.ToString(), form.CutLength.Value);
            Assert.Equal(expected.Coefficient, decimal.Parse(form.Coefficient.Value, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(expected.OrderDate.ToString("dd.MM.yyyy"), form.OrderDate.Value);
            Assert.Equal(3, (await store.ReadAsync(TestContext.Current.CancellationToken)).Count);
        }
    }

    private sealed class TrackingCreator : IManualOrderCreator, IPriceReader
    {
        public int PriceReads { get; private set; }
        public string? SelectedTable { get; private set; }
        public ManualOrderDraft? Created { get; private set; }
        public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
        {
            PriceReads++; SelectedTable = tableName;
            return Task.FromResult(new PriceTable(tableName, true, [new(6, "6", 20)]));
        }
        public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
        { draft.Validate(); Created = draft; return Task.FromResult(draft.ToOrder(4, 726)); }
    }
}

// Только синтетический PDF и уже поставляемый шрифт; никаких реальных спецификаций в репозитории.
internal sealed class HansPdfFile : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "journal-hans-" + Guid.NewGuid().ToString("N"));
    public string Path { get; }
    public HansPdfFile()
    {
        Directory.CreateDirectory(_directory);
        Path = System.IO.Path.Combine(_directory, "99 mm 1999-01-01-9999 неверное имя.pdf");
        var builder = new PdfDocumentBuilder();
        using var stream = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf"));
        using var font = new MemoryStream(); stream.CopyTo(font);
        var added = builder.AddTrueTypeFont(font.ToArray());
        foreach (var source in HansSample.Document().Pages)
        {
            var page = builder.AddPage(source.Width, source.Height);
            foreach (var word in source.Words)
                page.AddText(word.Text, 6, new PdfPoint(word.Left, word.Bottom), added);
        }
        File.WriteAllBytes(Path, builder.Build());
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
