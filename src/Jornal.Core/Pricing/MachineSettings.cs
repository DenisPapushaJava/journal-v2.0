using Jornal.Core.Orders;

namespace Jornal.Core.Pricing;

public sealed record MachineSettings(string Key, string Name, string DisplayName, string? SpecificationProfileId, bool CanEditImport)
{
    public static string LegacyKey(string name) => "legacy:" + name;
    public static IReadOnlyList<MachineSettings> Build(IEnumerable<CuttingMachine> machines, IReadOnlyDictionary<string, string>? labels = null)
    {
        var rows = CuttingMachine.LegacyNames.Select(name => new MachineSettings(LegacyKey(name), name, name,
            name == "HansLaser" ? CuttingMachine.HansProfile : CuttingMachine.VintechProfile, false))
            .Concat(CuttingMachine.Snapshot(machines).Select(machine => new MachineSettings(machine.TableName, machine.Name, machine.Name, machine.SpecificationProfileId, true)))
            .Select(row => row with { DisplayName = labels?.GetValueOrDefault(row.Key) ?? row.Name }).ToArray();
        Validate(rows, allowDuplicateDisplayNames: true);
        if (labels is not null && labels.Keys.Any(key => rows.All(row => row.Key != key)))
            throw new ArgumentException("Название связано с неизвестным станком. Справочник не изменён.");
        return rows;
    }
    public static void Validate(IReadOnlyList<MachineSettings> rows, bool allowDuplicateDisplayNames = false)
    {
        if (rows.Count is < 4 or > 132 || rows.Any(row => row is null ||
            row.Key != (row.CanEditImport ? row.Key : LegacyKey(row.Name)) ||
            (row.CanEditImport ? !CuttingMachine.IsCustomTable(row.Key) || CuttingMachine.LegacyNames.Contains(row.Name) : !CuttingMachine.LegacyNames.Contains(row.Name)) ||
            !CuttingMachine.IsSupportedProfile(row.SpecificationProfileId) || !IsValidName(row.Name) || !IsValidName(row.DisplayName) || row.DisplayName.Equals("Прочие прайсы", StringComparison.OrdinalIgnoreCase) ||
            !row.CanEditImport && row.SpecificationProfileId != (row.Name == "HansLaser" ? CuttingMachine.HansProfile : CuttingMachine.VintechProfile)) ||
            CuttingMachine.LegacyNames.Any(name => rows.All(row => row.Key != LegacyKey(name))) ||
            rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count ||
            rows.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count ||
            !allowDuplicateDisplayNames && rows.Select(row => row.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
            throw new ArgumentException("Проверьте названия станков: они должны быть непустыми и уникальными, не длиннее 255 символов.");
    }
    public static bool IsValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name == name.Trim() && name.Length <= 255 && !name.Any(char.IsControl);
    public static IReadOnlyDictionary<string, string> Names(IReadOnlyList<MachineSettings> rows)
    {
        Validate(rows, allowDuplicateDisplayNames: true);
        var ambiguous = rows.GroupBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rows.ToDictionary(row => row.Name, row => ambiguous.Contains(row.DisplayName) ? $"{row.DisplayName} ({row.Name})" : row.DisplayName, StringComparer.Ordinal);
    }
}

public sealed record MachineSettingsChange(MachineSettings Expected, string DisplayName, string? SpecificationProfileId)
{
    [System.Text.Json.Serialization.JsonIgnore] public MachineSettings Values => Expected with { DisplayName = DisplayName, SpecificationProfileId = SpecificationProfileId };
    [System.Text.Json.Serialization.JsonIgnore] public bool HasChanges => Values != Expected;
    public MachineSettingsChange Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Expected);
        if (!MachineSettings.IsValidName(DisplayName) || !CuttingMachine.IsSupportedProfile(SpecificationProfileId) ||
            !Expected.CanEditImport && SpecificationProfileId != Expected.SpecificationProfileId)
            throw new ArgumentException("Проверьте название и импорт спецификаций станка.");
        return this;
    }
    public void Verify(IReadOnlyList<MachineSettings> current)
    {
        if (current.SingleOrDefault(row => row.Key == Expected.Key) != Expected) throw MachineCreation.Conflict();
        MachineSettings.Validate(current.Select(row => row.Key == Expected.Key ? Values : row).ToArray(), allowDuplicateDisplayNames: true);
        if (current.Any(row => row.Key != Expected.Key && row.DisplayName.Equals(DisplayName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Станок с таким названием уже существует. Укажите другое название.");
    }
}

public interface IMachineSettingsReader
{
    Task<IReadOnlyList<MachineSettings>> ReadMachineSettingsAsync(CancellationToken cancellationToken = default);
}
public interface IMachineSettingsEditor
{
    Task<MachineSettings> UpdateMachineSettingsAsync(MachineSettingsChange change, CancellationToken cancellationToken = default);
}
public interface IMachineLabelsInitializer
{
    Task PrepareMachineLabelsAsync(CancellationToken cancellationToken = default);
}
