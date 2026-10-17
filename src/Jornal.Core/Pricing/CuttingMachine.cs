using Jornal.Core.Orders;

namespace Jornal.Core.Pricing;

// Название сохраняется в прежнем Orders.Вид_резки. Переименование не предусмотрено:
// старый EXE должен иметь возможность связать то же имя с тем же прайсом.
public sealed record CuttingMachine(string Name, string TableName, string? SpecificationProfileId = null)
{
    public const string CatalogTable = "JournalMachines";
    public const string WaterjetProfile = "waterjet-awjc/v1";
    public static IReadOnlyList<string> LegacyNames { get; } = ["Лазер", "Плазма", "Газо-кислородная", "HansLaser"];
    public static bool IsCustomTable(string? name) => name is { Length: 45 } && name.StartsWith("PriceMachine_", StringComparison.Ordinal) &&
        name.AsSpan(13).ToString().All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name != Name.Trim() || Name.Length > 255 || Name.Any(char.IsControl) ||
            LegacyNames.Contains(Name, StringComparer.OrdinalIgnoreCase) || Name.Equals("Прочие прайсы", StringComparison.OrdinalIgnoreCase) || !IsCustomTable(TableName) ||
            SpecificationProfileId is not (null or WaterjetProfile))
            throw new ArgumentException("Проверьте название станка и привязку к прайсу/спецификациям.");
    }
    public static CuttingMachine[] Snapshot(IEnumerable<CuttingMachine> machines)
    {
        var result = machines.ToArray();
        if (result.Length > 128 || result.Any(machine => machine is null) ||
            result.Select(machine => machine.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length ||
            result.Select(machine => machine.TableName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
            throw new ArgumentException("Каталог станков некорректен или содержит дубликаты.");
        foreach (var machine in result) machine.Validate();
        return result;
    }
    public static CuttingMachine? Resolve(string name, IEnumerable<CuttingMachine> machines)
    {
        if (LegacyNames.Contains(name, StringComparer.Ordinal)) return null;
        return machines.SingleOrDefault(machine => machine.Name == name) ??
            throw new PriceCalculationException(PriceCalculationFailure.UnsupportedCuttingType, "Станок отсутствует в каталоге текущей базы.");
    }
    public static CuttingMachine? Verify(string name, CuttingMachine? declared, IEnumerable<CuttingMachine> machines)
    {
        var current = Resolve(name, machines);
        if (declared != current) throw new OrderWriteException(OrderWriteFailure.Conflict, "Привязка станка изменилась. Обновите каталог и проверьте заявку.");
        return current;
    }
}

public interface IMachineCatalogReader
{
    Task<IReadOnlyList<CuttingMachine>> ReadMachinesAsync(CancellationToken cancellationToken = default);
}
public interface IMachineCreator
{
    Task<CuttingMachine> CreateMachineAsync(MachineCreation creation, CancellationToken cancellationToken = default);
}
public sealed record MachineCreation(CuttingMachine Machine, IReadOnlyList<PriceRate> Rates, IReadOnlyList<CuttingMachine> ExpectedMachines)
{
    public MachineCreation Snapshot()
    {
        ArgumentNullException.ThrowIfNull(Machine); ArgumentNullException.ThrowIfNull(Rates); ArgumentNullException.ThrowIfNull(ExpectedMachines);
        Machine.Validate(); var expected = CuttingMachine.Snapshot(ExpectedMachines);
        if (expected.Length >= 128 || expected.Any(machine => machine.Name.Equals(Machine.Name, StringComparison.OrdinalIgnoreCase) || machine.TableName == Machine.TableName))
            throw new ArgumentException("Станок с таким названием уже существует или каталог заполнен.");
        var table = new PriceTable(Machine.TableName, true, Rates.ToArray()); table.Validate();
        if (Rates.Count is < 1 or > 254 || Rates.Any(rate => rate.ColumnName != PriceTableChange.ColumnName(rate.ThicknessMillimeters) || rate.RublesPerMeter is null or < 0))
            throw new ArgumentException("Добавьте толщины и целые неотрицательные цены. Не более 254 толщин.");
        return new(Machine, table.Rates, expected);
    }
    public static OrderWriteException Conflict() => new(OrderWriteFailure.Conflict, "Каталог станков изменился. Обновите его и проверьте ввод.");
}
