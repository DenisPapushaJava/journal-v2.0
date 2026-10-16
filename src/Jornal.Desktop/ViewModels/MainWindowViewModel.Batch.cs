using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Jornal.Core.Orders;

namespace Jornal.Desktop.ViewModels;

public sealed record SelectionRestoreRequest(IReadOnlyCollection<int> Ids, int? ActiveId);

public partial class MainWindowViewModel
{
    private HashSet<int> _selectedOrderIds = [];
    private int? _activeOrderId;
    [ObservableProperty] private bool _requiresReload;
    partial void OnRequiresReloadChanged(bool value) => NotifyJournalAvailability();
    public bool CanUpdateSelectedOrders => orderEditor is not null && _selectedCount > 0 && CanUseJournal && !RequiresReload;
    public event EventHandler<SelectionRestoreRequest>? SelectionRestoreRequested;

    [RelayCommand(CanExecute = nameof(CanUpdateSelectedOrders))]
    private async Task SetOrderColorAsync(int color)
    {
        if (!CanUpdateSelectedOrders) return;
        var expected = Orders.Where(row => _selectedOrderIds.Contains(row.Id)).Select(row => row.Source).ToArray();
        var ids = expected.Select(order => order.Id).ToArray();
        var caption = color switch
        {
            OrderColors.Work => "В работе", OrderColors.Ready => "Готово",
            OrderColors.Shipped => "Отгружено", OrderColors.None => "Без отметки", _ => "Своя отметка"
        };
        await UpdateOrdersAsync(ids, () => orderEditor!.SetColorAsync(expected, color), $"{caption} · изменено заявок: {ids.Length}");
    }

    public DeliveryNumberViewModel? CreateDeliveryNumberForm()
    {
        if (!CanUpdateSelectedOrders) return null;
        var selected = Orders.Where(row => _selectedOrderIds.Contains(row.Id)).ToArray();
        var ids = selected.Select(row => row.Id).ToArray();
        var expected = selected.Select(row => row.Source).ToArray();
        return new DeliveryNumberViewModel(expected, number =>
            UpdateOrdersAsync(ids, () => orderEditor!.SetDeliveryNumberAsync(expected, number), $"БЛЦБ обновлён · заявок: {ids.Length}"),
            BatchStorageDescription, () => ErrorMessage, () => !RequiresReload, AccessHostHelp);
    }

    private async Task<bool> UpdateOrdersAsync(IReadOnlyCollection<int> ids,
        Func<Task<IReadOnlyList<OrderSummary>>> update, string message)
    {
        if (!CanUseJournal || orderEditor is null || RequiresReload) return false;
        var selectedIds = _selectedOrderIds.ToHashSet();
        var activeId = _activeOrderId;
        IsUpdatingOrders = true;
        var previousUpdate = UpdatedText;
        UpdatedText = "Сохранение изменений…";
        ErrorMessage = null;
        var writeCompleted = false;
        try
        {
            var updated = await update();
            writeCompleted = true;
            if (updated.Count != ids.Count || !updated.Select(order => order.Id).ToHashSet().SetEquals(ids))
                throw new InvalidOperationException("Адаптер вернул неполный набор изменённых заявок.");
            var replacements = updated.ToDictionary(order => order.Id);
            _allOrders = _allOrders.Select(order => replacements.GetValueOrDefault(order.Id, order)).ToArray();
            // Сохраняем объекты видимых строк: положение и выделение не привязаны к их индексам.
            foreach (var row in Orders.ToArray())
            {
                if (!replacements.TryGetValue(row.Id, out var replacement)) continue;
                row.Update(replacement);
                if (!MatchesCurrentFilters(replacement)) Orders.Remove(row);
            }
            HasNoOrders = Orders.Count == 0;
            CountText = $"Заявок: {Orders.Count} из {_allOrders.Count}";
            var surviving = Orders.Where(row => selectedIds.Contains(row.Id)).ToArray();
            var active = surviving.FirstOrDefault(row => row.Id == activeId) ?? surviving.FirstOrDefault();
            UpdateSelection(surviving, active);
            SelectionRestoreRequested?.Invoke(this, new(surviving.Select(row => row.Id).ToArray(), active?.Id));
            UpdatedText = message + " · " + StorageSuffix;
            return true;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Order batch update failed: {0}", exception);
            UpdatedText = previousUpdate;
            RequiresReload = writeCompleted || exception is OrderWriteException { RequiresReload: true };
            ErrorMessage = writeCompleted ? "Изменения сохранены, но не удалось обновить экран. Закройте форму БЛЦБ, если она открыта, и обновите список (F5)." :
                exception is OrderWriteException ? exception.Message : "Не удалось изменить заявки. Список и выбор сохранены — попробуйте ещё раз.";
            AccessHostHelp.ShowFor(exception);
            return false;
        }
        finally { IsUpdatingOrders = false; }
    }

    private bool MatchesCurrentFilters(OrderSummary order)
    {
        var matchesStatus = StatusFilter switch
        {
            OrderStatusFilter.Work => order.Color == OrderColors.Work,
            OrderStatusFilter.Ready => order.Color == OrderColors.Ready,
            OrderStatusFilter.Shipped => order.Color == OrderColors.Shipped, _ => true
        };
        return matchesStatus && (!FilterDate.HasValue || order.OrderDate == DateOnly.FromDateTime(FilterDate.Value)) &&
            MatchesSearch(order, SearchText.Trim());
    }
}
