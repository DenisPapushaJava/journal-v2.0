using CommunityToolkit.Mvvm.ComponentModel;

namespace Jornal.Desktop.ViewModels;

public partial class OrderEntryField(string label, string value = "",
    IReadOnlyList<string>? choices = null) : ObservableObject
{
    public string Label { get; } = label;
    public IReadOnlyList<string>? Choices { get; } = choices;
    public bool HasChoices => Choices is not null;
    private string? _importedValue;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImportHint)), NotifyPropertyChangedFor(nameof(InputHint))] private string? _importHint;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InputHint))] private string? _importEvidence;
    public bool HasImportHint => _importedValue == "" && Value.Length == 0;
    public string? InputHint => ImportEvidence is { Length: > 0 } ? $"{ImportHint}\n{ImportEvidence}" : Value;
    public void ApplyImport(string value, string evidence)
    {
        Value = value; _importedValue = value; ImportEvidence = evidence;
        ImportHint = value.Length == 0 ? "Не распознано" : "Из спецификации";
        Error = null;
    }
    partial void OnValueChanged(string value)
    {
        if (_importedValue is not null) ImportHint = value == _importedValue
            ? value.Length == 0 ? "Не распознано" : "Из спецификации" : "Изменено вручную";
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImportHint))] private string _value = value;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    public bool HasError => Error is not null;
}
