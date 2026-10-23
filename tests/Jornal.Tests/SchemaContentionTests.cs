using System.Runtime.InteropServices;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class SchemaContentionTests
{
    [Fact]
    public async Task RechecksReadinessOnlyAfterTheLockBackoff()
    {
        var reads = 0; var writes = 0; var waited = false;
        await SharedSchemaPreparation.EnsureAsync(
            () =>
            {
                reads++;
                if (reads == 1) return false;
                Assert.True(waited, "Не читать каталог во время подготовки другим клиентом.");
                return true;
            },
            () => { writes++; throw new IOException("Synthetic lock"); },
            exception => exception is IOException, TestContext.Current.CancellationToken,
            (_, _) => { waited = true; return Task.CompletedTask; });
        Assert.Equal(2, reads);
        Assert.Equal(1, writes);
    }

    [Theory]
    [InlineData(-67044352, "3045", true)]
    [InlineData(-67044352, "3051", false)]
    [InlineData(-67044352, "3031", false)]
    [InlineData(-67044352, "HY000", false)]
    [InlineData(-67044352, "40001", false)]
    [InlineData(-67044352, " 3045", false)]
    [InlineData(3045, "3045", true)]
    [InlineData(3031, "3045", false)]
    [InlineData(3045, "3031", false)]
    public void RecognizesJetDaoCodeInSqlStateWithoutHidingPermanentErrors(int native, string state, bool busy)
    {
        var records = new[] { (native, (string?)state) };
        Assert.Equal(busy, AccessSchemaContention.IsProviderLock(unchecked((int)0x80004005), records));
        Assert.False(AccessSchemaContention.IsProviderLock(unchecked((int)0x80070005), records));
        Assert.False(AccessSchemaContention.IsProviderLock(unchecked((int)0x80004005),
            new[] { (-67044352, (string?)"3045"), (-1, (string?)"3031") }));
    }

    [Theory]
    [InlineData(3008)] [InlineData(3045)] [InlineData(3046)] [InlineData(3188)]
    [InlineData(3211)] [InlineData(3218)] [InlineData(3260)] [InlineData(3261)] [InlineData(3262)]
    public void RecognizesExplicitProviderLocks(int code) =>
        Assert.True(AccessSchemaContention.IsProviderLock(unchecked((int)0x80004005), [0, code]));

    [Theory]
    [InlineData(3031)] [InlineData(3033)] [InlineData(3049)] [InlineData(3050)] [InlineData(3051)]
    [InlineData(3022)] [InlineData(3061)] [InlineData(3343)] [InlineData(9999)]
    public void DoesNotRetryPermanentOrAmbiguousProviderErrors(int code)
    {
        Assert.False(AccessSchemaContention.IsProviderLock(unchecked((int)0x80004005), [code]));
        Assert.False(AccessSchemaContention.IsProviderLock(unchecked((int)0x80040E40), [3045, code]));
    }

    [Theory]
    [InlineData(0x80070020u, true)] [InlineData(0x80070021u, true)]
    [InlineData(0x80030020u, true)] [InlineData(0x80030021u, true)]
    [InlineData(0x80040E40u, true)] [InlineData(0x80040E92u, true)]
    [InlineData(0x80004005u, false)] [InlineData(0x80070005u, false)]
    [InlineData(0x80070035u, false)] [InlineData(0x80040E09u, false)] [InlineData(0x80040E4Du, false)]
    public void GenericFailureIsNotEvidenceOfALock(uint code, bool busy) =>
        Assert.Equal(busy, AccessSchemaContention.IsProviderLock(unchecked((int)code), [0]));

    [Theory]
    [InlineData(0x80070005u)] [InlineData(0x80030005u)] [InlineData(0x80040E09u)]
    [InlineData(0x80040E4Du)] [InlineData(0x80040E94u)]
    public void ExplicitPermissionOrAuthenticationFailureCannotBeOverriddenByALockRecord(uint code) =>
        Assert.False(AccessSchemaContention.IsProviderLock(unchecked((int)code), [3045]));

    [Theory]
    [InlineData(3045, 6, 5)] [InlineData(3031, 1, 0)] [InlineData(3033, 1, 0)]
    [InlineData(3051, 1, 0)] [InlineData(3343, 1, 0)] [InlineData(9999, 1, 0)]
    public async Task OnlyProvenLocksConsumeThePreparationRetryBudget(int code, int expectedAttempts, int expectedDelays)
    {
        var attempts = 0; var delays = new List<TimeSpan>();
        var failure = new ExternalException("Synthetic provider failure", unchecked((int)0x80004005));
        var observed = await Assert.ThrowsAsync<ExternalException>(() => SharedSchemaPreparation.EnsureAsync(
            () => false, () => { attempts++; throw failure; },
            error => AccessSchemaContention.IsProviderLock(error.HResult, [code]), TestContext.Current.CancellationToken,
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; }));
        Assert.Same(failure, observed); Assert.Equal(expectedAttempts, attempts); Assert.Equal(expectedDelays, delays.Count);
        if (expectedDelays > 0) Assert.Equal(new[] { 250d, 500, 1000, 2000, 2000 }, delays.Select(delay => delay.TotalMilliseconds));
    }
}
