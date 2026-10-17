using Jornal.Core.Pricing;

namespace Jornal.Desktop.Import;

internal static class SpecificationMachineBinding
{
    internal static SpecificationImportPreview Bind(SpecificationImportPreview preview, IReadOnlyList<CuttingMachine> machines, string? current)
    {
        if (preview.Report.Profile != CuttingMachine.WaterjetProfile) return preview;
        var candidates = machines.Where(machine => machine.SpecificationProfileId == preview.Report.Profile).ToArray();
        var selected = candidates.SingleOrDefault(machine => machine.Name == current) ?? (candidates.Length == 1 ? candidates[0] : null);
        var fields = preview.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        fields["CuttingType"] = new(selected?.Name ?? "", selected is null ? "Выберите станок с профилем AWJC." : "Станок для спецификаций AWJC.");
        return preview with { Fields = fields };
    }
}
