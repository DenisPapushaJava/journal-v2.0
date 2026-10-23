namespace Jornal.Infrastructure.Access;

// Повторяется только идемпотентная подготовка схемы, никогда запись заявки.
internal static class SharedSchemaPreparation
{
    internal static async Task EnsureAsync(Func<bool> ready, Action prepare, Func<Exception, bool> busy,
        CancellationToken token, Func<TimeSpan, CancellationToken, Task>? delay = null)
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
