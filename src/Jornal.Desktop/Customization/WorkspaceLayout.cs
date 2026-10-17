using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jornal.Desktop.Customization;

/// <summary>Четыре связанные зоны: слева, две по центру, справа. Старый профиль без этого поля сохраняет прежний вид.</summary>
public sealed record WorkspaceLayout
{
    public string[] Panels { get; init; } = ["result", "orders", "status", "clipboard"];
    public double[] ColumnWidths { get; init; } = [2, 1, 1];
    public double TopFraction { get; init; } = .43;
    public double Height { get; init; } = 272;

    public WorkspaceLayout Normalize()
    {
        var defaults = new WorkspaceLayout();
        return this with
        {
            Panels = (Panels ?? []).Where(defaults.Panels.Contains).Distinct()
                .Concat(defaults.Panels).Distinct().ToArray(),
            ColumnWidths = Enumerable.Range(0, 3).Select(index => ColumnWidths is { Length: 3 } &&
                double.IsFinite(ColumnWidths[index]) && ColumnWidths[index] is >= .01 and <= 10000
                    ? ColumnWidths[index] : defaults.ColumnWidths[index]).ToArray(),
            TopFraction = double.IsFinite(TopFraction) ? Math.Clamp(TopFraction, .2, .8) : defaults.TopFraction,
            Height = double.IsFinite(Height) ? Math.Clamp(Height, ScreenLayout.MinimumResultHeight, 480) : defaults.Height
        };
    }
}

internal sealed class WorkspaceLayoutConverter : JsonConverter<WorkspaceLayout>
{
    public override WorkspaceLayout? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try { return document.RootElement.Deserialize<WorkspaceLayout>()?.Normalize(); }
        catch (JsonException) { return new(); }
    }

    public override void Write(Utf8JsonWriter writer, WorkspaceLayout value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Normalize());
}
