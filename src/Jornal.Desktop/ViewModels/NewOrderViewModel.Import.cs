using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Desktop.Import;

namespace Jornal.Desktop.ViewModels;

public partial class NewOrderViewModel
{
    private CancellationTokenSource? _importReadCancellation;
    public ISpecificationImportService ImportReader { get; set; } = new SpecificationImportService();
    public void CancelImportRead() => _importReadCancellation?.Cancel();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImport)), NotifyPropertyChangedFor(nameof(HasImportDiagnostics)), NotifyPropertyChangedFor(nameof(StorageDescription))]
    private SpecificationImportPreview? _importPreview;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasImportError))] private string? _importError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))] private bool _isImporting;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPendingImport)), NotifyPropertyChangedFor(nameof(PendingImportName))]
    private string? _pendingImportPath;
    public bool HasImport => ImportPreview is not null;
    public bool HasImportDiagnostics => !string.IsNullOrEmpty(ImportPreview?.Diagnostics);
    public bool HasImportError => !string.IsNullOrEmpty(ImportError);
    public bool HasPendingImport => PendingImportPath is not null;
    public string PendingImportName => Path.GetFileName(PendingImportPath) ?? "";
    private IReadOnlyDictionary<string, OrderEntryField> ImportFields => new Dictionary<string, OrderEntryField>
    {
        [nameof(OrderDate)] = OrderDate, [nameof(SpecificationNumber)] = SpecificationNumber, [nameof(CuttingType)] = CuttingType,
        [nameof(Material)] = Material, [nameof(Thickness)] = Thickness, [nameof(SheetSize)] = SheetSize, [nameof(SheetCount)] = SheetCount,
        [nameof(Weight)] = Weight, [nameof(CutLength)] = CutLength, [nameof(Coefficient)] = Coefficient,
        [nameof(MaterialId)] = MaterialId, [nameof(Gas)] = Gas
    };

    partial void OnIsImportingChanged(bool value)
    { SaveCommand.NotifyCanExecuteChanged(); CalculatePriceCommand.NotifyCanExecuteChanged(); }
    partial void OnPendingImportPathChanged(string? value)
    { SaveCommand.NotifyCanExecuteChanged(); CalculatePriceCommand.NotifyCanExecuteChanged(); }

    public void ApplyImport(SpecificationImportPreview preview)
    {
        if (IsSaved || SaveCommand.IsRunning || RequiresReload) return;
        _submitted = false;
        foreach (var (name, field) in ImportFields)
        {
            var value = preview.Fields[name]; field.ApplyImport(value.Value, value.Evidence);
        }
        HasValidationErrors = false; SaveError = null; ImportError = null;
        ImportPreview = preview;
        _quote = null; PriceText = "Не рассчитана"; PriceBreakdown = ""; PriceError = null;
    }

    public async Task RequestImportAsync(string path, CancellationToken cancellationToken)
    {
        if (IsBusy || IsSaved || RequiresReload) return;
        if (ImportFields.Any(pair => pair.Value.Value.Length > 0 && (pair.Key != nameof(Coefficient) || pair.Value.Value != "1")))
        { PendingImportPath = path; return; }
        await ImportFileAsync(path, cancellationToken);
    }

    [RelayCommand]
    private async Task ReplaceImportAsync(CancellationToken cancellationToken)
    {
        if (PendingImportPath is not { } path || IsBusy || IsSaved || RequiresReload) return;
        PendingImportPath = null;
        await ImportFileAsync(path, cancellationToken);
    }
    [RelayCommand] private void CancelImportReplacement() => PendingImportPath = null;

    private async Task ImportFileAsync(string path, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _importReadCancellation = cancellation;
        IsImporting = true; ImportError = null;
        try
        {
            var preview = await ImportReader.ReadAsync(path, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            ApplyImport(preview);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { ImportError = "Не удалось прочитать спецификацию. Прежний ввод сохранён. " + exception.Message; }
        finally { _importReadCancellation = null; IsImporting = false; }
    }
}
