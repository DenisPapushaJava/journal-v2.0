using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Avalonia.Input;
using Jornal.Core.Orders;
using Jornal.Core.Results;
using Jornal.Desktop.Clipboard;
using Xunit;

namespace Jornal.Tests;

public sealed class ResultOutputTests
{
    [Theory]
    [InlineData("10", "#C0504D", 192, 80, 77, 20)]
    [InlineData("12", "#2F5496", 47, 84, 150, 24)]
    public void HtmlAndRtfUseTheSameChosenFormattingWithoutChangingPlainText(string size, string color,
        int red, int green, int blue, int halfPoints)
    {
        const string text = "Лист 6x1500x3000\tМатериал: Ст3\nКомментарий ₽ 😀";
        var content = ResultClipboardContent.FromText(text, new ResultTextStyle
            { FontSizePoints = decimal.Parse(size, CultureInfo.InvariantCulture), ColorHex = color });
        Assert.Equal(text, content.Text);
        var html = new HtmlParser().ParseDocument(content.Html);
        foreach (var element in html.QuerySelectorAll("table,td"))
        {
            Assert.Contains($"font-size:{size}pt", element.GetAttribute("style"));
            Assert.Contains($"color:{color}", element.GetAttribute("style"));
            Assert.Contains("font-family:Arial", element.GetAttribute("style"));
        }
        Assert.Contains($@"\red{red}\green{green}\blue{blue};", content.Rtf);
        Assert.Contains($@"\f0\fs{halfPoints}\cf1 ", content.Rtf);
    }

    [Fact]
    public void FormattingDefaultsAndNormalizationCannotInjectMarkupOrBreakRtf()
    {
        var defaults = ResultClipboardContent.FromText("Текст");
        Assert.Contains("font-size:10pt;color:#B64C49", defaults.Html);
        Assert.Contains(@"\fs20\cf1", defaults.Rtf);
        var content = ResultClipboardContent.FromText("Текст", new ResultTextStyle
            { FontSizePoints = 999, ColorHex = "red;\"><script>bad</script>" });
        Assert.DoesNotContain("<script>", content.Html);
        Assert.Contains("font-size:10pt;color:#B64C49", content.Html);
        Assert.Contains(@"\fs20\cf1", content.Rtf);
        Assert.Equal(10, new ResultTextStyle { FontSizePoints = -1 }.Normalize().FontSizePoints);
    }

    [Fact]
    public void SingleLinePreservesLegacyFieldsSeparatorsAndWeight()
    {
        var text = OrderResultFormatter.Format([Order()]);
        Assert.Equal("Лист 2,5x1250x2500,\t   Материал: Амг3,\t   2 шт.\t   Вес 38,5 кг.\t   Резка 1250 ₽.\t   резка Лазер\n", text);
    }

    [Theory]
    [InlineData("0", "500")]
    [InlineData("499.49", "500")]
    [InlineData("500.5", "500")]
    [InlineData("501.5", "502")]
    [InlineData("1250.5", "1250")]
    [InlineData("1251.5", "1252")]
    [InlineData("5000000000", "5000000000")]
    public void OutputMinimumAndRoundingDoNotChangeStoredPrice(string stored, string expected)
    {
        var value = decimal.Parse(stored, CultureInfo.InvariantCulture);
        var order = Order() with { StoredCuttingPrice = value };
        var result = OrderResultFormatter.Format([order]);
        Assert.Equal(expected, Regex.Match(result, @"Резка\s+(\d+) ₽").Groups[1].Value);
        Assert.Equal(value, order.StoredCuttingPrice);
    }

    [Fact]
    public void BatchPaddingUsesFullLengthsAndPreservesArbitraryMaterial()
    {
        var first = Order() with { StoredCuttingPrice = 100, SheetCount = "1" };
        var second = Order() with { Id = 2, ThicknessMillimeters = 16, MaterialName = "5083H111",
            SheetCount = "22", WeightKilograms = "2", StoredCuttingPrice = 4850, CuttingType = "HansLaser" };
        var text = OrderResultFormatter.Format([first, second]);
        Assert.Equal("Лист 2,5x1250x2500,\t   Материал:         Амг3,\t     1 шт.\t   Вес 38,5 кг.\t   Резка   500 ₽.\t   резка         Лазер\n" +
            "Лист   16x1250x2500,\t   Материал: 5083H111,\t   22 шт.\t   Вес       2 кг.\t   Резка 4850 ₽.\t   резка HansLaser\n", text);
        var longMaterial = new string('М', 350) + "_неизвестная";
        Assert.Contains(longMaterial, OrderResultFormatter.Format([Order() with { MaterialName = longMaterial }, second]));
    }

