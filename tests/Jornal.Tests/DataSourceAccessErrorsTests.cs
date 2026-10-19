using System.ComponentModel;
using Jornal.Core.Orders;
using Jornal.Infrastructure;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Tests;

public sealed class DataSourceAccessErrorsTests
{
    private static string Database => Path.Combine(Path.GetTempPath(), "journal-private-test", "OrderDB.mdb");

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(53)] [InlineData(64)] [InlineData(67)]
    [InlineData(1222)] [InlineData(1231)] [InlineData(1232)] [InlineData(1236)] [InlineData(2250)]
    public void MissingOrUnreachableSourceUsesOnlyItsFileNameWithoutGuessingTheCause(int code)
    {
        var expected = "База данных «OrderDB.mdb» недоступна. Проверьте подключение и повторите попытку.";
        Assert.Equal(expected, DataSourceAccessErrors.Describe(Database, new Win32Exception(code), "Ошибка"));
        Assert.Equal(expected, DataSourceAccessErrors.Describe(Database, new IOException("SECRET", unchecked((int)0x80070000) | code), "Ошибка"));
    }

    [Theory]
    [InlineData(5)] [InlineData(65)] [InlineData(32)] [InlineData(33)]
    public void AccessDeniedAndSharingViolationsAreNotPresentedAsANetworkOutage(int code)
    {
        var message = DataSourceAccessErrors.Describe(Database, new Win32Exception(code), "Ошибка");
        Assert.Contains("OrderDB.mdb", message);
        Assert.Contains(code is 5 or 65 ? "Проверьте права доступа" : "занята другой операцией", message);
        Assert.DoesNotContain("недоступна", message);
        Assert.DoesNotContain(Path.GetDirectoryName(Database)!, message);
    }

    [Fact]
    public void UnexpectedProviderErrorsAndUnconfirmedWritesKeepTheirOriginalMeaning()
    {
        const string fallback = "Сохранение не подтверждено. Обновите список (F5).";
        var disconnected = new IOException("SECRET", unchecked((int)0x80070040));
        var uncertain = new OrderWriteException(OrderWriteFailure.OutcomeUnknown, fallback, disconnected);
        Assert.Equal(fallback, DataSourceAccessErrors.Describe(Database, uncertain, fallback));
        Assert.True(uncertain.RequiresReload);
        Assert.Equal("Ошибка драйвера", DataSourceAccessErrors.Describe(Database, new IOException("занята", unchecked((int)0x80004005)), "Ошибка драйвера"));
        Assert.Equal("Нет доступа к базе данных «OrderDB.mdb». Проверьте права доступа.",
            DataSourceAccessErrors.Describe(Database, new UnauthorizedAccessException("SECRET"), "Ошибка"));
    }

    [Fact]
    public async Task MissingMdbIsHandledBeforeStartingAccessHostWithoutAssumingASharedFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "journal-absent-" + Guid.NewGuid().ToString("N"), "OrderDB.mdb");
        var error = await Assert.ThrowsAsync<OrderReadException>(() => new AccessOrderReader(path).ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DataSourceAccessErrors.Unavailable(path), error.Message);
        Assert.IsAssignableFrom<IOException>(error.InnerException);
    }
}
