using System.IO.Compression;
using System.Xml.Linq;
using Jornal.Core.Pricing;
using Jornal.Desktop.ViewModels;
using Jornal.Infrastructure.Export;
using Jornal.Infrastructure.Local;
using Xunit;

namespace Jornal.Tests;

public sealed class PriceExportTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public async Task EveryUsedMachineIsSelectedByDefaultAndTheSharedTableIsReadOnlyOnce()
    {
        var reader = new Reader(); var custom = MachineCatalogTests.Machine("Гидроабразивная");
        var groups = PriceCatalog.Groups.Concat([new PriceGroup(custom.Name, [new(custom.TableName, "Основной", custom.Name)])]).ToArray();
        var model = new PriceExportViewModel(reader, groups);
        Assert.Equal(new[] { "Лазер", "Плазма", "Газо-кислородная", "HansLaser", "Гидроабразивная" }, model.Items.Select(item => item.Name));
        Assert.All(model.Items, item => Assert.True(item.IsSelected));
        var workbook = await new PriceExportReader(reader).ReadAsync(model.Items.Select(item => item.Group).ToArray(), Token);
        Assert.Equal(5, workbook.Sheets.Count); Assert.Equal(6, reader.Calls.Count);
        Assert.DoesNotContain("PriceLM500", reader.Calls); Assert.DoesNotContain("PriceHL", reader.Calls);
        Assert.Equal(1, reader.Calls.Count(name => name == PriceTables.Laser));
        Assert.Same(workbook.Sheets[0].Profiles[0].Table, workbook.Sheets[3].Profiles[0].Table);
        Assert.Equal(new[] { "Базовый", "Нержавеющая сталь", "Цветные металлы" }, workbook.Sheets[3].Profiles.Select(profile => profile.Name));
        await Assert.ThrowsAsync<ArgumentException>(() => new PriceExportReader(reader).ReadAsync([PriceCatalog.Groups[^1]], Token));
        if (Environment.GetEnvironmentVariable("JOURNAL_PRICE_EXPORT_PROBE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await PriceWorkbookExporter.SaveAsync(Path.Combine(directory, "Прайсы.xlsx"), workbook, Token);
        }
    }

    [Fact]
    public void AUsedReadOnlyProfileIsStillExportable()
    {
        var model = new PriceExportViewModel(new Reader(), [new("Лазер", [new(PriceTables.Laser, "Основной", "Лазер", CanEdit: false)])]);
        Assert.Single(model.Items); Assert.True(model.CanExport);
    }

    [Fact]
    public async Task TheWorkbookHasNumericSortedThicknessesPricesAndBlankMissingTariffs()
    {
        var source = await new PriceExportReader(new Reader()).ReadAsync([PriceCatalog.Groups[3]], Token);
        using var buffer = new MemoryStream(); PriceWorkbookExporter.Write(buffer, source, Token); buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var sheet = Read(zip, "xl/worksheets/sheet1.xml");
        var rows = sheet.Descendants(Sheet + "row").Where(row => (int)row.Attribute("r")! >= 5).ToArray();
        Assert.Equal(new[] { "2.5", "6", "10" }, rows.Select(row => row.Elements(Sheet + "c").First().Element(Sheet + "v")!.Value));
        Assert.Equal("0", sheet.Descendants(Sheet + "c").Single(cell => (string?)cell.Attribute("r") == "B7").Element(Sheet + "v")!.Value);
        Assert.Null(sheet.Descendants(Sheet + "c").Single(cell => (string?)cell.Attribute("r") == "B6").Element(Sheet + "v"));
        Assert.All(rows.SelectMany(row => row.Elements(Sheet + "c")), cell => Assert.Equal("n", (string?)cell.Attribute("t")));
        Assert.Equal("frozen", (string?)sheet.Descendants(Sheet + "pane").Single().Attribute("state"));
        Assert.Equal("A4:D7", (string?)sheet.Element(Sheet + "autoFilter")!.Attribute("ref"));
        Assert.DoesNotContain(sheet.Descendants(Sheet + "t"), text => text.Value.Contains("ID "));
        Assert.Empty(sheet.Descendants(Sheet + "f"));
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("external", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("vba", StringComparison.OrdinalIgnoreCase));
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        Assert.All(Read(zip, "xl/_rels/workbook.xml.rels").Elements(relationships + "Relationship"), link =>
            Assert.StartsWith("http://schemas.openxmlformats.org/officeDocument/2006/relationships/", (string?)link.Attribute("Type")));
    }

    [Fact]
    public void SheetNamesAreUniqueAndSafeAndTextCannotBecomeAFormula()
    {
        var titles = new[] { "History", "Лазер/1", "Лазер:1", new string('я', 30) + "'название", new string('я', 30) + "😀станок", "'=SUM(A1:A2)'", "\u0001Станок", "''" };
        var table = new PriceTable(PriceTables.Laser, false, [new(3m, "3", null)]);
        var book = new PriceExportWorkbook(DateTimeOffset.Now, titles.Select(title => new PriceExportSheet(title, [new("Основной", table)])).ToArray());
        using var buffer = new MemoryStream(); PriceWorkbookExporter.Write(buffer, book, Token); buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var names = Read(zip, "xl/workbook.xml").Descendants(Sheet + "sheet").Select(sheet => (string)sheet.Attribute("name")!).ToArray();
        Assert.Equal(titles.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, name =>
        {
            Assert.InRange(name.Length, 1, 31); Assert.DoesNotContain(name, character => "[]:*?/\\".Contains(character) || char.IsControl(character));
            Assert.False(name.StartsWith('\'') || name.EndsWith('\'')); Assert.NotEqual("History", name);
        });
        var formulaTitle = Read(zip, "xl/worksheets/sheet6.xml");
        Assert.Equal(titles[5], formulaTitle.Descendants(Sheet + "c").Single(cell => (string?)cell.Attribute("r") == "A1").Value);
        Assert.Empty(formulaTitle.Descendants(Sheet + "f"));
        Assert.Null(Read(zip, "xl/worksheets/sheet1.xml").Descendants(Sheet + "c").Single(cell => (string?)cell.Attribute("r") == "B5").Element(Sheet + "v"));
    }

    [Fact]
    public async Task CancelledPickerOrEmptySelectionDoesNotReadPricesOrWriteFiles()
    {
        var reader = new Reader(); var model = new PriceExportViewModel(reader, PriceCatalog.Groups);
        Assert.False(await model.ExportAsync(_ => Task.FromResult<string?>(null), Token));
        Assert.Empty(reader.Calls); Assert.Null(model.Error); Assert.False(model.IsExporting);
        foreach (var item in model.Items) item.IsSelected = false;
        Assert.False(model.CanExport);
        Assert.False(await model.ExportAsync(_ => throw new InvalidOperationException("Диалог не должен открыться"), Token));
        Assert.Empty(reader.Calls);
    }

    [Fact]
    public async Task SelectedPricesAreExportedWithoutChangingTheSourceOrIncludingOtherMachines()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        var model = new PriceExportViewModel(new LocalJournalStore(fixture.Path), PriceCatalog.Groups);
        foreach (var item in model.Items) item.IsSelected = item.Name == "Плазма";
        var output = Path.Combine(fixture.Directory, "Цены.xlsx");
        Assert.True(await model.ExportAsync(_ => Task.FromResult<string?>(output), Token), model.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
        using var zip = ZipFile.OpenRead(output);
        Assert.Equal("Плазма", (string?)Assert.Single(Read(zip, "xl/workbook.xml").Descendants(Sheet + "sheet")).Attribute("name"));
    }

    [Fact]
    public async Task ReadFailurePreservesThePreviousFileAndNamesTheProblemInsteadOfExportingAPartialBook()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var reader = new Reader { Failure = PriceTables.Plasma }; var model = new PriceExportViewModel(reader, PriceCatalog.Groups);
        var output = Path.Combine(fixture.Directory, "Цены.xlsx"); await File.WriteAllTextAsync(output, "Предыдущий файл", Token);
        Assert.False(await model.ExportAsync(_ => Task.FromResult<string?>(output), Token));
        Assert.Contains("Плазма", model.Error); Assert.DoesNotContain("СЕКРЕТ", model.Error);
        Assert.Equal("Предыдущий файл", await File.ReadAllTextAsync(output, Token));
        Assert.Empty(Directory.GetFiles(fixture.Directory, ".journal-prices-*.tmp")); Assert.True(model.CanExport);
    }

    [Fact]
    public async Task OtherExtensionsCannotOverwriteTheDatabase()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var reader = new Reader(); var model = new PriceExportViewModel(reader, PriceCatalog.Groups);
        var before = await File.ReadAllBytesAsync(fixture.Path, Token);
        Assert.False(await model.ExportAsync(_ => Task.FromResult<string?>(fixture.Path), Token));
        Assert.Contains(".xlsx", model.Error); Assert.Empty(reader.Calls);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path, Token));
    }

    [Fact]
    public async Task CancellingTheReadKeepsThePreviousFileAndBlocksRepeatedExportUntilFinished()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader { Pending = entered }; var model = new PriceExportViewModel(reader, PriceCatalog.Groups);
        var output = Path.Combine(fixture.Directory, "Цены.xlsx"); await File.WriteAllTextAsync(output, "Предыдущий файл", Token);
        var exporting = model.ExportAsync(_ => Task.FromResult<string?>(output), Token);
        await entered.Task.WaitAsync(Token); Assert.False(model.CanExport); Assert.True(model.IsExporting);
        Assert.False(await model.ExportAsync(_ => throw new InvalidOperationException("Повтор"), Token));
        model.CancelExport(); Assert.False(await exporting); Assert.True(model.CanExport);
        Assert.Equal("Предыдущий файл", await File.ReadAllTextAsync(output, Token));
    }

    [Fact]
    public async Task FailedWorkbookCreationCleansTheTemporaryFileAndKeepsTheDestination()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var output = Path.Combine(fixture.Directory, "Цены.xlsx"); await File.WriteAllTextAsync(output, "Предыдущий файл", Token);
        var invalid = new PriceExportWorkbook(DateTimeOffset.Now, []);
        await Assert.ThrowsAsync<ArgumentException>(() => PriceWorkbookExporter.SaveAsync(output, invalid, Token));
        Assert.Equal("Предыдущий файл", await File.ReadAllTextAsync(output, Token));
        Assert.Empty(Directory.GetFiles(fixture.Directory, ".journal-prices-*.tmp"));
    }

    [Fact]
    public async Task AnUnavailableDestinationShowsAnErrorAndAllowsRetryWithoutLeavingTemporaryFiles()
    {
        using var fixture = new LocalJournalFixture(); await fixture.InitializeAsync();
        var model = new PriceExportViewModel(new Reader(), PriceCatalog.Groups);
        var blocked = Path.Combine(fixture.Directory, "Занято.xlsx"); Directory.CreateDirectory(blocked);
        Assert.False(await model.ExportAsync(_ => Task.FromResult<string?>(blocked), Token));
        Assert.Contains("Не удалось сохранить Excel", model.Error); Assert.True(model.CanExport);
        Assert.Empty(Directory.GetFiles(fixture.Directory, ".journal-prices-*.tmp"));
        var output = Path.Combine(fixture.Directory, "Цены.xlsx");
        Assert.True(await model.ExportAsync(_ => Task.FromResult<string?>(output), Token)); Assert.Null(model.Error);
        Assert.True(File.Exists(output));
    }

    private static XElement Read(ZipArchive zip, string path)
    { using var stream = zip.GetEntry(path)!.Open(); return XElement.Load(stream); }

    internal sealed class Reader : IPriceReader
    {
        public List<string> Calls { get; } = [];
        public string? Failure { get; set; }
        public TaskCompletionSource? Pending { get; set; }
        public async Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
        {
            Calls.Add(tableName);
            if (tableName == Failure) throw new IOException("СЕКРЕТ путь к источнику");
            if (Pending is not null) { Pending.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); }
            return new(tableName, true, [new(10m, "10", 0), new(2.5m, "2,5", tableName == PriceTables.HansLaserId2 ? 40 : 17), new(6m, "6", null)]);
        }
    }
}
