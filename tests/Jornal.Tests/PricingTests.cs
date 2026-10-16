using System.Globalization;
using Jornal.Core.Pricing;
using Xunit;

namespace Jornal.Tests;

public sealed class PricingTests
{
    [Theory]
    [InlineData("Лазер", "0.5", null, "PriceLL500")]
    [InlineData("Лазер", "20", "1", "PriceLL500")]
    [InlineData("Плазма", "2", null, "PriceP")]
    [InlineData("Плазма", "32", null, "PriceP")]
    [InlineData("Газо-кислородная", "16", null, "PriceG")]
    [InlineData("Газо-кислородная", "200", null, "PriceG")]
    [InlineData("HansLaser", "60", "1", "PriceHL_id1")]
    [InlineData("HansLaser", "60", "2", "PriceHL_id2")]
    [InlineData("HansLaser", "60", "0", "PriceLL500")]
    [InlineData("HansLaser", "60", null, "PriceLL500")]
    [InlineData("HansLaser", "60", "01", "PriceLL500")]
    [InlineData("HansLaser", "60", " 1", "PriceLL500")]
    public void UsesLegacyPriceTableAndThicknessBoundaries(string kind, string thickness, string? id, string expected)
    {
        var request = new CuttingPriceRequest(kind, Number(thickness), 25, 1m, id);
        Assert.Equal(expected, CuttingPriceCalculator.SelectTable(request));
        if (kind == "HansLaser") Assert.Equal(Jornal.IdMaterialPrice.NameTableDBOfId(id!), expected);
    }

