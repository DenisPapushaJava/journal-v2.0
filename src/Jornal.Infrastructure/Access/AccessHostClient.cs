using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jornal.Core.Orders;
using Jornal.Core.Pricing;

namespace Jornal.Infrastructure.Access;

/// <summary>Читает и изменяет MDB через поставляемый x86-процесс, независимо от разрядности Office.</summary>
public sealed class AccessHostClient(string path, string? executable = null) : IOrderReader, IOrderBatchEditor, IManualOrderCreator, IPriceReader
{
    public static string DefaultExecutable => System.IO.Path.Combine(AppContext.BaseDirectory, "AccessHost", "Jornal.AccessHost.exe");
    public static bool IsBundled => OperatingSystem.IsWindows() && File.Exists(DefaultExecutable);

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
        var write = request.Operation is AccessHostOperation.SetColor or AccessHostOperation.SetDeliveryNumber or AccessHostOperation.CreateOrder;
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
            process.Start();
            output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            error = process.StandardError.ReadToEndAsync(deadline.Token);
            // После начала отправки записи любой обрыв считается неподтверждённым исходом.
            dispatched = true;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
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
                    if (request.Operation != AccessHostOperation.CreateOrder || response.Failure != OrderWriteFailure.NotSaved ||
                        response.Orders is not null || response.PriceTable is not null)
                        throw new InvalidDataException("Некорректный ответ об изменении прайса.");
                    try
                    {
                        var calculated = CuttingPriceCalculator.Calculate(request.Draft!.PriceRequest,
                            new(quote.TableName, true, [new(request.Draft.ThicknessMillimeters, quote.ThicknessColumn, quote.RublesPerMeter)]));
                        if (calculated != quote) throw new ArgumentException("Цена не соответствует параметрам.");
                    }
                    catch (Exception exception) when (exception is ArgumentException or PriceCalculationException)
                    { throw new InvalidDataException("Некорректная новая цена в ответе модуля Access.", exception); }
                    throw new OrderPriceChangedException(quote);
                }
                if (write) throw new OrderWriteException(response.Failure ?? OrderWriteFailure.OutcomeUnknown, response.Error);
                throw new OrderReadException(response.Error);
            }
            if (response.Failure is not null || response.ChangedQuote is not null)
                throw new InvalidDataException("Неполный ответ модуля Access.");
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
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or
            InvalidOperationException or JsonException or OperationCanceledException)
        {
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
