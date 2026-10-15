using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class DatabaseSettingsWindow : Window
{
    private DatabaseSettingsViewModel? _model;
    public DatabaseSettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.Connected -= OnConnected;
            _model = DataContext as DatabaseSettingsViewModel;
            if (_model is not null) _model.Connected += OnConnected;
        };
    }
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_model?.UseDatabase == true) DatabasePathInput.Focus();
        else DatabaseModeButton.Focus();
    }
    protected override void OnClosed(EventArgs e)
    {
        if (_model is not null)
        {
            _model.ConnectCommand.Cancel();
            _model.Connected -= OnConnected;
        }
        base.OnClosed(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
    }
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    private void OnConnected(object? sender, EventArgs e) => Close(true);

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (_model is not { } model || !StorageProvider.CanOpen) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите базу OrderDB.mdb", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("База Access (*.mdb)") { Patterns = ["*.mdb", "*.MDB"] }]
            });
            if (files.Count > 0 && IsVisible)
            {
                var path = files[0].TryGetLocalPath();
                if (path is null) model.Error = "Выберите файл в локальной или подключённой сетевой папке.";
                else { model.UseDatabase = true; model.DatabasePath = path; }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            model.Error = "Не удалось открыть выбор файла. Можно ввести полный путь вручную.";
        }
    }
}
