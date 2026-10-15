using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public enum OrderStatusFilter { All, Work, Ready, Shipped }

public partial class MainWindowViewModel(IOrderReader orderReader, IManualOrderCreator? orderCreator = null) : ObservableObject
{
    private IReadOnlyList<OrderSummary> _allOrders = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _searchScopeIndex;
    [ObservableProperty] private string _countText = "Заявок: 0";
    [ObservableProperty] private string _selectionText = "Выбрано: 0";
    [ObservableProperty] private string _activeOrderText = "Активная: —";
    [ObservableProperty] private string _updatedText = "F5 — обновить список";
    [ObservableProperty] private bool _hasNoOrders = true;
    [ObservableProperty] private DateTime? _filterDate;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ThemeLabel))] private bool _isDarkTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterAll))]
    [NotifyPropertyChangedFor(nameof(FilterWork))]
    [NotifyPropertyChangedFor(nameof(FilterReady))]
    [NotifyPropertyChangedFor(nameof(FilterShipped))]
    private OrderStatusFilter _statusFilter;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string ThemeLabel => IsDarkTheme ? "Тёмная тема" : "Светлая тема";
    public bool FilterAll => StatusFilter == OrderStatusFilter.All;
    public bool FilterWork => StatusFilter == OrderStatusFilter.Work;
    public bool FilterReady => StatusFilter == OrderStatusFilter.Ready;
    public bool FilterShipped => StatusFilter == OrderStatusFilter.Shipped;
    public ObservableCollection<OrderRowViewModel> Orders { get; } = [];
    public bool CanCreateOrder => orderCreator is not null;
    public NewOrderViewModel? CreateOrderForm() => orderCreator is null ? null : new NewOrderViewModel(orderCreator);

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
        var count = selected.Count(row => Orders.Contains(row));
        SelectionText = $"Выбрано: {count}";
        ActiveOrderText = count > 1 ? "Выбрано несколько заявок" : active is not null && Orders.Contains(active)
            ? $"Активная: {active.SpecificationNumber}" : "Активная: —";
    }

    [RelayCommand] private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;
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

    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        try
        {
            var orders = await orderReader.ReadAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _allOrders = orders;
            ApplySearch();
            UpdatedText = $"Обновлено в {DateTime.Now:HH:mm} · F5 — обновить";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // При отмене сохраняем уже показанные строки.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Order loading failed: {0}", exception);
            ErrorMessage = "Не удалось загрузить заявки. Попробуйте обновить список.";
        }
    }

    private void ApplySearch()
    {
        var query = SearchText.Trim();
        var filtered = _allOrders.Where(order => MatchesSearch(order, query));
        var color = StatusFilter switch
        {
            OrderStatusFilter.Work => -882036, OrderStatusFilter.Ready => -10238087,
            OrderStatusFilter.Shipped => -8154114, _ => (int?)null
        };
        if (color.HasValue) filtered = filtered.Where(order => order.Color == color.Value);
        if (FilterDate.HasValue)
        {
            var date = DateOnly.FromDateTime(FilterDate.Value);
            filtered = filtered.Where(order => order.OrderDate == date);
        }

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
        if (text is not null) return text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var thicknessMatches = decimal.TryParse(query.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var thickness) && order.ThicknessMillimeters == thickness;
        return SearchScopeIndex == 6 ? thicknessMatches :
            OrderSearch.Matches(order, query) ||
            order.DeliveryNumber.Contains(query, StringComparison.OrdinalIgnoreCase) || thicknessMatches;
    }
}
