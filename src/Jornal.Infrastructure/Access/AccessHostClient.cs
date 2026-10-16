using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

/// <summary>Читает и изменяет MDB через поставляемый x86-процесс, независимо от разрядности Office.</summary>
public sealed class AccessHostClient(string path, string? executable = null) : IOrderReader, IOrderBatchEditor, IManualOrderCreator, IPriceReader, IOrderMaintenance, IOrderDurationInitializer
{
    private readonly Action<Process> _start = static process => { process.Start(); };
    internal AccessHostClient(string path, string? executable, Action<Process> start) : this(path, executable) => _start = start;
    public static string DefaultExecutable => System.IO.Path.Combine(AppContext.BaseDirectory, "AccessHost", "Jornal.AccessHost.exe");
    public static bool IsBundled => OperatingSystem.IsWindows() && File.Exists(DefaultExecutable);

    public async Task PrepareCuttingTimeAsync(CancellationToken cancellationToken = default) =>
        _ = await SendAsync(new(1, AccessHostOperation.PrepareCuttingTime, System.IO.Path.GetFullPath(path)), cancellationToken);

    public async Task<OrderRecord?> ReadRecordAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        return (await SendAsync(new(1, AccessHostOperation.ReadOrder, System.IO.Path.GetFullPath(path), OrderId: id), cancellationToken)).Record;
    }

    public async Task<OrderRecord> UpdateAsync(OrderEditChange change, CancellationToken cancellationToken = default)
    {
        change.Validate();
        return (await SendAsync(new(1, AccessHostOperation.UpdateOrder, System.IO.Path.GetFullPath(path), Edit: change), cancellationToken)).Record!;
    }

    public async Task DeleteAsync(OrderRecord expected, CancellationToken cancellationToken = default)
    {
        expected.Validate();
        await SendAsync(new(1, AccessHostOperation.DeleteOrder, System.IO.Path.GetFullPath(path), Record: expected), cancellationToken);
    }

    public async Task<OrderRecord> RestoreAsync(OrderRecord archived, CancellationToken cancellationToken = default)
    {
        archived.Validate();
        return (await SendAsync(new(1, AccessHostOperation.RestoreOrder, System.IO.Path.GetFullPath(path), Record: archived), cancellationToken)).Record!;
    }

    public Task<PriceTable> ReadAsync(string tableName, CancellationToken cancellationToken = default) => ReadPriceTableAsync(tableName, cancellationToken);

    public async Task<OrderSummary> CreateAsync(ManualOrderDraft draft, CancellationToken cancellationToken = default)
    {
        draft.Validate();
        var response = await SendAsync(new(1, AccessHostOperation.CreateOrder, System.IO.Path.GetFullPath(path), Draft: draft), cancellationToken);
        return response.Orders![0];
    }

    public async Task<IReadOnlyList<OrderSummary>> ReadAsync(CancellationToken cancellationToken = default) =>
        (await SendAsync(new(1, AccessHostOperation.Read, path), cancellationToken)).Orders!;

    public async Task<PriceTable> ReadPriceTableAsync(string tableName, CancellationToken cancellationToken = default)
    {
        PriceTables.ValidateName(tableName);
        try
        {
            return (await SendAsync(new(1, AccessHostOperation.ReadPriceTable, System.IO.Path.GetFullPath(path), PriceTableName: tableName), cancellationToken)).PriceTable!;
        }
        catch (OrderReadException exception) { throw new PriceReadException(exception.Message, exception); }
    }

    public Task<IReadOnlyList<OrderSummary>> SetColorAsync(IReadOnlyCollection<OrderSummary> expected, int color,
        CancellationToken cancellationToken = default)
    {
        var change = OrderBatchChange.ForColor(expected, color);
        return SendOrdersAsync(new(1, AccessHostOperation.SetColor, path, change.Expected.ToArray(), color), cancellationToken);
    }

    public Task<IReadOnlyList<OrderSummary>> SetDeliveryNumberAsync(IReadOnlyCollection<OrderSummary> expected, string number,
        CancellationToken cancellationToken = default)
    {
        var change = OrderBatchChange.ForDeliveryNumber(expected, number);
        return SendOrdersAsync(new(1, AccessHostOperation.SetDeliveryNumber, path, change.Expected.ToArray(), DeliveryNumber: change.DeliveryNumber), cancellationToken);
    }

    private async Task<IReadOnlyList<OrderSummary>> SendOrdersAsync(AccessHostRequest request, CancellationToken cancellationToken) =>
        (await SendAsync(request, cancellationToken)).Orders!;

    private async Task<AccessHostResponse> SendAsync(AccessHostRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var write = request.Operation is AccessHostOperation.SetColor or AccessHostOperation.SetDeliveryNumber or AccessHostOperation.CreateOrder or
            AccessHostOperation.UpdateOrder or AccessHostOperation.DeleteOrder or AccessHostOperation.RestoreOrder or AccessHostOperation.PrepareCuttingTime;
        var dispatched = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(System.IO.Path.GetFullPath(executable ?? DefaultExecutable))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            }
        };
        Task<string>? output = null;
        Task<string>? error = null;
        try
        {
            _start(process);
            output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            error = process.StandardError.ReadToEndAsync(deadline.Token);
            // После начала отправки записи любой обрыв считается неподтверждённым исходом.
            dispatched = true;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            if (AccessHostBlockedException.IsExitCode(process.ExitCode))
                throw new AccessHostBlockedException(process.ExitCode);
            var response = JsonSerializer.Deserialize<AccessHostResponse>(await output);
            _ = await error;
            if (response is null || response.Protocol != 1 || process.ExitCode != 0)
                throw new InvalidDataException("Некорректный ответ модуля Access.");
            if (response.Error is not null)
            {
                if (response.Failure is { } failure && !Enum.IsDefined(failure))
                    throw new InvalidDataException("Неизвестный результат записи.");
                if (response.ChangedQuote is { } quote)
                {
                    if (request.Operation is not (AccessHostOperation.CreateOrder or AccessHostOperation.UpdateOrder) || response.Failure != OrderWriteFailure.NotSaved ||
                        response.Orders is not null || response.PriceTable is not null || response.Record is not null || response.MaintenanceCompleted is not null)
                        throw new InvalidDataException("Некорректный ответ об изменении прайса.");
                    try
                    {
                        var priceRequest = request.Draft?.PriceRequest ?? request.Edit!.PriceRequest;
                        var calculated = CuttingPriceCalculator.Calculate(priceRequest,
                            new(quote.TableName, true, [new(priceRequest.ThicknessMillimeters, quote.ThicknessColumn, quote.RublesPerMeter)]));
                        if (calculated != quote) throw new ArgumentException("Цена не соответствует параметрам.");
                    }
                    catch (Exception exception) when (exception is ArgumentException or PriceCalculationException)
                    { throw new InvalidDataException("Некорректная новая цена в ответе модуля Access.", exception); }
                    if (request.Operation == AccessHostOperation.UpdateOrder) throw new OrderEditPriceChangedException(quote);
                    throw new OrderPriceChangedException(quote);
                }
                if (write) throw new OrderWriteException(response.Failure ?? OrderWriteFailure.OutcomeUnknown, response.Error);
                throw new OrderReadException(response.Error);
            }
            if (response.Failure is not null || response.ChangedQuote is not null)
                throw new InvalidDataException("Неполный ответ модуля Access.");
            if (request.Operation == AccessHostOperation.PrepareCuttingTime)
            {
                if (response.CuttingTimePrepared != true || response.Orders is not null || response.Record is not null || response.PriceTable is not null || response.MaintenanceCompleted is not null)
                    throw new InvalidDataException("Подготовка хранения времени не подтверждена.");
                return response;
            }
            if (response.CuttingTimePrepared is not null) throw new InvalidDataException("Ответ подготовки передан для другой операции.");
            if (request.Operation is AccessHostOperation.ReadOrder or AccessHostOperation.UpdateOrder or AccessHostOperation.DeleteOrder or AccessHostOperation.RestoreOrder)
            {
                if (response.MaintenanceCompleted != true || response.Orders is not null || response.PriceTable is not null)
                    throw new InvalidDataException("Операция с полной записью не подтверждена.");
                try { response.Record?.Validate(); }
                catch (OrderWriteException exception) { throw new InvalidDataException("Некорректная полная запись в ответе хоста.", exception); }
                if (request.Operation == AccessHostOperation.ReadOrder && response.Record is { } record && record.Id != request.OrderId ||
                    request.Operation == AccessHostOperation.DeleteOrder && response.Record is not null ||
                    request.Operation == AccessHostOperation.RestoreOrder && response.Record != request.Record ||
                    request.Operation == AccessHostOperation.UpdateOrder && (response.Record is not { Price: >= 0 } updated || updated != request.Edit!.Values with { Price = updated.Price }))
                    throw new InvalidDataException("Поля изменённой/восстановленной заявки не соответствуют запросу.");
                if (request.Operation == AccessHostOperation.UpdateOrder && request.Edit?.ExpectedQuote is { } expectedQuote && response.Record!.Price != expectedQuote.StoredPrice)
                    throw new InvalidDataException("Подтверждённая цена не соответствует просмотренной.");
                return response;
            }
            if (response.Record is not null || response.MaintenanceCompleted is not null)
                throw new InvalidDataException("Ответ полной записи передан для другой операции.");
            if (request.Operation == AccessHostOperation.ReadPriceTable)
            {
                if (response.PriceTable is null || response.Orders is not null || response.PriceTable.Name != request.PriceTableName)
                    throw new InvalidDataException("Неполный ответ с прайсом модуля Access.");
                try { response.PriceTable.Validate(); }
                catch (ArgumentException exception) { throw new InvalidDataException("Некорректный прайс в ответе модуля Access.", exception); }
            }
            else if (response.Orders is null || response.PriceTable is not null)
                throw new InvalidDataException("Неполный ответ модуля Access.");
            if (request.Operation == AccessHostOperation.CreateOrder && (response.Orders!.Length != 1 ||
                response.Orders[0] is not { Id: > 0, StoredCuttingPrice: not null }))
                throw new InvalidDataException("Модуль Access не подтвердил новую заявку.");
            if (request.Operation == AccessHostOperation.CreateOrder && response.Orders![0] is { } created &&
                created != request.Draft!.ToOrder(created.Id, created.StoredCuttingPrice))
                throw new InvalidDataException("Поля созданной заявки не соответствуют запросу.");
            return response;
        }
        catch (OrderReadException) { throw; }
        catch (OrderWriteException) { throw; }
        catch (Exception exception) when (exception is AccessHostBlockedException or IOException or InvalidDataException or System.ComponentModel.Win32Exception or
            InvalidOperationException or JsonException or OperationCanceledException)
        {
            var blocked = exception as AccessHostBlockedException ??
                (exception is System.ComponentModel.Win32Exception native && !dispatched && AccessHostBlockedException.IsLaunchCode(native.NativeErrorCode)
                    ? new AccessHostBlockedException(native.NativeErrorCode, native) : null);
            // При блокировке DLL процесс может завершиться раньше, чем клиент запишет stdin.
            if (blocked is null)
            {
                try
                {
                    if (process.HasExited && AccessHostBlockedException.IsExitCode(process.ExitCode))
                        blocked = new AccessHostBlockedException(process.ExitCode, exception);
                }
                catch (Exception stateError) when (stateError is InvalidOperationException or System.ComponentModel.Win32Exception)
                { /* Состояние процесса недоступно; сохраняем исходный характер отказа. */ }
            }
            if (blocked is not null)
            {
                if (write)
                    throw new OrderWriteException(dispatched ? OrderWriteFailure.OutcomeUnknown : OrderWriteFailure.NotSaved,
                        blocked.Message + (dispatched ? " Сохранение не подтверждено. Изменения могли примениться; обновите список (F5) перед повтором."
                            : " Этот запрос на запись в MDB не отправлен."), blocked);
                throw new OrderReadException(blocked.Message, blocked);
            }
            if (write)
                throw new OrderWriteException(dispatched ? OrderWriteFailure.OutcomeUnknown : OrderWriteFailure.NotSaved,
                    dispatched ? "Модуль Access не подтвердил сохранение. Изменения могли примениться. Обновите список (F5) перед повтором."
                    : "Не удалось запустить модуль Access. Пакет не сохранён. Восстановите установку Journal.", exception);
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new OrderReadException("Не удалось получить данные от модуля Access. Проверьте доступ к MDB и восстановите установку Journal при необходимости.", exception);
        }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            // Наблюдаем и ошибки перенаправленных потоков при отмене/завершении процесса.
            if (output is not null) try { await output; } catch (Exception) { }
            if (error is not null) try { await error; } catch (Exception) { }
        }
    }
}
