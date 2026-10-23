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
                // Другой компьютер мог закончить подготовку после нашей проверки.
                try { if (ready()) return; }
                catch (Exception check) when (busy(check)) { }
                if (attempt >= 5) throw;
                await delay(TimeSpan.FromMilliseconds(Math.Min(2000, 250 * (1 << attempt))), token).ConfigureAwait(false);
            }
        }
    }
}
