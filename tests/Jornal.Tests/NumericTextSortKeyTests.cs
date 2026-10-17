using System.Globalization;
using Jornal.Desktop.Tables;
using Xunit;

namespace Jornal.Tests;

public sealed class NumericTextSortKeyTests
{
    [Theory]
    [InlineData("3", "20", -1)]
    [InlineData("20", "100", -1)]
    [InlineData("100", "3", 1)]
    [InlineData("1,3", "1.03", 1)]
    [InlineData("1.03", "1,0300", 0)]
    [InlineData("+02", "2.0", 0)]
    [InlineData("-2", "0", -1)]
    [InlineData("-20", "-3", -1)]
    [InlineData("-0,000", "0", 0)]
    [InlineData(" 2,50 ", "2.5", 0)]
    [InlineData("0.000000000000000000000000000001", "0.000000000000000000000000000002", -1)]
    [InlineData("1234567890123456789012345678901234567891", "1234567890123456789012345678901234567890", 1)]
    public void ComparisonIsNumericAndExactInEitherCulture(string first, string second, int expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "ru-RU", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var a = NumericTextSortKey.Parse(first)!; var b = NumericTextSortKey.Parse(second)!;
                Assert.Equal(expected, Math.Sign(a.CompareTo(b)));
                Assert.Equal(-expected, Math.Sign(b.CompareTo(a)));
                Assert.Equal(expected, Math.Sign(((IComparable)a).CompareTo(b)));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("2+1")]
    [InlineData("комплект")]
    [InlineData("1 000")]
    [InlineData("1\u00a0000")]
    [InlineData("1,2.3")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1 кг")]
    [InlineData("1.")]
    [InlineData(".5")]
    public void UnknownOrCompositeTextHasNoNumericKey(string? value) => Assert.Null(NumericTextSortKey.Parse(value));

    [Fact]
    public void TheFullLegacyTextLengthCanBeComparedWithoutOverflowOrRounding()
    {
        var largest = NumericTextSortKey.Parse(new string('9', 255))!;
        var lower = NumericTextSortKey.Parse(new string('9', 254) + "8")!;
        Assert.Equal(1, largest.CompareTo(lower));
        var smallest = NumericTextSortKey.Parse("0." + new string('0', 252) + "1")!;
        Assert.Equal(1, smallest.CompareTo(NumericTextSortKey.Parse("0")));
        Assert.Null(NumericTextSortKey.Parse(new string('9', 256)));
    }
}
