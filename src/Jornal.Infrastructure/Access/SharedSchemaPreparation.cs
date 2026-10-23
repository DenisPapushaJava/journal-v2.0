namespace Jornal.Infrastructure.Access;

// Повторяется только идемпотентная подготовка схемы, никогда запись заявки.
internal static class SharedSchemaPreparation
{
    // Jet разделяет состояние движка между соединениями процесса. Не открываем
    // второй каталог одновременно с DDL первого; другие процессы защищает
    // исключительное подключение MDB и прежний ограниченный retry.
    private static readonly SemaphoreSlim PreparationGate = new(1, 1);

    internal static async Task EnsureAsync(Func<bool> ready, Action prepare, Func<Exception, bool> busy,
        CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        await PreparationGate.WaitAsync(token).ConfigureAwait(false);
        try { await EnsureCoreAsync(ready, prepare, busy, token, delay).ConfigureAwait(false); }
        finally { PreparationGate.Release(); }
    }

    private static async Task EnsureCoreAsync(Func<bool> ready, Action prepare, Func<Exception, bool> busy,
        CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay)
    {
        delay ??= Task.Delay;
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (ready()) return;
                prepare();
                if (!ready()) throw new InvalidOperationException("Подготовка структуры не подтверждена.");
                return;
            }
            catch (Exception exception) when (busy(exception))
            {
                // После блокировки сначала даём другому клиенту закончить DDL.
                // Следующая итерация проверит готовое; немедленный probe может
                // попасть в ещё изменяемый каталог Jet и не даёт полезной паузы.
                if (attempt >= 5) throw;
                await delay(TimeSpan.FromMilliseconds(Math.Min(2000, 250 * (1 << attempt))), token).ConfigureAwait(false);
            }
        }
    }
}