    [Theory]
    [InlineData("Лазер", "20.1")]
    [InlineData("Плазма", "1.5")]
    [InlineData("Плазма", "32.1")]
    [InlineData("Газо-кислородная", "15.9")]
    [InlineData("Газо-кислородная", "200.1")]
    [InlineData("HansLaser", "60.1")]
    public void RangeIsCheckedEvenWhenTariffExists(string kind, string thickness)
    {
        var error = Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.SelectTable(new(kind, Number(thickness), 1, 1)));
        Assert.Equal(PriceCalculationFailure.ThicknessOutOfRange, error.Failure);
    }

    [Fact]
    public void KeepsDoubleArithmeticToEvenAndDoesNotApplyTheEmailMinimum()
    {
        var request = new CuttingPriceRequest("Лазер", 2.5m, 25, 2.3m);
        var table = Table(1);
        Assert.Equal(57m, CuttingPriceCalculator.Calculate(request, table).StoredPrice);
        Assert.Equal(58m, decimal.Round(1m * 25m * 2.3m));
        Assert.Equal(2m, CuttingPriceCalculator.Calculate(request with { CutLengthMeters = 1, Coefficient = 2.5m }, table).StoredPrice);
        Assert.Equal(4m, CuttingPriceCalculator.Calculate(request with { CutLengthMeters = 1, Coefficient = 3.5m }, table).StoredPrice);
        Assert.Equal(0m, CuttingPriceCalculator.Calculate(request with { CutLengthMeters = 0 }, table).StoredPrice);
        Assert.Equal(0m, CuttingPriceCalculator.Calculate(request, Table(0)).StoredPrice);
        foreach (var tariff in new[] { 1, 17, 87, 253, 1001 })
        foreach (var length in new[] { 0, 1, 15, 25, 127, 12345 })
        foreach (var coefficient in new[] { 1m, 1.1m, 1.25m, 2.3m, 3m })
        {
            var expected = (decimal)Math.Round(Convert.ToDouble(tariff) * length * (double)coefficient);
            Assert.Equal(expected, CuttingPriceCalculator.Calculate(request with { CutLengthMeters = length, Coefficient = coefficient }, Table(tariff)).StoredPrice);
        }
    }

    [Fact]
    public void MissingNullAndNegativeTariffsCannotBecomeAFreeQuote()
    {
        var request = new CuttingPriceRequest("Лазер", 2.5m, 1, 1);
        foreach (var table in new[] { Table(null), new PriceTable(PriceTables.Laser, false, []), new PriceTable(PriceTables.Laser, true, []) })
            Assert.Equal(PriceCalculationFailure.PriceUnavailable,
                Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.Calculate(request, table)).Failure);
        Assert.Equal(PriceCalculationFailure.InvalidPrice,
            Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.Calculate(request, Table(-1))).Failure);
        Assert.Throws<ArgumentException>(() => CuttingPriceCalculator.Calculate(request, Table(87) with { Name = "PriceLM500" }));
    }

    [Fact]
    public void ValidatesInputsCurrencyOverflowAndQuoteBreakdown()
    {
        var request = new CuttingPriceRequest("Лазер", 2.5m, 1, 1);
        foreach (var invalid in new[] { request with { ThicknessMillimeters = 0 }, request with { ThicknessMillimeters = -1 },
            request with { CutLengthMeters = -1 }, request with { Coefficient = 0 }, request with { Coefficient = -1 } })
            Assert.Equal(PriceCalculationFailure.InvalidParameters,
                Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.Calculate(invalid, Table(1))).Failure);
        Assert.Equal(PriceCalculationFailure.UnsupportedCuttingType,
            Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.SelectTable(request with { CuttingType = "другое" })).Failure);
        var maximum = CuttingPriceCalculator.Calculate(request with { Coefficient = 922337203685477m }, Table(1));
        Assert.Equal(922337203685477m, maximum.StoredPrice);
        Assert.Equal(PriceCalculationFailure.AmountOutOfRange, Assert.Throws<PriceCalculationException>(() =>
            CuttingPriceCalculator.Calculate(request with { Coefficient = 922337203685478m }, Table(1))).Failure);
        Assert.Equal(new CuttingPriceQuote("PriceLL500", "2,5", 87, 1, 1, 87), CuttingPriceCalculator.Calculate(request, Table(87)));
    }

    [Fact]
    public void PunchCoefficientMatchesActualLegacyClassAtRoundingAndAllStepBoundaries()
    {
        foreach (var parts in new[] { 1, 2, 3, 7, 100, int.MaxValue })
        foreach (var punches in Enumerable.Range(0, 1001).Append(int.MaxValue))
        {
            var legacy = Jornal.Clasess.CalculationCoefficient.CoefficientOfPunch(Math.Round((double)punches / parts));
            Assert.Equal(Number(legacy), CuttingPriceCalculator.CalculatePunchCoefficient(parts, punches));
        }
        Assert.Equal(1.1m, CuttingPriceCalculator.CalculatePunchCoefficient(2, 5));
        Assert.Equal(1.3m, CuttingPriceCalculator.CalculatePunchCoefficient(2, 7));
        Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.CalculatePunchCoefficient(0, 1));
        Assert.Throws<PriceCalculationException>(() => CuttingPriceCalculator.CalculatePunchCoefficient(1, -1));
    }

    [Fact]
    public async Task ServiceReadsOnlySelectedTableAndDoesNotReadForInvalidOrCancelledInput()
    {
        var reader = new Reader();
        var service = new CuttingPriceService(reader);
        var request = new CuttingPriceRequest("HansLaser", 2.5m, 10, 1, "2");
        Assert.Equal(870m, (await service.CalculateAsync(request, TestContext.Current.CancellationToken)).StoredPrice);
        Assert.Equal(PriceTables.HansLaserId2, reader.Requested);
        reader.Requested = null;
        await Assert.ThrowsAsync<PriceCalculationException>(() => service.CalculateAsync(request with { ThicknessMillimeters = 61 }, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CalculateAsync(request, cancellation.Token));
        Assert.Null(reader.Requested);
    }

    private sealed class Reader : IPriceReader
    {
        public string? Requested { get; set; }
        public Task<PriceTable> ReadAsync(string name, CancellationToken cancellationToken = default)
        {
            Requested = name;
            return Task.FromResult(Table(87) with { Name = name });
        }
    }
    private static PriceTable Table(int? rate) => new(PriceTables.Laser, true, [new(2.5m, "2,5", rate)]);
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
