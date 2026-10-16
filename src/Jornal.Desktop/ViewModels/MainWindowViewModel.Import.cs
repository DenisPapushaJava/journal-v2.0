using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Desktop.Import;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSpecificationOverlay)), NotifyPropertyChangedFor(nameof(SpecificationOverlayTitle))]
    private bool _isDraggingSpecification;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSpecificationOverlay)), NotifyPropertyChangedFor(nameof(SpecificationOverlayTitle))]
    private bool _isImportingSpecification;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SpecificationOverlayTitle))] private string? _specificationDropError;
    [ObservableProperty] private string _specificationOverlayDescription = "HTML БЛОК/Wintech или PDF HansLaser · новая заявка после проверки";
    public bool ShowSpecificationOverlay => IsDraggingSpecification || IsImportingSpecification;
    public string SpecificationOverlayTitle => IsImportingSpecification ? "Читаем спецификацию…" :
        SpecificationDropError is null ? "Отпустите спецификацию здесь" : "Этот файл пока не принимается";
    public bool CanImportSpecification => CanCreateOrder && _pendingCreationForm is null;
    public string? ImportAvailabilityError => _pendingCreationForm is not null || RequiresReload
        ? "Сначала обновите список (F5) и проверьте предыдущее добавление. «Новая заявка» восстановит сохранённый ввод."
        : orderCreator is null ? "Источник доступен только для чтения. Для добавления подключите редактируемый журнал."
        : !CanUseJournal ? "Дождитесь завершения текущего действия или завершите настройку экрана." : null;
    partial void OnIsImportingSpecificationChanged(bool value) => NotifyJournalAvailability();

    public async Task<NewOrderViewModel?> ReadSpecificationAsync(IReadOnlyList<string> paths, ISpecificationImportService reader,
        CancellationToken cancellationToken)
    {
        IsDraggingSpecification = false;
        if (IsImportingSpecification) return null;
        if ((ImportAvailabilityError ?? SpecificationImportService.CheckFiles(paths)) is { } error)
        { ErrorMessage = error; return null; }
        SpecificationOverlayDescription = Path.GetFileName(paths[0]);
        IsImportingSpecification = true;
        SpecificationImportPreview? preview = null;
        try { preview = await reader.ReadAsync(paths[0], cancellationToken); cancellationToken.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { ErrorMessage = "Не удалось прочитать спецификацию. " + exception.Message; }
        finally { IsImportingSpecification = false; }
        if (preview is null || cancellationToken.IsCancellationRequested || CreateOrderForm(forImport: true) is not { } form) return null;
        form.ApplyImport(preview);
        return form;
    }
}
