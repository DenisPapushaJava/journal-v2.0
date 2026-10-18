using System.Globalization;
using Jornal.Desktop.Tables;
using Xunit;

namespace Jornal.Tests;

public sealed class SpecificationNumberSortKeyTests
{
    [Theory]
    [InlineData("1018-859", "1018-900", -1)]
    [InlineData("1018-900", "1018-909", -1)]
    [InlineData("1018-920", "1018-1121", -1)]
    [InlineData("1018-0900", "1018-900", 0)]
    [InlineData(" 1018-0859 ", "1018-859", 0)]
    [InlineData("1018-0", "1018-000", 0)]
    [InlineData("1018-2401", "1018-10511", -1)]
    [InlineData("2026-10-18-859", "2026-10-18-900", -1)]
    [InlineData("Заказ-2-9", "заказ-2-10", -1)]
    [InlineData("Заказ-2", "Заказ-10", -1)]
    [InlineData("А-1", "а-01", 0)]
    [InlineData("1018-900", "1018-900-А", -1)]
    [InlineData("", "1018-859", -1)]
    [InlineData(null, "", 0)]
    [InlineData("Номер", "Номер", 0)]
    public void NumericPartsAreComparedWithoutChangingTextOrInterpretingTime(string? left, string? right, int expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "ru-RU", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                var first = new SpecificationNumberSortKey(left);
                var second = new SpecificationNumberSortKey(right);
                Assert.Equal(expected, Math.Sign(first.CompareTo(second)));
                Assert.Equal(-expected, Math.Sign(second.CompareTo(first)));
                Assert.Equal(expected, Math.Sign(((IComparable)first).CompareTo(second)));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void LongLegacyNumbersDoNotOverflowAndLeadingZeroesRemainEquivalent()
    {
        var prefix = "1018-";
        var highest = new SpecificationNumberSortKey(prefix + new string('9', 250));
        var lower = new SpecificationNumberSortKey(prefix + new string('9', 249) + "8");
        Assert.Equal(1, Math.Sign(highest.CompareTo(lower)));
        Assert.Equal(-1, new SpecificationNumberSortKey("1018-2147483647").CompareTo(new("1018-2147483648")));
        Assert.Equal(0, new SpecificationNumberSortKey(prefix + new string('0', 247) + "859").CompareTo(new("1018-859")));
    }
}
