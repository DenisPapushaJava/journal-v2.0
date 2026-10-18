using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Core.Orders;
using Jornal.Infrastructure.Access;
using Jornal.Infrastructure.Diagnostics;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private bool _ordersLoaded;
    private DateOnly? _loadedDate;
    private bool _scopeLoadFailed;
    private long _readGeneration;
    private CancellationTokenSource? _scopeReadCancellation;
    public Task OrdersLoadTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StartupPeriodIndex))]
    private bool _showTodayOnStartup = true;
    public IReadOnlyList<string> StartupPeriods { get; } = ["Сегодня", "Все даты"];
    public int StartupPeriodIndex { get => ShowTodayOnStartup ? 0 : 1; set => ShowTodayOnStartup = value != 1; }
    public bool IsTodaySelected
    {
        get => FilterDate?.Date == DateTime.Today;
        set { if (value) FilterDate = DateTime.Today; }
    }
    public bool IsAllDatesSelected
    {
        get => FilterDate is null;
        set { if (value) FilterDate = null; }
    }
    public string EmptyOrdersText => FilterDate is { } date ? $"Нет заявок за {date:dd.MM.yyyy}" : "Заявки не найдены";
    private DateOnly? RequestedDate => FilterDate is { } date ? DateOnly.FromDateTime(date) : null;
    private bool CoversRequestedPeriod => _ordersLoaded && (_loadedDate is null || _loadedDate == RequestedDate);

    private Task<IReadOnlyList<OrderSummary>> ReadExistingOrdersForDateAsync(DateOnly date, CancellationToken token) =>
        _ordersLoaded && (_loadedDate is null || _loadedDate == date)
            ? Task.FromResult<IReadOnlyList<OrderSummary>>(_allOrders.Where(row => row.OrderDate == date).ToArray())
            : orderReader.ReadDayAsync(date, token);

    // Вызывается после чтения локальных настроек и до первого запроса/открытия окна.
    public void InitializeStartupPeriod()
    {
        if (_ordersLoaded || _scopeReadCancellation is not null) return;
        FilterDate = ShowTodayOnStartup ? DateTime.Today : null;
    }

    public void CancelOrderLoading()
    {
        _readGeneration++;
        var wasReading = _scopeReadCancellation is not null;
        _scopeReadCancellation?.Cancel();
        _scopeReadCancellation = null;
        if (wasReading) IsLoadingOrders = false;
    }

    private async Task LoadOrdersScopeAsync(bool force, CancellationToken cancellationToken = default)
    {
        // Неизвестный исход записи требует полной сверки, в том числе исторических заявок.
        var date = RequiresReload ? null : RequestedDate;
        CancelOrderLoading();
        var generation = _readGeneration;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _scopeReadCancellation = cancellation;
        var previousUpdate = UpdatedText;
        IsLoadingOrders = true;
        ErrorMessage = null;
        UpdatedText = date is { } day ? $"Загрузка заявок за {day:dd.MM.yyyy}…" : "Загрузка журнала…";
        UpdateSelection([], null);
        SelectionClearingRequested?.Invoke(this, EventArgs.Empty);
        AppDiagnostics.Current?.Mark(DiagnosticOperation.OrderRead);
        try
        {
            var rows = date is { } selected
                ? await orderReader.ReadDayAsync(selected, cancellation.Token)
                : await orderReader.ReadAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (generation != _readGeneration) return;
            _allOrders = rows;
            _loadedDate = date;
            _ordersLoaded = true;
            _scopeLoadFailed = false;
            ErrorMessage = (orderReader as OrderDurationPreparingReader)?.PreparationWarning;
            if (force) RequiresReload = false;
            ApplyLoadedOrders();
            UpdatedText = $"Обновлено в {DateTime.Now:HH:mm} · F5 — обновить";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _readGeneration) UpdatedText = previousUpdate;
        }
        catch (Exception exception)
        {
            if (generation != _readGeneration) return;
            // Старый список остаётся видимым, но действия с неполным результатом запрещены.
            _scopeLoadFailed = !CoversRequestedPeriod;
            UpdatedText = previousUpdate;
            ErrorMessage = AppDiagnostics.WithCode(exception is OrderReadException ? exception.Message :
                "Не удалось загрузить заявки. Попробуйте обновить список (F5).",
                AppDiagnostics.Capture(DiagnosticOperation.OrderRead, exception));
            AccessHostHelp.ShowFor(exception);
        }
        finally
        {
            if (generation == _readGeneration)
            {
                _scopeReadCancellation = null;
                IsLoadingOrders = false;
            }
        }
    }
}
