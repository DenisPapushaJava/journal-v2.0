using System.Globalization;
using System.Text;
using Jornal.Core.Pricing;
using Jornal.Parsing.Documents;
using Jornal.Parsing.Specifications;
using Xunit;

namespace Jornal.Tests;

public sealed class BlockHtmlParserTests
{
    [Fact]
    public async Task OptionalPrivateCorpusMatchesIndependentlyRecordedExpectedValuesWithoutChangingSources()
    {
        var path = Environment.GetEnvironmentVariable("JOURNAL_BLOCK_SPEC_MANIFEST");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(path), "Частный корпус HTML не задан.");
        var cases = System.Text.Json.JsonSerializer.Deserialize<CorpusCase[]>(await File.ReadAllTextAsync(path!, TestContext.Current.CancellationToken))!;
        Assert.NotEmpty(cases);
        foreach (var item in cases)
        {
            var before = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(item.Path, TestContext.Current.CancellationToken));
            var document = await new DocumentReader().ReadAsync(item.Path, TestContext.Current.CancellationToken);
            var parsed = new BlockHtmlSpecificationParser().Parse(document, TestContext.Current.CancellationToken);
            if (item.Expected is not null)
            {
                Assert.True(parsed.IsComplete, item.Id + ": " + Messages(parsed));
                Assert.Equal(item.Expected, BlockSpecificationNormalizer.Normalize(parsed));
            }
            else
            {
                Assert.False(parsed.IsComplete);
                Assert.Equal(item.ExpectedErrors, parsed.Issues.Where(issue => issue.Severity == SpecificationIssueSeverity.Error).Select(issue => issue.Code));
            }
            if (item.Layouts is not null) Assert.Equal(item.Layouts, parsed.SheetLayouts.Select(layout => layout.Value));
            if (item.Raw is { } raw)
            {
                Assert.Equal(raw.OrderDate, parsed.Header!.Value.OrderDate); Assert.Equal(raw.Number, parsed.Header.Value.JournalNumber);
                Assert.Equal(raw.CuttingType, parsed.CuttingType!.Value); Assert.Equal(raw.Material, parsed.Material!.Value);
                Assert.Equal(raw.SheetCount, parsed.SheetCount!.Value); Assert.Equal(raw.CutLengthMillimeters, parsed.CutLengthMillimeters!.Value);
                Assert.Equal(raw.WeightKilograms, parsed.WeightKilograms!.Value); Assert.Equal(raw.PartCount, parsed.PartCount!.Value);
                Assert.Equal(raw.PunchCount, parsed.PunchCount!.Value); Assert.Equal(raw.DurationSeconds, parsed.CuttingDuration is null ? null : (long?)parsed.CuttingDuration.Value.TotalSeconds);
            }
            Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(item.Path, TestContext.Current.CancellationToken)));
        }
    }
    private sealed record CorpusCase(string Id, string Path, NormalizedBlockSpecification? Expected, string[]? ExpectedErrors = null,
        SheetLayout[]? Layouts = null, RawExpectation? Raw = null);
    private sealed record RawExpectation(DateOnly OrderDate, string Number, string CuttingType, string Material,
        int SheetCount, decimal CutLengthMillimeters, decimal WeightKilograms, int PartCount, int PunchCount, long? DurationSeconds);

    [Fact]
    public async Task UsesHeaderSheetAndOrderTotalsWithEvidenceAndLegacyNormalization()
    {
        var parsed = await Parse(Html());
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Empty(parsed.Issues);
        Assert.Equal(new DateOnly(2026, 5, 14), parsed.Header!.Value.OrderDate);
        Assert.Equal("2026-05-14-0078", parsed.Header.Value.OriginalIdentifier);
        Assert.Equal("0514-0078", parsed.Header.Value.JournalNumber);
        Assert.Equal("Лазер", parsed.CuttingType!.Value); Assert.Equal("Сплав_X77", parsed.Material!.Value);
        Assert.Equal(new(6, 4000, 1500), parsed.Dimensions!.Value);
        Assert.Equal(3, parsed.SheetCount!.Value); Assert.Equal(14, parsed.PartCount!.Value); Assert.Equal(31, parsed.PunchCount!.Value);
        Assert.Equal(32500.5m, parsed.CutLengthMillimeters!.Value); Assert.Equal(48.2m, parsed.WeightKilograms!.Value);
        Assert.Equal(TimeSpan.FromHours(36) + TimeSpan.FromMinutes(47), parsed.CuttingDuration!.Value);
        var source = Assert.Single(parsed.CutLengthMillimeters.Sources);
        Assert.Equal("Длина реза:(мм)", source.Label); Assert.Equal("32500.5", source.RawText);
        Assert.Equal(3, source.Table); Assert.Equal(1, source.Row); Assert.Equal(1, source.Cell);
        var normalized = BlockSpecificationNormalizer.Normalize(parsed);
        Assert.Equal("1500x4000", normalized.SheetSize); Assert.Equal(33, normalized.CutLengthMeters); Assert.Equal(1.1m, normalized.Coefficient);
        Assert.Equal("48.2", normalized.WeightKilograms); Assert.Equal("3", normalized.SheetCount);
    }

    [Theory]
    [InlineData("Материал: Подмена 1 mm")]
    [InlineData("Одна деталь")]
    [InlineData("Все детали")]
    [InlineData("Всего по заказу:")]
    [InlineData("Спецификация № 1999-01-01-9999--ПЛАЗМА.rcam")]
    [InlineData("Вес:(кг) 999999 Габариты: 999x999x999 Листов: 888 Длина реза: 999")]
    [InlineData("Одна очень длинная деталь .rcam Спецификация № Пробивки: Материал: Вес: Общие кол-во деталей:")]
    public async Task WordsAndSymbolsInPartNamesCannotReplaceAnyOrderValue(string partName)
    {
        var baseline = BlockSpecificationNormalizer.Normalize(await Parse(Html()));
        foreach (var name in new[] { partName, partName + new string('x', 12000) })
        {
            var parsed = await Parse(Html(partName: name));
            Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(baseline, BlockSpecificationNormalizer.Normalize(parsed));
        }
    }

    [Theory]
    [InlineData("6", "x", "--", "rcam")]
    [InlineData("6.0", "×", "-", "cry")]
    [InlineData("6,0", "х", "--", "whatever")]
    public async Task AcceptsIntegerAndFractionalThicknessBothHeaderSeparatorsAndDifferentExtensions(string thickness, string separator, string dash, string extension)
    {
        var html = Html().Replace("6.0x4000x1500", $"{thickness}{separator}4000{separator}1500")
            .Replace("2026-05-14-0078", "2026-05-14" + dash + "0078").Replace(".rcam", "." + extension);
        var parsed = await Parse(html);
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(6, parsed.Dimensions!.Value.ThicknessMillimeters);
        Assert.Equal("0514-0078", parsed.Header!.Value.JournalNumber);
        Assert.Equal("2026-05-14" + dash + "0078", parsed.Header.Value.OriginalIdentifier);
    }

    [Theory]
    [InlineData("ЛАЗЕР", "Лазер", 6)]
    [InlineData("ПЛАЗМА", "Плазма", 6)]
    [InlineData("ГАЗО-КИСЛОРОДНАЯ", "Газо-кислородная", 20)]
    public async Task ReadsTheMachineTagAfterTheIdentifierNotAnotherWordInTheName(string tag, string expected, int thickness)
    {
        var html = Html().Replace("6.0", thickness + ".0").Replace("--ЛАЗЕР.rcam", "--" + tag + "--для ПЛАЗМА и ЛАЗЕР.rcam");
        var parsed = await Parse(html);
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(expected, parsed.CuttingType!.Value);
    }

    [Fact]
    public async Task ReordersTablesAndFieldPairsAndDoesNotUseTheExternalFileNameOrReportPrintDate()
    {
        var html = Html(); var blocks = html.Split("<!--table-->");
        var reordered = blocks[0] + blocks[4] + blocks[3] + blocks[1] + blocks[2] + blocks[5];
        var expected = BlockSpecificationNormalizer.Normalize(await Parse(html));
        var actual = await Parse(reordered, "Вес 999 1999-01-01-9999 ПЛАЗМА.htm");
        Assert.True(actual.IsComplete, Messages(actual)); Assert.Equal(expected, BlockSpecificationNormalizer.Normalize(actual));
        Assert.Equal(new DateOnly(2026, 5, 14), actual.Header!.Value.OrderDate);
    }

    [Theory]
    [InlineData("Амг3")]
    [InlineData("5083H111")]
    [InlineData("Новая_марка-777")]
    [InlineData("Cт3")]
    public async Task KeepsArbitraryMaterialSpellingWithoutTransliteration(string material)
    {
        var parsed = await Parse(Html().Replace("Сплав_X77", material));
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(material, parsed.Material!.Value);
    }

    [Fact]
    public async Task ALabelWordMayBeTheMaterialNameWhenItOccupiesTheMaterialValueCell()
    {
        foreach (var material in new[] { "Материал", "Вес", "Габариты", "Пробивки", "123" })
        {
            var parsed = await Parse(Html().Replace("Сплав_X77 6.0 mm", material));
            Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(material, parsed.Material!.Value);
        }
    }

    [Theory]
    [InlineData("29:46", 107160)]
    [InlineData("168:00", 604800)]
    [InlineData("200:01:02", 720062)]
    public async Task CuttingTimeIsAnUnboundedHourDuration(string text, int seconds)
    {
        var parsed = await Parse(Html().Replace("36:47", text));
        Assert.Equal(TimeSpan.FromSeconds(seconds), parsed.CuttingDuration!.Value);
    }

    [Theory]
    [InlineData("36:60")]
    [InlineData("1:00:99")]
    [InlineData("999999999999999999999:00")]
    public async Task InvalidOptionalDurationIsVisibleWithoutInventingAValueOrBlockingKnownMdbFields(string text)
    {
        var parsed = await Parse(Html().Replace("36:47", text));
        Assert.Null(parsed.CuttingDuration); Assert.True(parsed.IsComplete);
        Assert.Contains(parsed.Issues, issue => issue.Field == SpecificationField.CuttingDuration && issue.Severity == SpecificationIssueSeverity.Warning);
    }

    [Fact]
    public async Task ConflictingTotalsOrMaterialsKeepAllEvidenceAndPreventAutomaticNormalization()
    {
        foreach (var html in new[] {
            Html().Replace("</tbody><!--summary-end-->", "<tr><td>Вес:(кг)</td><td>999</td></tr></tbody><!--summary-end-->"),
            Html().Replace("<!--detail-end-->", "<tr><td>Иная деталь</td><td>Материал:</td><td>Другой_сплав 6 mm</td><td>Одна деталь</td><td>Все детали</td></tr><!--detail-end-->") })
        {
            var parsed = await Parse(html);
            Assert.False(parsed.IsComplete);
            var issue = Assert.Single(parsed.Issues, issue => issue.Code == "ConflictingValues");
            Assert.Equal(2, issue.Sources!.Count);
            Assert.Throws<ArgumentException>(() => BlockSpecificationNormalizer.Normalize(parsed));
        }
    }

    [Theory]
    [InlineData("<td>Пробивки:</td><td>31</td>", "", SpecificationField.PunchCount, "MissingField")]
    [InlineData("<td>Вес:(кг)</td><td>48.2</td>", "<td>Вес:(кг)</td><td>неизвестно</td>", SpecificationField.Weight, "InvalidValue")]
    [InlineData("<td>Листов:</td><td>3</td>", "<td>Листов:</td><td>1.5</td>", SpecificationField.SheetCount, "InvalidValue")]
    [InlineData("<td>Общие кол-во деталей:</td><td>14</td>", "<td>Общие кол-во деталей:</td><td>0</td>", SpecificationField.PartCount, "InvalidValue")]
    [InlineData("--ЛАЗЕР.rcam", "--НЕИЗВЕСТНЫЙ.rcam", SpecificationField.CuttingType, "InvalidValue")]
    [InlineData("2026-05-14", "2026-02-30", SpecificationField.Header, "InvalidValue")]
    [InlineData("Сплав_X77 6.0 mm", "Сплав_X77 8.0 mm", SpecificationField.Material, "MaterialThicknessMismatch")]
    public async Task MissingInvalidAndContradictoryValuesAreNotReplacedByDefaults(string before, string after, SpecificationField field, string code)
    {
        var parsed = await Parse(Html().Replace(before, after));
        Assert.False(parsed.IsComplete); Assert.Contains(parsed.Issues, issue => issue.Field == field && issue.Code == code);
        Assert.Throws<ArgumentException>(() => BlockSpecificationNormalizer.Normalize(parsed));
    }

    [Fact]
    public async Task InlineCellsDecimalsGroupingAndRepeatedEqualTotalsAreSupported()
    {
        var html = Html().Replace("<td>Габариты:</td><td>6.0x4000x1500</td>", "<td colspan='2'>Габариты: 6 × 4000 × 1500</td>")
            .Replace("<td>Длина реза:(мм)</td><td>32500.5</td>", "<td>Длина реза:(мм) 32&nbsp;500,5</td>")
            .Replace("<td>Время:(ч:мм)</td><td>36:47</td>", "<td>Время:(ч:мм) 36:47</td>")
            .Replace("</tbody><!--summary-end-->", "<tr><td>Вес:(кг)</td><td>48,20</td></tr></tbody><!--summary-end-->");
        var parsed = await Parse(html);
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(32500.5m, parsed.CutLengthMillimeters!.Value);
        Assert.Equal(2, parsed.WeightKilograms!.Sources.Count);
    }

    [Fact]
    public async Task NestedLayoutTablesLineBreaksAndSpacingDoNotMergeLabelsIntoValues()
    {
        var html = Html().Replace("<body>", "<body><table><tr><td>").Replace("</body>", "</td></tr></table></body>")
            .Replace("Спецификация № 6 мм", "Спецификация <b>№</b><br>6 мм")
            .Replace("<td>Габариты:</td><td>6.0x4000x1500</td>", "<td><p>Габариты :</p><p>6.0x4000x1500</p></td>")
            .Replace("Вес:(кг)", "Вес : ( кг )");
        var parsed = await Parse(html);
        Assert.True(parsed.IsComplete, Messages(parsed));
        Assert.Equal(BlockSpecificationNormalizer.Normalize(await Parse(Html())), BlockSpecificationNormalizer.Normalize(parsed));
    }

    [Fact]
    public async Task DoesNotFillMissingTotalsFromThePartTableAndRejectsMultipleDifferentOrderHeaders()
    {
        var html = Html().Replace("<td>Всего по заказу:</td>", "<td>Иная таблица:</td>");
        var parsed = await Parse(html);
        Assert.False(parsed.IsComplete); Assert.Null(parsed.PartCount); Assert.Null(parsed.PunchCount); Assert.Null(parsed.CutLengthMillimeters);
        html = Html().Replace("Логотип</td>", "Логотип</td><td>Спецификация № 6 мм 2026-05-15-0079--ЛАЗЕР.rcam</td>");
        parsed = await Parse(html);
        Assert.False(parsed.IsComplete); Assert.Null(parsed.Header);
        Assert.Contains(parsed.Issues, issue => issue.Field == SpecificationField.Header && issue.Code == "ConflictingValues");
    }

    [Fact]
    public async Task ExplicitMeterUnitsAreConvertedAndOversizedOrCancelledInputsNeverNormalize()
    {
        var parsed = await Parse(Html().Replace("Длина реза:(мм)</td><td>32500.5", "Длина реза:(м)</td><td>32.5005"));
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(32500.5m, parsed.CutLengthMillimeters!.Value);
        Assert.Equal("32.5005", Assert.Single(parsed.CutLengthMillimeters.Sources).RawText);
        parsed = await Parse(Html().Replace("32500.5", "2147483647000"));
        Assert.False(parsed.IsComplete); Assert.Contains(parsed.Issues, issue => issue.Code == "LengthOutOfRange");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new BlockHtmlSpecificationParser().Parse(new(DocumentKind.Html, "", [], []), cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleSheetSizesBecomeOneCommaSeparatedMdbValueWithTheOrderTotals(bool oneCell)
    {
        var sizes = oneCell ? "<td>6x4000x1500 1шт; 6x3000x1000 2шт</td>" : "<td>6x4000x1500 1шт</td><td>6x3000x1000 2шт</td>";
        var parsed = await Parse(Html().Replace("<td>6.0x4000x1500</td>", sizes));
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Null(parsed.Dimensions);
        Assert.Equal(new[] { new SheetLayout(new(6, 4000, 1500), 1), new SheetLayout(new(6, 3000, 1000), 2) }, parsed.SheetLayouts.Select(layout => layout.Value));
        Assert.Equal(3, parsed.SheetCount!.Value); Assert.Equal(32500.5m, parsed.CutLengthMillimeters!.Value);
        Assert.Empty(parsed.Issues);
        var actual = BlockSpecificationNormalizer.Normalize(parsed);
        Assert.Equal(BlockSpecificationNormalizer.Normalize(await Parse(Html())) with { SheetSize = "1500x4000, 1000x3000" }, actual);
        Assert.All(parsed.SheetLayouts, layout => Assert.NotEmpty(layout.Sources));
    }

    [Fact]
    public async Task AnExtraSizeWithoutAQuantityIsAlsoDetectedAndDeclaredCountsAreChecked()
    {
        var parsed = await Parse(Html().Replace("<td>6.0x4000x1500</td>", "<td>6x4000x1500</td><td>6x3000x1000</td>"));
        Assert.Equal(2, parsed.SheetLayouts.Count); Assert.True(parsed.IsComplete, Messages(parsed));
        Assert.Equal("1500x4000, 1000x3000", BlockSpecificationNormalizer.Normalize(parsed).SheetSize);
        parsed = await Parse(Html().Replace("6.0x4000x1500", "6x4000x1500 2шт"));
        Assert.False(parsed.IsComplete); Assert.Contains(parsed.Issues, issue => issue.Code == "SheetCountMismatch");
        parsed = await Parse(Html().Replace("6.0x4000x1500", "6x4000x1500 3шт"));
        Assert.True(parsed.IsComplete, Messages(parsed));
    }

    [Fact]
    public async Task RepeatedLayoutsDoNotDuplicateTheStoredSizeAndDifferentThicknessesCannotShareAnMdbRow()
    {
        var parsed = await Parse(Html().Replace("<td>6.0x4000x1500</td>", "<td>6x4000x1500</td><td>6x3000x1000</td><td>6x4000x1500</td>"));
        Assert.True(parsed.IsComplete, Messages(parsed)); Assert.Equal(3, parsed.SheetLayouts.Count);
        Assert.Equal("1500x4000, 1000x3000", BlockSpecificationNormalizer.Normalize(parsed).SheetSize);
        parsed = await Parse(Html().Replace("<td>6.0x4000x1500</td>", "<td>6x4000x1500</td><td>8x3000x1000</td>"));
        Assert.False(parsed.IsComplete); Assert.Contains(parsed.Issues, issue => issue.Code == "ConflictingThickness");
        Assert.Throws<ArgumentException>(() => BlockSpecificationNormalizer.Normalize(parsed));
    }

    [Fact]
    public async Task AnOversizedSizeListKeepsEverySourceAndNeverSilentlyTruncatesTheMdbText()
    {
        var sizes = string.Concat(Enumerable.Range(1000, 30).Select(length => $"<td>6x{length}x1000</td>"));
        var parsed = await Parse(Html().Replace("<td>6.0x4000x1500</td>", sizes));
        Assert.False(parsed.IsComplete); Assert.Equal(30, parsed.SheetLayouts.Count);
        var issue = Assert.Single(parsed.Issues); Assert.Equal("SheetSizeTooLong", issue.Code); Assert.Equal(30, issue.Sources!.Count);
        Assert.Throws<ArgumentException>(() => BlockSpecificationNormalizer.Normalize(parsed));
    }

    [Fact]
    public async Task MissingClosingRowTagsInGeneratedReportsStillKeepWeightAndDurationSeparate()
    {
        var parsed = await Parse(Html().Replace("</td></tr>", "</td>"));
        Assert.True(parsed.IsComplete, Messages(parsed));
        Assert.Equal(48.2m, parsed.WeightKilograms!.Value); Assert.Equal(TimeSpan.FromMinutes(36 * 60 + 47), parsed.CuttingDuration!.Value);
    }

    [Fact]
    public async Task UnknownDocumentsAndHansMarkersDoNotEnterTheBlockProfile()
    {
        foreach (var html in new[] { "<table><tr><td>Материал: Ст3</td></tr></table>", "@" + Html(), "*@" + Html() })
        {
            var parsed = await Parse(html);
            Assert.False(parsed.IsComplete); Assert.Contains(parsed.Issues, issue => issue.Code == "UnsupportedFormat");
        }
        var pdf = new SourceDocument(DocumentKind.Pdf, null, [], []);
        Assert.False(new BlockHtmlSpecificationParser().Parse(pdf, TestContext.Current.CancellationToken).IsComplete);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("999.999", 1)]
    [InlineData("1000", 2)]
    [InlineData("1999.9", 2)]
    [InlineData("2000", 3)]
    [InlineData("212973.1", 213)]
    public void KeepsTheLegacyBlockMeterRoundingEvenOnExactMultiples(string millimeters, int expected)
    {
        var value = decimal.Parse(millimeters, CultureInfo.InvariantCulture);
        Assert.Equal(expected, BlockSpecificationNormalizer.LegacyCutLengthMeters(value));
        var legacy = int.Parse(millimeters.Split('.')[0], CultureInfo.InvariantCulture) / 1000 + 1;
        Assert.Equal(legacy, expected);
    }

    private static string Html(string partName = "Контрольная деталь") => $$"""
        <html><head><meta charset="utf-8"></head><body>
        <!--table--><table><tr><td>Логотип</td><td><b>Спецификация № 6 мм 2026-05-14-0078--ЛАЗЕР.rcam</b></td><td>01.06.2026</td></tr></table>
        <!--table--><table><tr><td>Листов:</td><td>3</td><td>Габариты:</td><td>6.0x4000x1500</td></tr></table>
        <!--table--><table><tr><td>{{System.Net.WebUtility.HtmlEncode(partName)}}</td><td>Материал:</td><td>Сплав_X77 6.0 mm</td><td>Одна деталь</td><td>Все детали</td></tr>
        <tr><td>Вес:</td><td>99999</td><td>Кол-во деталей:</td><td>777</td><td>Пробивка:</td><td>888</td></tr><!--detail-end--></table>
        <!--table--><table><tbody><tr><td>Всего по заказу:</td><td>Общие кол-во деталей:</td><td>14</td></tr>
        <tr><td>Длина реза:(мм)</td><td>32500.5</td></tr><tr><td>Пробивки:</td><td>31</td></tr><tr><td>Вес:(кг)</td><td>48.2</td></tr>
        <tr><td>Время:(ч:мм)</td><td>36:47</td></tr></tbody><!--summary-end--></table>
        <!--table--></body></html>
        """;

    private static string Messages(SpecificationParseResult result) => string.Join("; ", result.Issues.Select(issue => issue.Message));
    private static async Task<SpecificationParseResult> Parse(string html, string name = "synthetic.html")
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-block-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, name); await File.WriteAllTextAsync(path, html, Encoding.UTF8, TestContext.Current.CancellationToken);
            var doc = await new DocumentReader().ReadAsync(path, TestContext.Current.CancellationToken);
            return new BlockHtmlSpecificationParser().Parse(doc, TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(root, true); }
    }
}
