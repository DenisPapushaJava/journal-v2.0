using System.Diagnostics;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Local;

// Отдельная локальная копия для разработки. Этот адаптер никогда не открывает MDB для записи.
public sealed class LocalJournalStore : IOrderReader, IManualOrderCreator, IOrderBatchEditor, IPriceReader
{
    public const string Extension = ".journal-test.json";
    private const long MaximumSize = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string _path;

    public LocalJournalStore(string path)
    {
        if (!IsLocalCopyPath(path)) throw new ArgumentException("Выберите файл локальной копии " + Extension + ".", nameof(path));
        _path = Path.GetFullPath(path);
    }

    public static bool IsLocalCopyPath(string? path) => path?.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) == true;

    public Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
        ReadAsync<IReadOnlyList<OrderSummary>>(book => book.Orders.OrderByDescending(row => row.OrderDate)
            .ThenByDescending(row => row.Id).ToArray(), cancellationToken);

    public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default)
    {
        PriceTables.ValidateName(tableName);
        return ReadAsync(book => book.Prices.Single(table => table.Name == tableName), cancellationToken);
    }

    public Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default) =>
        WriteAsync(book =>
        {
            var request = new CuttingPriceRequest(draft.CuttingType, draft.ThicknessMillimeters,
                draft.CutLengthMeters, draft.Coefficient, draft.MaterialId);
            var table = book.Prices.Single(price => price.Name == CuttingPriceCalculator.SelectTable(request));
            var quote = CuttingPriceCalculator.Calculate(request, table);
            ValidateDraft(draft);
            if (book.LastIssuedId == int.MaxValue)
                throw new OrderWriteException(OrderWriteFailure.NotSaved, "Закончился диапазон номеров локальных записей.");
            var order = new OrderSummary(book.LastIssuedId + 1, draft.OrderDate, draft.SpecificationNumber,
                draft.ManagerName, draft.CustomerName, draft.CuttingType, draft.MaterialName,
                draft.ThicknessMillimeters, quote.StoredPrice, OrderColors.None)
            {
                SheetSize = draft.SheetSize, SheetCount = draft.SheetCount, CutLengthMeters = draft.CutLengthMeters,
                WeightKilograms = draft.WeightKilograms, Coefficient = draft.Coefficient,
                MaterialId = draft.MaterialId, CuttingGas = draft.CuttingGas,
                StoredOrderDateTime = draft.OrderDate.ToDateTime(TimeOnly.MinValue),
                StoredCoefficient = draft.Coefficient.ToString(CultureInfo.InvariantCulture) + "|-1|-1" +
                    (draft.CuttingType == "HansLaser" ? "|" + draft.MaterialId + "|" + draft.CuttingGas : "")
            };
            return (book with { LastIssuedId = order.Id, Orders = [.. book.Orders, order] }, order);
        }, cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color,
        CancellationToken cancellationToken = default) => UpdateAsync(OrderBatchChange.ForColor(expected, color), cancellationToken);

    public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number,
        CancellationToken cancellationToken = default) => UpdateAsync(OrderBatchChange.ForDeliveryNumber(expected, number), cancellationToken);

    private Task<IReadOnlyList<OrderSummary>> UpdateAsync(OrderBatchChange change, CancellationToken cancellationToken) =>
        WriteAsync<IReadOnlyList<OrderSummary>>(book =>
        {
            var current = book.Orders.ToDictionary(row => row.Id);
            if (change.Expected.Any(expected => !current.TryGetValue(expected.Id, out var row) || !change.Matches(row, expected)))
                throw OrderWriteException.Conflict();
            var replacements = change.Expected.ToDictionary(row => row.Id, row => change.Apply(current[row.Id]));
            return (book with { Orders = book.Orders.Select(row => replacements.GetValueOrDefault(row.Id, row)).ToArray() },
                replacements.Values.ToArray());
        }, cancellationToken);

    public static Task InitializeAsync(string path, IReadOnlyList<OrderSummary> orders, IReadOnlyList<PriceTable> prices,
        string sourceName, string sourceSha256, CancellationToken cancellationToken = default) => Task.Run(() =>
        {
            var store = new LocalJournalStore(path);
            var book = new LocalJournalFile("Journal.LocalTestCopy", 1, sourceName, sourceSha256, DateTimeOffset.UtcNow,
                Math.Max(0, orders.Select(row => row.Id).DefaultIfEmpty().Max()), orders.ToArray(), prices.ToArray());
            Validate(book);
            Directory.CreateDirectory(Path.GetDirectoryName(store._path)!);
            using var gate = store.Acquire(cancellationToken);
            // Повторный импорт всегда создаёт новый файл, а не стирает сделанные пользователем изменения.
            store.Save(book, overwrite: false, cancellationToken);
        }, cancellationToken);

    private Task<T> ReadAsync<T>(Func<LocalJournalFile, T> read, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            using var gate = Acquire(cancellationToken);
            var result = read(Load());
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new OrderReadException("Локальная копия недоступна. Проверьте путь и права доступа.", exception);
        }
    }, cancellationToken);

    private Task<T> WriteAsync<T>(Func<LocalJournalFile, (LocalJournalFile Book, T Result)> update,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            using var gate = Acquire(cancellationToken);
            var (book, result) = update(Load());
            Save(book, overwrite: true, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OrderReadException)
        {
            throw new OrderWriteException(OrderWriteFailure.NotSaved,
                "Не удалось сохранить локальную копию. Ввод сохранён; проверьте файл и повторите действие.", exception);
        }
    }, cancellationToken);

    private LocalJournalFile Load()
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumSize) throw new OrderReadException("Локальная копия превышает допустимые 128 МиБ.");
        try
        {
            var book = JsonSerializer.Deserialize<LocalJournalFile>(stream, JsonOptions);
            if (book is null) throw new JsonException();
            Validate(book);
            return book;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new OrderReadException("Локальная копия повреждена или имеет неподдерживаемый формат. Файл не изменён.", exception);
        }
    }

    private void Save(LocalJournalFile book, bool overwrite, CancellationToken cancellationToken)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = OpenPrivateFile(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                JsonSerializer.Serialize(stream, book, JsonOptions);
                if (stream.Length > MaximumSize) throw new IOException("Локальная копия превышает допустимые 128 МиБ.");
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private FileStream Acquire(CancellationToken cancellationToken)
    {
        // Все экземпляры и процессы приложения используют один lock-файл. Удалять его после закрытия нельзя:
        // другой процесс может уже открыть тот же inode; перенос JSON выполняется под этой блокировкой.
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return OpenPrivateFile(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite); }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(2))
            {
                if (cancellationToken.WaitHandle.WaitOne(25)) cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static FileStream OpenPrivateFile(string path, FileMode mode, FileAccess access)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = FileShare.None };
        if (OperatingSystem.IsLinux()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static void Validate(LocalJournalFile book)
    {
        if (book.Format != "Journal.LocalTestCopy" || book.Version != 1 || string.IsNullOrEmpty(book.SourceName) ||
            book.SourceSha256 is not { Length: 64 } || !book.SourceSha256.All(Uri.IsHexDigit) ||
            book.Orders is null || book.Prices is null || book.LastIssuedId < 0 ||
            book.Orders.Any(row => row is null || row.Id > book.LastIssuedId || row.SpecificationNumber is null ||
                row.ManagerName is null || row.CustomerName is null || row.MaterialName is null || row.CuttingType is null) ||
            book.Orders.Select(row => row.Id).Distinct().Count() != book.Orders.Length ||
            book.Prices.Length != PriceTables.All.Count ||
            !book.Prices.Select(table => table?.Name).ToHashSet().SetEquals(PriceTables.All))
            throw new ArgumentException("Некорректный файл локальной копии.");
        foreach (var table in book.Prices) table.Validate();
    }

    private static void ValidateDraft(ManualOrderDraft draft)
    {
        var fields = new[] { draft.SpecificationNumber, draft.ManagerName, draft.CustomerName, draft.MaterialName,
            draft.SheetSize, draft.SheetCount, draft.WeightKilograms };
        if (fields.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 255 || value.Any(char.IsControl)) ||
            draft.MaterialName.Any(char.IsWhiteSpace) || draft.MaterialId is null || draft.CuttingGas is null ||
            draft.MaterialId.Length > 255 || draft.CuttingGas.Length > 255 ||
            draft.MaterialId.Any(c => char.IsControl(c) || c == '|') || draft.CuttingGas.Any(c => char.IsControl(c) || c == '|'))
            throw new OrderWriteException(OrderWriteFailure.NotSaved, "Проверьте обязательные поля и формат ID/газа заявки.");
    }
}

internal sealed record LocalJournalFile(string Format, int Version, string SourceName, string SourceSha256,
    DateTimeOffset ImportedAt, int LastIssuedId, OrderSummary[] Orders, PriceTable[] Prices);
