using Jornal.Core.Pricing;

namespace Jornal.Desktop.DataSources;

// Settings уже содержит рабочие ключи и импорт. Второе чтение каталога нужно
// только для старого источника или при отказе необязательных названий.
internal sealed record MachineCatalogSnapshot(IReadOnlyList<CuttingMachine> Machines,
    IReadOnlyList<MachineSettings> Settings, IReadOnlyDictionary<string, string> Names, Exception? NamesError)
{
    internal static async Task<MachineCatalogSnapshot> ReadAsync(IMachineCatalogReader source, CancellationToken token)
    {
        Exception? namesError = null;
        if (source is IMachineSettingsReader settingsReader)
        {
            try
            {
                var settings = (await settingsReader.ReadMachineSettingsAsync(token)).ToArray();
                token.ThrowIfCancellationRequested();
                var names = MachineSettings.Names(settings);
                var machines = CuttingMachine.Snapshot(settings.Where(row => row.CanEditImport)
                    .Select(row => new CuttingMachine(row.Name, row.Key, row.SpecificationProfileId)));
                return new(machines, settings, names, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { namesError = exception; }
        }
        var canonical = CuttingMachine.Snapshot(await source.ReadMachinesAsync(token));
        token.ThrowIfCancellationRequested();
        var fallback = MachineSettings.Build(canonical);
        return new(canonical, namesError is null ? fallback : [], MachineSettings.Names(fallback), namesError);
    }
}
