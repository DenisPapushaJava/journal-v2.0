using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics.CodeAnalysis;

namespace Jornal.Desktop.ViewModels;

public partial class OrderEntryField(string label, string value = "",
    IReadOnlyList<string>? choices = null) : ObservableObject
{
    public string Label { get; } = label;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasChoices)), NotifyPropertyChangedFor(nameof(HasSuggestions)), NotifyPropertyChangedFor(nameof(HasPlainInput))] private IReadOnlyList<string>? _choices = choices;
    public bool HasChoices => Choices is not null;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSuggestions)), NotifyPropertyChangedFor(nameof(HasPlainInput))] private IReadOnlyList<string>? _suggestions;
    public bool HasSuggestions => !HasChoices && Suggestions is { Count: > 0 };
    public bool HasPlainInput => !HasChoices && !HasSuggestions;
    public string? SuggestionText { get => Value; set => Value = value ?? ""; }
    private string? _importedValue;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImportHint)), NotifyPropertyChangedFor(nameof(InputHint))] private string? _importHint;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(InputHint))] private string? _importEvidence;
    public bool HasImportHint => _importedValue == "" && Value.Length == 0;
    public string? InputHint => ImportEvidence is { Length: > 0 } ? $"{ImportHint}\n{ImportEvidence}" : Value;
    public void ApplyImport(string value, string evidence)
    {
        NeedsReview = false;
        Value = value; _importedValue = value; ImportEvidence = evidence;
        ImportHint = value.Length == 0 ? "Не распознано" : "Из спецификации";
        Error = null;
    }
    private void OnValueChanged(string value)
    {
        NeedsReview = false;
        if (_importedValue is not null) ImportHint = value == _importedValue
            ? value.Length == 0 ? "Не распознано" : "Из спецификации" : "Изменено вручную";
    }
    [ObservableProperty] private bool _needsReview;
    [RelayCommand] private void ConfirmReview() { NeedsReview = false; Error = null; }
    private string _value = value ?? "";
    // TextBox.Text и ComboBox.SelectedItem могут прислать null до/при пересоздании привязки.
    [AllowNull]
    public string Value
    {
        get => _value;
        set
        {
            var text = value ?? "";
            if (_value == text) return;
            OnPropertyChanging(nameof(Value));
            _value = text;
            OnValueChanged(text);
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(HasImportHint));
            OnPropertyChanged(nameof(SuggestionText));
        }
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    public bool HasError => Error is not null;
}
