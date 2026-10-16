using System.Text.Json;
using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;
using Velopack;
using Velopack.Sources;

if (args is ["--check-published-release", var expectedVersion])
{
    await PublishedFeedProbe.CheckAsync(expectedVersion);
    return;
}
if (args is ["--check-published-release", var targetVersion, var installedVersion])
{
    await PublishedFeedProbe.CheckAsync(targetVersion, installedVersion);
    return;
}

VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
// Только изолированная тестовая программа. Рабочее приложение не принимает источник обновлений из окружения.
var root = Environment.GetEnvironmentVariable("JOURNAL_UPDATE_PROBE_ROOT") ?? throw new InvalidOperationException("Test root is required.");
var updater = new VelopackAppUpdater(_ => new UpdateManager(new SimpleFileSource(new DirectoryInfo(Path.Combine(root, "releases"))),
    new UpdateOptions { ExplicitChannel = VelopackAppUpdater.RuntimeChannel, AllowVersionDowngrade = false }));
try
{
    if (!updater.IsInstalled) throw new InvalidOperationException("Probe was not installed.");
    var store = new JsonUpdateRestartStore(Path.Combine(root, "profile", "update-restart.json"));
    var version = updater.CurrentVersion;
    var preferences = new JsonAppUpdatePreferencesStore(Path.Combine(root, "profile", "update-settings.json"));
    var updates = new AppUpdatesViewModel(updater, preferencesStore: preferences);
    if (version == "2026.1.0")
    {
        updates.IncludePreview = true;
        File.WriteAllText(Path.Combine(root, "started-v1"), version);
        var update = await updater.CheckAsync(false, CancellationToken.None) ?? throw new InvalidOperationException("No update found.");
        await updater.DownloadAsync(update, new Progress<int>(), CancellationToken.None);
        store.Save(new(1, "existing-OrderDB.mdb", "Текст для письма\nПримечание <заказчику>", [42]));
        updater.ScheduleRestart(update);
        return;
    }
    if (!updates.IncludePreview) throw new InvalidDataException("Update channel was not restored after updating.");
    var draft = store.Take() ?? throw new InvalidOperationException("Result was not restored.");
    if (draft.ResultText != "Текст для письма\nПримечание <заказчику>" || !draft.ResultOrderIds.SequenceEqual([42]))
        throw new InvalidDataException("Draft differs.");
    File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { version, restored = true }));
}
catch (Exception exception)
{
    File.WriteAllText(Path.Combine(root, "failed.txt"), exception.ToString());
    Environment.ExitCode = 1;
}
