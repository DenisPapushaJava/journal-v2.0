using System.Data.OleDb;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Xunit;

namespace Jornal.Access.Tests;

[SupportedOSPlatform("windows")]
public sealed class NativeAccessTests(ITestOutputHelper output)
{
    [Fact]
    public void DiagnoseProviderInitialization()
    {
        using var database = Database.Create();
        foreach (var directoryName in new[] { "Journal copy & test ", "Journal's copy ", "Журнал копия ", "Journal's копия & тест " })
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), directoryName + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "OrderDB.mdb");
            File.Copy(database.Path, path);
            try
            {
                foreach (var mode in new object[] { "Read", 19 })
                foreach (var raw in new[] { false, true })
                {
                    var provider = OleDbOrderReader.FindProvider()!;
                    var builder = new OleDbConnectionStringBuilder { Provider = provider, DataSource = path };
                    builder["Mode"] = mode;
                    builder["OLE DB Services"] = -2;
                    var text = raw ? $"Provider={provider};Data Source={path};Mode={mode};OLE DB Services=-2" : builder.ConnectionString;
                    try
                    {
                        using var connection = new OleDbConnection(text);
                        connection.Open();
                        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM [Orders]";
                        output.WriteLine($"PASS {text}: {command.ExecuteScalar()}");
                    }
                    catch (Exception exception) { output.WriteLine($"FAIL {text}: {exception.Message}"); }
                }
            }
            finally { OleDbConnection.ReleaseObjectPool(); Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public async Task ColorsPersistAcrossConnectionsWithoutChangingAnyOtherFieldOrSchema()
    {
        using var database = Database.Create();
        var before = await database.Read();
        var schema = database.Schema();
        var editor = new OleDbOrderBatchEditor(database.Path);
        var changed = await editor.SetColorAsync(before, OrderColors.Ready, TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(row => row with { Color = OrderColors.Ready }), changed);
        Assert.Equal(changed, await database.Read());
        Assert.Equal(changed, await editor.SetColorAsync(changed, OrderColors.Ready, TestContext.Current.CancellationToken));
        changed = await editor.SetColorAsync([changed[0], changed[1], changed[0]], OrderColors.None, TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(row => row with { Color = OrderColors.None }), changed);
        Assert.Equal(changed, await database.Read());
        Assert.Equal(schema, database.Schema());
        Assert.Equal(87, database.Scalar("SELECT [1,5] FROM [PriceP]"));
    }

    [Fact]
    public async Task DeliveryIsParameterizedUnicodeTextAndClearsToNull()
    {
        using var database = Database.Create();
        var before = await database.Read();
        var editor = new OleDbOrderBatchEditor(database.Path);
        const string number = "БЛЦБ-'А' %_*? []; не SQL";
        var changed = await editor.SetDeliveryNumberAsync(before, number, TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(row => row with { DeliveryNumber = number }), changed);
        Assert.Equal(changed, await database.Read());
        changed = await editor.SetDeliveryNumberAsync(changed, "", TestContext.Current.CancellationToken);
        Assert.Equal(before.Select(row => row with { DeliveryNumber = null }), changed);
        Assert.Equal(changed, await database.Read());
        Assert.Equal(1234.5678m, changed.Single(row => row.Id == 42).StoredCuttingPrice);
        Assert.Equal(changed, await editor.SetDeliveryNumberAsync(changed, "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LateConflictRollsBackEarlierRowsInTheSameBatch()
    {
        using var database = Database.Create();
        var original = await database.Read();
        await new OleDbOrderBatchEditor(database.Path).SetColorAsync([original.Single(row => row.Id == 43)], OrderColors.Work, TestContext.Current.CancellationToken);
        var expected = await database.Read();
        var exception = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderBatchEditor(database.Path)
            .SetColorAsync(original.Reverse().ToArray(), OrderColors.Ready, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, exception.Failure);
        Assert.Equal(expected, await database.Read());
    }

    [Fact]
    public async Task StatementFailureAfterTheFirstUpdateRollsBackTheWholeBatch()
    {
        using var database = Database.Create();
        database.Execute("CREATE UNIQUE INDEX [UniqueDelivery] ON [Orders] ([БЛЦБ])");
        var before = await database.Read();
        var exception = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderBatchEditor(database.Path)
            .SetDeliveryNumberAsync(before, "Один номер", TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.NotSaved, exception.Failure);
        Assert.Equal(before, await database.Read());
    }

    [Fact]
    public async Task DeletedRowRollsBackTheRestOfTheBatch()
    {
        using var database = Database.Create();
        var before = await database.Read();
        database.Execute("DELETE FROM [Orders] WHERE [Код]=43");
        var exception = await Assert.ThrowsAsync<OrderWriteException>(() => new OleDbOrderBatchEditor(database.Path)
            .SetColorAsync(before, OrderColors.Ready, TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, exception.Failure);
        Assert.Equal(before.Where(row => row.Id != 43), await database.Read());
    }

    [Theory]
    [InlineData("блцб-000042")]
    [InlineData("БЛЦБ-000042 ")]
    public async Task TextConflictUsesBinaryComparisonIncludingCaseAndSpaces(string concurrentValue)
    {
        using var database = Database.Create();
        var before = await database.Read();
        var row = before.Single(row => row.Id == 42);
        var editor = new OleDbOrderBatchEditor(database.Path);
        await editor.SetDeliveryNumberAsync([row], concurrentValue, TestContext.Current.CancellationToken);
        Assert.Equal(concurrentValue, (await database.Read()).Single(row => row.Id == 42).DeliveryNumber);
        var exception = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SetDeliveryNumberAsync([row], "Новый номер", TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, exception.Failure);
        Assert.Equal(concurrentValue, (await database.Read()).Single(row => row.Id == 42).DeliveryNumber);
    }

    [Fact]
    public async Task NullAndEmptyOriginalTextAreDifferentButUnrelatedChangesDoNotConflict()
    {
        using var database = Database.Create();
        var before = await database.Read();
        var editor = new OleDbOrderBatchEditor(database.Path);
        var empty = before.Single(row => row.Id == 43);
        var exception = await Assert.ThrowsAsync<OrderWriteException>(() => editor.SetDeliveryNumberAsync(
            [empty with { DeliveryNumber = "" }], "Новый", TestContext.Current.CancellationToken));
        Assert.Equal(OrderWriteFailure.Conflict, exception.Failure);
        await editor.SetColorAsync(before, OrderColors.Work, TestContext.Current.CancellationToken);
        var changed = await editor.SetDeliveryNumberAsync(before, "Новый", TestContext.Current.CancellationToken);
        Assert.All(changed, row => Assert.Equal(OrderColors.Work, row.Color));
        Assert.All(changed, row => Assert.Equal("Новый", row.DeliveryNumber));
        Assert.Equal(changed, await database.Read());
    }

    [Fact]
    public async Task CompetingConnectionsCannotPartiallyOverwriteAnUncommittedChange()
    {
        using var database = Database.Create();
        var before = await database.Read();
        using var other = database.Connection();
        using var transaction = other.BeginTransaction();
        using var command = other.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE [Orders] SET [Color]={OrderColors.Work} WHERE [Код]=42";
        command.ExecuteNonQuery();
        var saving = new OleDbOrderBatchEditor(database.Path).SetColorAsync(before, OrderColors.Ready, TestContext.Current.CancellationToken);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        transaction.Commit();
        var exception = await Assert.ThrowsAsync<OrderWriteException>(() => saving);
        Assert.Contains(exception.Failure, new[] { OrderWriteFailure.Conflict, OrderWriteFailure.NotSaved });
        Assert.Equal(before.Select(row => row.Id == 42 ? row with { Color = OrderColors.Work } : row), await database.Read());
    }

    [Fact]
    public async Task ValidationAndPreCancelledWritesDoNotOpenOrChangeTheDatabase()
    {
        using var database = Database.Create();
        var before = await database.Read();
        var bytes = SHA256.HashData(File.ReadAllBytes(database.Path));
        var editor = new OleDbOrderBatchEditor(database.Path);
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetDeliveryNumberAsync(before, new string('1', 256), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => editor.SetColorAsync(before, OrderColors.Ready, cancellation.Token));
        Assert.Equal(bytes, SHA256.HashData(File.ReadAllBytes(database.Path)));
        Assert.Equal(before, await database.Read());
    }

    private sealed class Database : IDisposable
    {
        private readonly string _directory;
        private readonly string _fixture;
        private readonly byte[] _fixtureHash;
        public string Path { get; }
        private Database(string fixture)
        {
            _fixture = fixture;
            _fixtureHash = SHA256.HashData(File.ReadAllBytes(fixture));
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Journal copy & test " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "OrderDB.mdb");
            File.Copy(fixture, Path);
        }
        public static Database Create()
        {
            var fixture = Environment.GetEnvironmentVariable("JOURNAL_SYNTHETIC_MDB");
            var available = OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(fixture);
            if (Environment.GetEnvironmentVariable("JOURNAL_REQUIRE_NATIVE_TESTS") == "1") Assert.True(available, "Обязательная MDB не подготовлена.");
            Assert.SkipUnless(available, "Нативные проверки требуют Windows и искусственную MDB.");
            return new(fixture!);
        }
        public Task<IReadOnlyList<OrderSummary>> Read() => new AccessOrderReader(Path).ReadAsync(TestContext.Current.CancellationToken);
        public OleDbConnection Connection()
        {
            var builder = new OleDbConnectionStringBuilder { Provider = OleDbOrderReader.FindProvider() ?? throw new InvalidOperationException("Не найден Jet/ACE."), DataSource = Path };
            builder["Mode"] = 19; builder["OLE DB Services"] = -2;
            var connection = new OleDbConnection(builder.ConnectionString); connection.Open(); return connection;
        }
        public void Execute(string sql)
        {
            using var connection = Connection(); using var command = connection.CreateCommand();
            command.CommandText = sql; command.ExecuteNonQuery();
        }
        public object Scalar(string sql)
        {
            using var connection = Connection(); using var command = connection.CreateCommand();
            command.CommandText = sql; return command.ExecuteScalar()!;
        }
        public string[] Schema()
        {
            using var connection = Connection();
            var table = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, [null, null, "Orders", null])!;
            return table.Rows.Cast<System.Data.DataRow>().Select(row => string.Join('|',
                new[] { "COLUMN_NAME", "DATA_TYPE", "ORDINAL_POSITION", "IS_NULLABLE", "COLUMN_DEFAULT", "CHARACTER_MAXIMUM_LENGTH" }.Select(name => row[name])))
                .Order(StringComparer.Ordinal).ToArray();
        }
        public void Dispose()
        {
            OleDbConnection.ReleaseObjectPool();
            Assert.Equal(_fixtureHash, SHA256.HashData(File.ReadAllBytes(_fixture)));
            Directory.Delete(_directory, recursive: true);
        }
    }
}