    [Fact]
    public void UserExampleIsOrderedByNumericThicknessWithoutChangingAnyLine()
    {
        OrderSummary[] orders =
        [
            Order() with { Id = 1, ThicknessMillimeters = 3, MaterialName = "оцинк.", SheetCount = "3",
                WeightKilograms = "177,6", StoredCuttingPrice = 4385 },
            Order() with { Id = 2, ThicknessMillimeters = 1.5m, MaterialName = "оцинк.", SheetCount = "7",
                WeightKilograms = "260,9", StoredCuttingPrice = 2941 },
            Order() with { Id = 3, ThicknessMillimeters = 10, MaterialName = "Cт3", SheetSize = "1500x6000", SheetCount = "6",
                WeightKilograms = "2554,6", StoredCuttingPrice = 7841 },
            Order() with { Id = 4, ThicknessMillimeters = 0.8m, MaterialName = "Aisi-430", SheetSize = "1000x2000", SheetCount = "253",
                WeightKilograms = "4477,4", StoredCuttingPrice = 76012 }
        ];
        var original = orders.ToArray();

        Assert.Equal("Лист 0,8x1000x2000,\t   Материал: Aisi-430,\t   253 шт.\t   Вес 4477,4 кг.\t   Резка 76012 ₽.\t   резка Лазер\n" +
            "Лист 1,5x1250x2500,\t   Материал:     оцинк.,\t       7 шт.\t   Вес   260,9 кг.\t   Резка   2941 ₽.\t   резка Лазер\n" +
            "Лист     3x1250x2500,\t   Материал:     оцинк.,\t       3 шт.\t   Вес   177,6 кг.\t   Резка   4385 ₽.\t   резка Лазер\n" +
            "Лист   10x1500x6000,\t   Материал:           Cт3,\t       6 шт.\t   Вес 2554,6 кг.\t   Резка   7841 ₽.\t   резка Лазер\n",
            OrderResultFormatter.Format(orders));
        Assert.Equal(original, orders);
    }

