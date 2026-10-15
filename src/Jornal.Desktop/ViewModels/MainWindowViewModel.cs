using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public partial class MainWindowViewModel(IOrderReader orderReader) : ObservableObject
{
    private IReadOnlyList<OrderSummary> _allOrders = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _countText = "Заявок: 0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public ObservableCollection<OrderRowViewModel> Orders { get; } = [];

    partial void OnSearchTextChanged(string value) => ApplySearch();

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
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Keep the currently displayed rows when loading is cancelled.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Order loading failed: {0}", exception);
            ErrorMessage = "Не удалось загрузить заявки. Попробуйте обновить список.";
        }
    }

    private void ApplySearch()
    {
        Orders.Clear();
        foreach (var order in OrderSearch.Filter(_allOrders, SearchText))
        {
            Orders.Add(new OrderRowViewModel(order));
        }

        CountText = $"Заявок: {Orders.Count} из {_allOrders.Count}";
    }
}
