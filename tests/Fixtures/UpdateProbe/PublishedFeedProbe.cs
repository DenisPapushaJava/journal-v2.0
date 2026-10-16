using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Velopack;
using Velopack.Locators;

// Проверяет настоящий публичный feed без токена, установки или запуска скачанного Windows-кода.
internal static class PublishedFeedProbe
{
    public static async Task CheckAsync(string expectedVersion, string installedVersion = "2026.1.0-alpha.1")
    {
        var directory = Path.Combine(Path.GetTempPath(), "journal-published-feed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            VelopackAppUpdater Create(string installed) => new(preview => new UpdateManager(
                new JournalGithubSource(preview), new UpdateOptions { ExplicitChannel = "win-x64", AllowVersionDowngrade = false },
                new TestVelopackLocator("DenisPapusha.Journal", installed, directory)));
            var updates = new AppUpdatesViewModel(Create(installedVersion)) { IncludePreview = true };
            await updates.CheckCommand.ExecuteAsync(null);
            if (updates.HasError) throw new InvalidDataException(updates.Error);
            if (!updates.HasUpdate || updates.AvailableVersion != expectedVersion)
                throw new InvalidDataException($"Expected {expectedVersion}, got {updates.AvailableVersion}.");
            if (updates.NotificationText != $"Доступна новая версия Journal {expectedVersion}" || !updates.CanDownload || updates.IsDownloaded)
                throw new InvalidDataException("Bell notification or manual download state differs.");
            await updates.DownloadCommand.ExecuteAsync(null);
            if (updates.HasError || !updates.IsDownloaded || !updates.CanInstall || updates.RestartScheduled)
                throw new InvalidDataException(updates.Error ?? "The checked package was not ready for a manual installation.");
            var current = new AppUpdatesViewModel(Create(expectedVersion)) { IncludePreview = true };
            await current.CheckCommand.ExecuteAsync(null);
            if (current.HasError) throw new InvalidDataException(current.Error);
            if (current.HasUpdate)
                throw new InvalidDataException("The installed version was offered again.");
            Console.WriteLine($"Публичный win-x64 feed: {installedVersion} → {updates.AvailableVersion}; состояние колокольчика проверено через модель окна, настоящий пакет загружен и проверен адаптером, повторное предложение той же версии отсутствует. Установка не запускалась.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