    [Fact]
    public void EqualThicknessGroupsMaterialsIgnoringCaseAndPreservesTiesAndNames()
    {
        var orders = new[]
        {
            Order() with { Id = 1, ThicknessMillimeters = 2, MaterialName = "Z-β", SheetSize = "1000x2000" },
            Order() with { Id = 2, ThicknessMillimeters = 2, MaterialName = "Aisi-430", SheetSize = "1250x2500" },
            Order() with { Id = 3, ThicknessMillimeters = 2, MaterialName = "aisi-430", SheetSize = "1500x3000" },
            Order() with { Id = 4, ThicknessMillimeters = 2, MaterialName = "Aisi-430", SheetSize = "1500x6000" },
            Order() with { Id = 5, ThicknessMillimeters = 1.5m, MaterialName = "Z-β" }
        };
        var lines = OrderResultFormatter.Format(orders).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[] { "Z-β", "Aisi-430", "aisi-430", "Aisi-430", "Z-β" },
            lines.Select(line => Regex.Match(line, @"Материал:\s+([^,\t]+),").Groups[1].Value));
        Assert.Contains("2x1250x2500,", lines[1]);
        Assert.Contains("2x1500x3000,", lines[2]);
        Assert.Contains("2x1500x6000,", lines[3]);
    }

    [Fact]
    public void UnknownThicknessFollowsAllKnownThicknesses()
    {
        var lines = OrderResultFormatter.Format([
            Order() with { Id = 1, ThicknessMillimeters = null, MaterialName = "A" },
            Order() with { Id = 2, ThicknessMillimeters = 10, MaterialName = "Z" },
            Order() with { Id = 3, ThicknessMillimeters = 2, MaterialName = "Z" },
            Order() with { Id = 4, ThicknessMillimeters = 2.5m, MaterialName = "Z" }
        ]).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(new[] { "Лист     2x1250x2500,", "Лист 2,5x1250x2500,", "Лист   10x1250x2500,",
            "Лист     —x1250x2500," }, lines.Select(line => line.Split('\t')[0]));
    }

    [Fact]
    public void MissingPriceIsRejectedBeforeFormatting()
    {
        Assert.Throws<ArgumentException>(() => OrderResultFormatter.Format([Order(), Order() with { StoredCuttingPrice = null }]));
        Assert.Empty(OrderResultFormatter.Format([]));
    }

    [Fact]
    public void HtmlHasAlignedCellsAndEscapesUserTextWithoutLosingEdits()
    {
        const string text = "Примечание <script>alert('x')</script> & {правка} 😀\nЛист 6x1000x2000,\t   Материал: 5083H111,\t   3 шт.\t   Вес 10,5 кг.\t   Резка 500 ₽.\t   резка HansLaser\n";
        var content = ResultClipboardContent.FromText(text);
        var html = new HtmlParser().ParseDocument(content.Html);
        Assert.Equal(text, content.Text);
        Assert.Empty(html.QuerySelectorAll("script"));
        var rows = html.QuerySelectorAll("tr");
        Assert.Equal(2, rows.Length);
        Assert.Equal("Примечание <script>alert('x')</script> & {правка} 😀", rows[0].TextContent);
        Assert.Equal("6", rows[0].QuerySelector("td")!.GetAttribute("colspan"));
        Assert.Equal(6, rows[1].QuerySelectorAll("td").Length);
        Assert.Contains("Резка 500 ₽.", rows[1].TextContent);
        Assert.DoesNotContain("<script>", content.Html);
        Assert.Contains("background-color:#ffffff", content.Html);
    }

    [Fact]
    public void RtfEscapesUnicodeTabsLineEndingsAndControlCharacters()
    {
        var content = ResultClipboardContent.FromText("Лист {1} \\ ₽ 😀\tA\r\nБ\rВ\n");
        Assert.StartsWith(@"{\rtf1", content.Rtf);
        Assert.Contains(@"\u1051?", content.Rtf);
        Assert.Contains(@"\u8381?", content.Rtf);
        Assert.Contains(@"\u-10179?\u-8704?", content.Rtf);
        Assert.Contains(@"\{1\} \\ ", content.Rtf);
        Assert.Contains(@"\tab A\par ", content.Rtf);
        Assert.Equal(3, Regex.Matches(content.Rtf, @"\\par ").Count);
        Assert.All(content.Rtf, character => Assert.True(character <= 127));
    }

    [Fact]
    public void WindowsHtmlOffsetsPointToUtf8FragmentAndCompleteContext()
    {
        const string fragment = "<table><tr><td>Амг3 &amp; ₽ 😀</td></tr></table>";
        var bytes = ResultClipboardPayload.WindowsHtml(fragment);
        var header = Encoding.UTF8.GetString(bytes);
        int Offset(string field) => int.Parse(Regex.Match(header, $@"{field}:(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        var start = Offset("StartFragment");
        var end = Offset("EndFragment");
        Assert.Equal(fragment, Encoding.UTF8.GetString(bytes[start..end]));
        Assert.Equal(bytes.Length, Offset("EndHTML"));
        Assert.StartsWith("<!DOCTYPE html>", Encoding.UTF8.GetString(bytes[Offset("StartHTML")..]));
        Assert.EndsWith("<!--StartFragment-->", Encoding.UTF8.GetString(bytes[..start]));
        Assert.StartsWith("<!--EndFragment-->", Encoding.UTF8.GetString(bytes[end..]));
    }

    [Fact]
    public async Task ClipboardPayloadContainsAllFormatsInOneItemWithNativeByteEncoding()
    {
        var content = ResultClipboardContent.FromText(OrderResultFormatter.Format([Order()]));
        using var data = (IAsyncDataTransfer)ResultClipboardPayload.Create(content);
        Assert.Single(data.Items);
        Assert.Equal(content.Text, await data.TryGetTextAsync());
        var rtf = await data.TryGetValueAsync(ResultClipboardPayload.RtfFormat);
        Assert.Equal(content.Rtf + (OperatingSystem.IsWindows() ? "\0" : ""), Encoding.ASCII.GetString(rtf!));
        var html = await data.TryGetValueAsync(ResultClipboardPayload.HtmlFormat);
        Assert.NotNull(html);
        Assert.Contains("Материал", Encoding.UTF8.GetString(html));
        Assert.Equal(OperatingSystem.IsWindows() ? "HTML Format" : OperatingSystem.IsMacOS() ? "public.html" : "text/html", ResultClipboardPayload.HtmlFormat.Identifier);
        Assert.Equal(OperatingSystem.IsWindows() ? "Rich Text Format" : OperatingSystem.IsMacOS() ? "public.rtf" : "text/rtf", ResultClipboardPayload.RtfFormat.Identifier);

        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "result.html"), ResultClipboardPayload.HtmlDocument(content.Html));
            File.WriteAllText(Path.Combine(directory, "result.rtf"), content.Rtf, Encoding.ASCII);
            File.WriteAllText(Path.Combine(directory, "result.txt"), content.Text);
        }
    }

    internal static OrderSummary Order() => new(1, new DateOnly(2026, 10, 15), "1015-1001", "Менеджер", "Пример",
        "Лазер", "Амг3", 2.5m, 1250m, -1)
        { SheetSize = "1250x2500", SheetCount = "2", WeightKilograms = "38.5" };
}
