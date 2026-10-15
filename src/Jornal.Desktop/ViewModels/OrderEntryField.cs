using CommunityToolkit.Mvvm.ComponentModel;

namespace Jornal.Desktop.ViewModels;

public partial class OrderEntryField(string label, string hint = "", string value = "",
    IReadOnlyList<string>? choices = null) : ObservableObject
{
    public string Label { get; } = label;
    public string Hint { get; } = hint;
    public IReadOnlyList<string>? Choices { get; } = choices;
    public bool HasChoices => Choices is not null;
    [ObservableProperty] private string _value = value;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    public bool HasError => Error is not null;
}
