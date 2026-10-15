namespace Jornal.Desktop.Customization;

public sealed record ColumnPreference(string Id, int Position, bool Visible, bool Star, double Width);

public sealed record ScreenLayout
{
    public bool ResultAboveCommands { get; init; } = true;
    public double ResultHeight { get; init; } = 152;
    public string[] PanelOrder { get; init; } = ["orders", "clipboard", "status"];
    public Dictionary<string, double> PanelWidths { get; init; } = new()
        { ["orders"] = 380, ["clipboard"] = 548, ["status"] = 592 };
    public ColumnPreference[] Columns { get; init; } = [];

    public ScreenLayout Normalize()
    {
        var defaults = new ScreenLayout();
        var order = (PanelOrder ?? []).Where(defaults.PanelOrder.Contains).Distinct()
            .Concat(defaults.PanelOrder).Distinct().ToArray();
        var widths = defaults.PanelWidths.ToDictionary(pair => pair.Key, pair =>
            PanelWidths is not null && PanelWidths.TryGetValue(pair.Key, out var width) &&
            double.IsFinite(width) && width is >= 1 and <= 10000 ? width : pair.Value);
        var columns = (Columns ?? []).Where(column => column is not null &&
            !string.IsNullOrWhiteSpace(column.Id) && column.Id.Length <= 64 &&
            column.Position is >= 1 and <= 100 && double.IsFinite(column.Width) &&
            column.Width is > 0 and <= 5000).DistinctBy(column => column.Id).Take(64).ToArray();
        return this with
        {
            ResultHeight = double.IsFinite(ResultHeight) ? Math.Clamp(ResultHeight, 120, 280) : 152,
            PanelOrder = order, PanelWidths = widths, Columns = columns
        };
    }
}

public sealed record UiPreferences
{
    public int Version { get; init; } = 1;
    public bool DarkTheme { get; init; }
    public ScreenLayout Layout { get; init; } = new();
}

public sealed record PreferencesLoadResult(UiPreferences? Preferences, string? Error = null);

public interface IUiPreferencesStore
{
    PreferencesLoadResult Load();
    void Save(UiPreferences preferences);
}

// Окна в тестах и дизайнере не обращаются к настройкам пользователя ОС.
public sealed class MemoryUiPreferencesStore : IUiPreferencesStore
{
    public UiPreferences? Value { get; private set; }
    public PreferencesLoadResult Load() => new(Value);
    public void Save(UiPreferences preferences) => Value = preferences;
}
