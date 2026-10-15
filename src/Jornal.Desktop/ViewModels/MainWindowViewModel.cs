using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using Jornal.Desktop.DataSources;

namespace Jornal.Desktop.ViewModels;

public enum OrderStatusFilter { All, Work, Ready, Shipped }

public partial class MainWindowViewModel(IOrderReader orderReader, IManualOrderCreator? orderCreator = null,
    IOrderBatchEditor? orderEditor = null, OrderSourceManager? sourceManager = null) : ObservableObject
{
    private IReadOnlyList<OrderSummary> _allOrders = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _searchScopeIndex;
    [ObservableProperty] private string _countText = "Заявок: 0";
    [ObservableProperty] private string _selectionText = "Выбрано: 0";
    [ObservableProperty] private string _activeOrderText = "Активная: —";
    [ObservableProperty] private string _updatedText = "F5 — обновить список";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSelectOrders)), NotifyPropertyChangedFor(nameof(ShowEmptyState))] private bool _hasNoOrders = true;
    [ObservableProperty] private DateTime? _filterDate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ThemeActionLabel))]
    private bool _isDarkTheme;

    [ObservableProperty] private bool _isCustomizing;
    [ObservableProperty] private bool _isUpdatingOrders;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowEmptyState))] private bool _isLoadingOrders;

    partial void OnIsCustomizingChanged(bool value) => NotifyJournalAvailability();
    partial void OnIsUpdatingOrdersChanged(bool value) => NotifyJournalAvailability();
    partial void OnIsLoadingOrdersChanged(bool value) => NotifyJournalAvailability();

    private void NotifyJournalAvailability()
    {
        foreach (var name in new[] { nameof(CanUseJournal), nameof(CanCreateOrder), nameof(CanSelectOrders),
            nameof(CanConfigureScreen), nameof(CanInteractWithTable), nameof(CanUpdateSelectedOrders),
            nameof(CanAddResult), nameof(CanClearResult), nameof(CanCopyResult), nameof(CanEditResult), nameof(CanConfigureSource) })
            OnPropertyChanged(name);
        ReloadCommand.NotifyCanExecuteChanged();
        SetOrderColorCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPreferencesError))] private string? _preferencesError;
    public bool HasPreferencesError => !string.IsNullOrEmpty(PreferencesError);
    public bool CanUseJournal => !IsCustomizing && !IsUpdatingOrders && !IsLoadingOrders;
    public bool CanConfigureScreen => CanUseJournal;
    public bool CanInteractWithTable => !IsUpdatingOrders && !IsLoadingOrders;
    public bool CanSelectOrders => !HasNoOrders && CanUseJournal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterAll))]
    [NotifyPropertyChangedFor(nameof(FilterWork))]
    [NotifyPropertyChangedFor(nameof(FilterReady))]
    [NotifyPropertyChangedFor(nameof(FilterShipped))]
    private OrderStatusFilter _statusFilter;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError)), NotifyPropertyChangedFor(nameof(ShowEmptyState))] private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowEmptyState => HasNoOrders && !IsLoadingOrders && !HasError;
    public string ThemeActionLabel => IsDarkTheme ? "Включить светлую тему" : "Включить тёмную тему";
    public bool FilterAll => StatusFilter == OrderStatusFilter.All;
    public bool FilterWork => StatusFilter == OrderStatusFilter.Work;
    public bool FilterReady => StatusFilter == OrderStatusFilter.Ready;
    public bool FilterShipped => StatusFilter == OrderStatusFilter.Shipped;
    public ObservableCollection<OrderRowViewModel> Orders { get; } = [];
    public bool CanCreateOrder => orderCreator is not null && CanUseJournal;
    public NewOrderViewModel? CreateOrderForm() => CanCreateOrder ? new NewOrderViewModel(orderCreator!) : null;

    public void AcceptCreatedOrder(OrderSummary order)
    {
        _allOrders = new[] { order }.Concat(_allOrders.Where(row => row.Id != order.Id)).ToArray();
        // Новую строку показываем даже при несовпадающих прежних фильтрах.
        ResetFilters();
        ApplySearch();
        UpdatedText = "Демозаявка добавлена · до закрытия программы";
    }
    public IReadOnlyList<string> SearchScopes { get; } =
        ["Все поля", "Менеджер", "Заказчик", "Спецификация", "БЛЦБ", "Материал", "Толщина"];

    partial void OnSearchTextChanged(string value) => ApplySearch();
    partial void OnSearchScopeIndexChanged(int value) => ApplySearch();
    partial void OnStatusFilterChanged(OrderStatusFilter value) => ApplySearch();
    partial void OnFilterDateChanged(DateTime? value) => ApplySearch();

    public void UpdateSelection(IEnumerable<OrderRowViewModel> selected, OrderRowViewModel? active)
    {
        var visibleIds = Orders.Select(row => row.Id).ToHashSet();
        _selectedOrderIds = selected.Select(row => row.Id).Where(visibleIds.Contains).ToHashSet();
        var count = _selectedOrderIds.Count;
        _selectedCount = count;
        _activeOrderId = active is not null && visibleIds.Contains(active.Id) ? active.Id : null;
        OnPropertyChanged(nameof(CanAddResult));
        OnPropertyChanged(nameof(CanUpdateSelectedOrders));
        SetOrderColorCommand.NotifyCanExecuteChanged();
        SelectionText = $"Выбрано: {count}";
        ActiveOrderText = count > 1 ? "Выбрано несколько заявок" : active is not null && Orders.Contains(active)
            ? $"Активная: {active.SpecificationNumber}" : "Активная: —";
    }

    [RelayCommand] private void SetStatusFilter(OrderStatusFilter filter) => StatusFilter = filter;
    [RelayCommand] private void Today() => FilterDate = FilterDate?.Date == DateTime.Today ? null : DateTime.Today;
    [RelayCommand] private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private void ResetFilters()
    {
        SearchText = string.Empty;
        SearchScopeIndex = 0;
        StatusFilter = OrderStatusFilter.All;
        FilterDate = null;
    }

    [RelayCommand(CanExecute = nameof(CanUseJournal))]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (!CanUseJournal) return;
        ErrorMessage = null;
        IsLoadingOrders = true;
        try
        {
            var orders = await orderReader.ReadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _allOrders = orders;
            ApplySearch();
            RequiresReload = false;
            UpdatedText = $"Обновлено в {DateTime.Now:HH:mm} · F5 — обновить";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // При отмене сохраняем уже показанные строки.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Order loading failed: {0}", exception);
            ErrorMessage = exception is OrderReadException ? exception.Message : "Не удалось загрузить заявки. Попробуйте обновить список.";
        }
        finally { IsLoadingOrders = false; }
    }

    private void ApplySearch()
    {
        var filtered = _allOrders.Where(MatchesCurrentFilters);

        Orders.Clear();
        foreach (var order in filtered) Orders.Add(new OrderRowViewModel(order));
        UpdateSelection([], null);
        HasNoOrders = Orders.Count == 0;
        CountText = $"Заявок: {Orders.Count} из {_allOrders.Count}";
    }

    private bool MatchesSearch(OrderSummary order, string query)
    {
        if (query.Length == 0) return true;
        var text = SearchScopeIndex switch
        {
            1 => order.ManagerName, 2 => order.CustomerName, 3 => order.SpecificationNumber,
            4 => order.DeliveryNumber, 5 => order.MaterialName, _ => null
        };
        if (SearchScopeIndex is >= 1 and <= 5) return text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
        var thicknessMatches = decimal.TryParse(query.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var thickness) && order.ThicknessMillimeters == thickness;
        return SearchScopeIndex == 6 ? thicknessMatches :
            OrderSearch.Matches(order, query) ||
            order.DeliveryNumber?.Contains(query, StringComparison.OrdinalIgnoreCase) == true || thicknessMatches;
    }
}
