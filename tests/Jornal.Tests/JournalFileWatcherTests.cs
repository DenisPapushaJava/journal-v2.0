using Jornal.Desktop.DataSources;
using Xunit;

namespace Jornal.Tests;

public sealed class JournalFileWatcherTests
{
    [Fact]
    public async Task DetectsAWriteToTheChosenFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "OrderDB.mdb");
        await File.WriteAllTextAsync(path, "test", TestContext.Current.CancellationToken);
        try
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = await JournalFileWatcher.SubscribeAsync(path, () => signal.TrySetResult(),
                () => signal.TrySetException(new IOException("Наблюдение остановлено")));
            await File.WriteAllTextAsync(path, "external update", TestContext.Current.CancellationToken);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AtomicReplacementIsDetected()
    {
        var root = Path.Combine(Path.GetTempPath(), "journal-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "test.journal-test.json");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);
        try
        {
            var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = await JournalFileWatcher.SubscribeAsync(path, () => signal.TrySetResult(),
                () => signal.TrySetException(new IOException()));
            var temporary = Path.Combine(root, "temporary.json");
            await File.WriteAllTextAsync(temporary, "new", TestContext.Current.CancellationToken);
            File.Move(temporary, path, overwrite: true);
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(root, true); }
    }
}
