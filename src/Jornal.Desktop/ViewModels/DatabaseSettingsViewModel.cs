using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Jornal.Desktop.ViewModels;

public partial class DatabaseSettingsViewModel(string? currentPath, Func<string?, CancellationToken, Task<string?>> connect) : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UseDemo))] private bool _useDatabase = currentPath is not null;
    [ObservableProperty] private string _databasePath = currentPath ?? "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    [ObservableProperty] private bool _isConnected;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool UseDemo { get => !UseDatabase; set => UseDatabase = !value; }
    public event EventHandler? Connected;
    partial void OnDatabasePathChanged(string value) => Error = null;
    partial void OnUseDatabaseChanged(bool value) => Error = null;

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        Error = null;
        string? path = null;
        if (UseDatabase)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(DatabasePath) || !Path.IsPathFullyQualified(DatabasePath.Trim()) ||
                    !string.Equals(Path.GetExtension(DatabasePath.Trim()), ".mdb", StringComparison.OrdinalIgnoreCase))
                { Error = "Укажите полный путь к существующему файлу .mdb или выберите его кнопкой «Обзор»."; return; }
                path = Path.GetFullPath(DatabasePath.Trim());
            }
            catch (ArgumentException) { Error = "Путь к файлу содержит недопустимые символы."; return; }
        }
        try
        {
            Error = await connect(path, cancellationToken);
            if (Error is not null || cancellationToken.IsCancellationRequested) return;
            IsConnected = true;
            Connected?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
