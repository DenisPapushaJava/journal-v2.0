using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;
using Jornal.Desktop.Tables;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private OrderSearchQuery _searchQuery = new(null);
    private bool _resettingSearch;
    private readonly OrderRowCollection _visibleRows = new();
    private readonly Dictionary<int, OrderRowViewModel> _rowCache = new();
    private IReadOnlyList<OrderSummary>? _cachedSource;
    [ObservableProperty] private bool _isSearchPending;
    public event EventHandler? SelectionClearingRequested;

    public IReadOnlyList<string> SearchScopes { get; } =
        ["Все поля", "Спецификация", "БЛЦБ", "Заказчик", "Менеджер", "Материал", "Комментарий"];

    partial void OnSearchTextChanged(string value) => ChangeSearch();
    partial void OnSearchScopeIndexChanged(int value) => ChangeSearch();
    partial void OnIsSearchPendingChanged(bool value)
    {
        if (value) UpdateSelection([], null);
        NotifyJournalAvailability();
    }

    private void ChangeSearch()
    {
        var query = new OrderSearchQuery(SearchText, (OrderSearchField)SearchScopeIndex);
        var unchanged = query.Field == _searchQuery.Field && string.Equals(query.Text, _searchQuery.Text, StringComparison.OrdinalIgnoreCase);
        _searchQuery = query;
        if (_resettingSearch || unchanged) return;
        ResetTableFilters();
        ApplySearch();
    }

    private void ResetTableFilters()
    {
        var wasResetting = _resettingSearch;
        _resettingSearch = true;
        try
        {
            foreach (var filter in ColumnFilters) filter.Reset();
            StatusFilter = OrderStatusFilter.All;
            FilterDate = null;
        }
        finally { _resettingSearch = wasResetting; }
    }

    [RelayCommand] private void ClearSearch() => ResetSearch(resetScope: false);
    [RelayCommand] private void ResetFilters() => ResetSearch(resetScope: true);

    private void ResetSearch(bool resetScope)
    {
        _resettingSearch = true;
        try
        {
            var wasEmpty = SearchText.Length == 0;
            SearchText = "";
            // Отменяет ещё не применённый ввод, даже если применённый запрос уже пуст.
            if (wasEmpty) OnPropertyChanged(nameof(SearchText));
            if (resetScope) SearchScopeIndex = 0;
            ResetTableFilters();
        }
        finally { _resettingSearch = false; }
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (_resettingSearch) return;
        if (!ReferenceEquals(_cachedSource, _allOrders))
        {
            var ids = _allOrders.Select(row => row.Id).ToHashSet();
            foreach (var id in _rowCache.Keys.Where(id => !ids.Contains(id)).ToArray()) _rowCache.Remove(id);
            _cachedSource = _allOrders;
        }
        var rows = new List<OrderRowViewModel>();
        foreach (var order in _allOrders.Where(MatchesCurrentFilters))
        {
            if (!_rowCache.TryGetValue(order.Id, out var row)) _rowCache[order.Id] = row = new(order);
            else if (!ReferenceEquals(row.Source, order)) row.Update(order);
            rows.Add(row);
        }
        _visibleRows.ReplaceWith(rows);
        UpdateSelection([], null);
        SelectionClearingRequested?.Invoke(this, EventArgs.Empty);
        HasNoOrders = Orders.Count == 0;
        CountText = $"Заявок: {Orders.Count} из {_allOrders.Count}";
        NotifyFilterContextChanged();
    }
}
