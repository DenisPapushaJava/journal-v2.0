using Jornal.Desktop.Updates;
using Velopack;
using Velopack.Locators;

// Проверяет настоящий публичный feed без токена, установки или запуска скачанного Windows-кода.
internal static class PublishedFeedProbe
{
    public static async Task CheckAsync(string expectedVersion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-published-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            VelopackAppUpdater Create(string installed) => new(preview => new UpdateManager(
                new JournalGithubSource(preview), new UpdateOptions { ExplicitChannel = "win-x64", AllowVersionDowngrade = false },
                new TestVelopackLocator("DenisPapusha.Journal", installed, directory)));
            var updater = Create("2026.1.0-alpha.1");
            var update = await updater.CheckAsync(true, CancellationToken.None) ?? throw new InvalidDataException("Public preview update was not found.");
            if (update.Version != expectedVersion) throw new InvalidDataException($"Expected {expectedVersion}, got {update.Version}.");
            await updater.DownloadAsync(update, new Progress<int>(), CancellationToken.None);
            if (await Create(expectedVersion).CheckAsync(true, CancellationToken.None) is not null)
                throw new InvalidDataException("The installed version was offered again.");
            Console.WriteLine($"Публичный win-x64 feed: alpha.1 → {update.Version}; настоящий пакет загружен и проверен адаптером, повторное предложение той же версии отсутствует. Установка не запускалась.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
