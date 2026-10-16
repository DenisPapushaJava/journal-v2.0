using Jornal.Core.Pricing;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class PriceReaderTests
{
    [Fact]
    public void ReadsFirstRowByThicknessNameIndependentlyOfColumnOrderAndPreservesNullAndZero()
    {
        var price = AccessPriceMapper.ReadExport("PriceLL500", new StringReader("\"Код\",\"2,5\",\"5\",\"0.5\"\n1,87,NULL,0\n2,999,999,999\n"), TestContext.Current.CancellationToken);
        Assert.True(price.HasRow);
        Assert.Equal(new PriceRate[] { new(2.5m, "2,5", 87), new(5m, "5", null), new(0.5m, "0.5", 0) }, price.Rates);
        var reversed = AccessPriceMapper.Map("PriceLL500", ["0.5", "5", "2,5"], [0, DBNull.Value, 87]);
        Assert.Equal(price.Rates.OrderBy(rate => rate.ThicknessMillimeters), reversed.Rates.OrderBy(rate => rate.ThicknessMillimeters));
        var actualHeaderFormat = AccessPriceMapper.ReadExport("PriceLL500",
            new StringReader("0,5\t0,8\t1\t1,2\t2,5\n10\t20\t30\t40\t50\n"), TestContext.Current.CancellationToken, '\t');
        Assert.Equal(5, actualHeaderFormat.Rates.Count);
        Assert.Equal(50, actualHeaderFormat.Rates.Single(rate => rate.ThicknessMillimeters == 2.5m).RublesPerMeter);
    }

    [Fact]
    public void DistinguishesEmptyTableFromNullTariffAndRejectsAmbiguousOrMalformedValues()
    {
        var empty = AccessPriceMapper.ReadExport("PriceP", new StringReader("\"2,5\",\"3\"\n"), TestContext.Current.CancellationToken);
        Assert.False(empty.HasRow);
        Assert.All(empty.Rates, rate => Assert.Null(rate.RublesPerMeter));
        Assert.Throws<PriceReadException>(() => AccessPriceMapper.Map("PriceP", ["2,5", "2.50"], [1, 2]));
        Assert.Throws<PriceReadException>(() => AccessPriceMapper.Map("PriceP", ["2,5", "2,5"], [1, 2]));
        Assert.Throws<PriceReadException>(() => AccessPriceMapper.Map("PriceP", ["2,5"], []));
        foreach (var invalid in new object[] { "NULL", "не цена", "87.5", 2147483648L })
            Assert.Throws<PriceReadException>(() => AccessPriceMapper.Map("PriceP", ["2,5"], [invalid]));
        Assert.Throws<PriceReadException>(() => AccessPriceMapper.ReadExport("PriceP", new StringReader(""), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsUntrustedTableNamesBeforeOpeningFilesOrStartingProcesses()
    {
        foreach (var name in new[] { "Orders", "PriceP]; DELETE FROM Orders", "PriceP --", "pricep", "" })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new AccessPriceReader("missing.mdb").ReadAsync(name, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => new MdbToolsPriceReader("missing.exe").ReadAsync("missing.mdb", name, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(() => new AccessHostClient("missing.mdb", "missing.exe").ReadPriceTableAsync(name, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task LinuxExporterDrainsLargeOutputAfterTheFirstPriceRow()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Проверка Linux-процесса MDB Tools.");
        var root = Path.Combine(Path.GetTempPath(), "journal-price-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "exporter");
        try
        {
            await File.WriteAllTextAsync(script, "#!/bin/sh\nprintf '\"2,5\"\\n87\\n'\ni=0\nwhile [ $i -lt 30000 ]; do printf '999999999\\n'; i=$((i+1)); done\n", TestContext.Current.CancellationToken);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var table = await new MdbToolsPriceReader(script).ReadAsync("unused.mdb", "PriceP", timeout.Token);
            Assert.Equal(87, Assert.Single(table.Rates).RublesPerMeter);
            timeout.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MdbToolsPriceReader("missing.exe").ReadAsync("unused.mdb", "PriceP", timeout.Token));
        }
        finally { Directory.Delete(root, true); }
    }
}
