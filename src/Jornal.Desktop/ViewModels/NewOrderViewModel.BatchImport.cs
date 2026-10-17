using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Core.Orders;
using Jornal.Desktop.Import;
using Jornal.Infrastructure.Diagnostics;
using Jornal.Parsing.Documents;

namespace Jornal.Desktop.ViewModels;

public partial class NewOrderViewModel
{
    private bool HasCurrentParameters => Fields.Any(entry => entry != Manager && entry != Customer && entry.Value.Length > 0 && (entry != Coefficient || entry.Value != "1"));
    private readonly SemaphoreSlim _batchReads = new(2);
    public ObservableCollection<BatchSpecificationItem> BatchItems { get; } = [];
    public IReadOnlyList<OrderSummary> ExistingOrders { get; set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Heading)), NotifyPropertyChangedFor(nameof(WindowTitle)), NotifyPropertyChangedFor(nameof(SaveLabel))]
    private bool _isBatchMode;
    public bool IsReadingSpecifications => IsImporting || IsBatchReading;
    public bool IsBatchReading => BatchItems.Any(item => item.IsReading);
    public int IncludedCount => BatchItems.Count(item => item.IsIncluded);
    public string BatchSummary => $"Готово: {BatchItems.Count(item => item.IsIncluded && item.IsReady)} из {IncludedCount}";
    public bool CanEditBatch => !SaveCommand.IsRunning && !CalculatePriceCommand.IsRunning && !IsSaved && !RequiresReload;
    public bool CanAddFiles => CanEditBatch && !IsImporting && !HasPendingImport;
    private bool CanSaveBatch => _creator is IManualOrderBatchCreator && IncludedCount > 0 && BatchItems.Where(item => item.IsIncluded).All(item => item.IsReady && !item.Form.Material.NeedsReview);

    internal void NotifyBatchState()
    {
        foreach (var name in new[] { nameof(IsBusy), nameof(IsBatchReading), nameof(IsReadingSpecifications), nameof(IncludedCount), nameof(BatchSummary), nameof(SaveLabel), nameof(CanAddFiles), nameof(CanEditBatch) })
            OnPropertyChanged(name);
        foreach (var item in BatchItems) item.NotifyOwnerState();
        SaveCommand.NotifyCanExecuteChanged(); CalculatePriceCommand.NotifyCanExecuteChanged();
    }

    internal void SyncBatchSharedFields()
    {
        foreach (var item in BatchItems)
        { item.Form.Manager.Value = Manager.Value; item.Form.Customer.Value = Customer.Value; }
        if (HasValidationErrors && BatchItems.FirstOrDefault(item => item.IsIncluded && item.HasParameters) is { } first)
        {
            Manager.Error = first.Form.Manager.Error; Customer.Error = first.Form.Customer.Error;
            HasValidationErrors = Manager.HasError || Customer.HasError;
        }
        UpdateDuplicateWarnings();
    }

    public async Task ImportFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken, bool appendToCurrent = false)
    {
        if (!CanAddFiles) return;
        if (IsBatchMode || paths.Count > 1 || appendToCurrent && HasCurrentParameters) await AddBatchFilesAsync(paths, cancellationToken);
        else if (paths.Count == 1) await RequestImportAsync(paths[0], cancellationToken);
    }

    public async Task AddBatchFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        if (!CanAddFiles || paths.Count == 0) return;
        if (_creator is not IManualOrderBatchCreator)
        { ImportError = "Этот источник не поддерживает пакетное добавление."; return; }
        var preserveCurrent = !IsBatchMode && HasCurrentParameters;
        if (BatchItems.Count + paths.Count + (preserveCurrent ? 1 : 0) > ManualOrderBatch.MaximumCount)
        { ImportError = $"В одном пакете — до {ManualOrderBatch.MaximumCount} файлов."; return; }
        ImportError = null;
        if (!IsBatchMode)
        {
            IsBatchMode = true;
            if (preserveCurrent)
            {
                var form = NewBatchForm();
                if (ImportPreview is { } previous) form.ApplyImport(previous, _importSourcePath);
                for (var index = 0; index < Fields.Count; index++) form.Fields[index].Value = Fields[index].Value;
                form.Material.NeedsReview = Material.NeedsReview;
                form._quote = _quote; form.PriceText = PriceText; form.PriceBreakdown = PriceBreakdown;
                var current = new BatchSpecificationItem(this, form, ImportPreview?.FileName ?? "Текущая заявка", _importSourcePath) { HasParameters = true, IsReading = false };
                BatchItems.Add(current);
            }
        }
        var pending = new List<BatchSpecificationItem>();
        foreach (var path in paths)
        {
            var item = new BatchSpecificationItem(this, NewBatchForm(), string.IsNullOrEmpty(path) ? "Папка или недоступный файл" : System.IO.Path.GetFileName(path), path);
            var duplicate = BatchItems.Any(existing => existing.Path is { Length: > 0 } previous &&
                string.Equals(previous, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            BatchItems.Add(item);
            if (duplicate) { item.Failure = "Этот файл уже есть в пакете."; item.IsReading = false; }
            else pending.Add(item);
        }
        NotifyBatchState();
        await Task.WhenAll(pending.Select(item => ReadBatchItemAsync(item, cancellationToken)));
    }

    private NewOrderViewModel NewBatchForm()
    {
        var form = new NewOrderViewModel(_creator, IsLocalCopy, IsDatabase, InitialColor) { ImportReader = ImportReader, ExistingOrders = ExistingOrders };
        form.SetMachines(_machines); return form;
    }

    private async Task ReadBatchItemAsync(BatchSpecificationItem item, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, item.ReadCancellation);
        var entered = false;
        try
        {
            if (SpecificationImportService.CheckFiles([item.Path ?? ""]) is { } error)
                throw new DocumentRejectedException(DocumentRejection.UnsupportedFileType, error);
            await _batchReads.WaitAsync(linked.Token); entered = true;
            await LoadMachinesAsync(linked.Token);
            item.Form.SetMachines(_machines);
            var preview = await ImportReader.ReadAsync(item.Path!, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!BatchItems.Contains(item)) return;
            item.Form.ApplyImport(preview, item.Path);
            item.Form.Manager.Value = Manager.Value; item.Form.Customer.Value = Customer.Value;
            item.HasParameters = true;
            UpdateDuplicateWarnings();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { if (BatchItems.Contains(item)) item.Failure = "Чтение отменено."; }
        catch (DocumentRejectedException exception) { item.Failure = exception.Message; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { item.Failure = AppDiagnostics.Describe(DiagnosticOperation.SpecificationImport, exception, "Не удалось прочитать спецификацию."); }
        finally { if (entered) _batchReads.Release(); item.IsReading = false; }
    }

    internal void RemoveBatchItem(BatchSpecificationItem item)
    {
        if (!CanAddFiles) return;
        item.Detach(); BatchItems.Remove(item); UpdateDuplicateWarnings(); NotifyBatchState();
    }

    internal void UpdateDuplicateWarnings()
    {
        foreach (var item in BatchItems.Where(item => item.HasParameters))
        {
            var form = item.Form;
            var sameNumber = BatchItems.Any(other => other != item && other.HasParameters && other.Form.OrderDate.Value == form.OrderDate.Value &&
                other.Form.SpecificationNumber.Value == form.SpecificationNumber.Value && other.Form.CuttingType.Value == form.CuttingType.Value);
            var inDatabase = DateOnly.TryParseExact(form.OrderDate.Value, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) && ExistingOrders.Any(order => order.OrderDate == date &&
                    order.SpecificationNumber == form.SpecificationNumber.Value && order.CuttingType == form.CuttingType.Value && order.CustomerName == Customer.Value);
            item.Warning = sameNumber ? "В пакете есть спецификация с таким номером и датой." : inDatabase ? "Похожая заявка уже есть в журнале." : null;
        }
    }

    private async Task CalculateBatchPricesAsync(CancellationToken cancellationToken)
    {
        foreach (var item in BatchItems.Where(item => item.IsIncluded && item.IsReady).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var registration = cancellationToken.Register(item.Form.CalculatePriceCommand.Cancel);
            await item.Form.CalculatePriceCommand.ExecuteAsync(null);
        }
    }

    private async Task SaveBatchAsync()
    {
        SaveError = null; AccessHostHelp.ShowFor(null);
        var items = BatchItems.Where(item => item.IsIncluded).ToArray();
        var drafts = new List<ManualOrderDraft>();
        foreach (var item in items)
        {
            item.Form.SaveError = null;
            item.Form.Manager.Value = Manager.Value; item.Form.Customer.Value = Customer.Value;
            if (!item.Form.Validate())
            {
                Manager.Error = item.Form.Manager.Error; Customer.Error = item.Form.Customer.Error;
                HasValidationErrors = true; return;
            }
            drafts.Add(item.Form.BuildDraft());
        }
        HasValidationErrors = false;
        try
        {
            CreatedOrders = await ((IManualOrderBatchCreator)_creator).CreateBatchAsync(drafts);
            if (CreatedOrders.Count != drafts.Count || CreatedOrders.Select(order => order.Id).Distinct().Count() != drafts.Count)
                throw new OrderWriteException(OrderWriteFailure.OutcomeUnknown, "Сохранение пакета не подтверждено. Обновите список (F5).");
            CreatedOrder = CreatedOrders[0];
        }
        catch (OrderBatchPriceChangedException changed)
        {
            var item = items[changed.Index]; item.Form.ShowQuote(changed.Quote); item.Form.SaveError = "Прайс изменился. Проверьте цену.";
            SaveError = $"Прайс изменился у заявки {BatchItems.IndexOf(item) + 1}. Проверьте новую цену.";
            return;
        }
        catch (Exception exception)
        {
            SaveError = AppDiagnostics.Describe(DiagnosticOperation.OrderCreate, exception, "Пакет не добавлен. Ввод сохранён в форме.");
            AccessHostHelp.ShowFor(exception);
            if (exception is OrderWriteException { RequiresReload: true })
            { RequiresReload = true; ReloadRequired?.Invoke(this, EventArgs.Empty); }
            return;
        }
        IsSaved = true; NotifyBatchState(); Saved?.Invoke(this, EventArgs.Empty);
    }
}
