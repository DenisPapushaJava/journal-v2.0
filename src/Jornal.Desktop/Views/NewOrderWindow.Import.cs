using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Jornal.Desktop.Views;

public partial class NewOrderWindow
{
    private readonly CancellationTokenSource _importLifetime = new();
    private void OnCancelFormImport(object? sender, RoutedEventArgs e) => _viewModel?.CancelImportRead();
    private async void OnBrowseSpecification(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { IsBusy: false, IsSaved: false } model || !StorageProvider.CanOpen) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите спецификацию", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Спецификации HTML / PDF") { Patterns = ["*.html", "*.htm", "*.pdf", "*.HTML", "*.HTM", "*.PDF"] }]
            });
            if (!IsVisible || files.Count == 0) return;
            if (files[0].TryGetLocalPath() is not { } path)
            { model.ImportError = "Выберите файл в локальной или подключённой сетевой папке."; return; }
            await model.RequestImportAsync(path, _importLifetime.Token);
        }
        catch (OperationCanceledException) when (_importLifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { if (IsVisible) model.ImportError = "Не удалось открыть выбор спецификации. " + exception.Message; }
    }
}
