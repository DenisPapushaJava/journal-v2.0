using Avalonia.Threading;
using Jornal.Core.Orders;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class MainWindow
{
    private CancellationTokenSource? _createdOrderRevealCancellation;

    private void CancelCreatedOrderReveal()
    {
        _createdOrderRevealCancellation?.Cancel();
        _createdOrderRevealCancellation = null;
    }

    private async Task RevealCreatedOrdersAsync(MainWindowViewModel model, IReadOnlyList<OrderSummary> created)
    {
        CancelCreatedOrderReveal();
        using var cancellation = new CancellationTokenSource();
        _createdOrderRevealCancellation = cancellation;
        try
        {
            model.AcceptCreatedOrders(created);
            var loading = model.OrdersLoadTask;
            await loading.WaitAsync(cancellation.Token);
            // AcceptCreatedOrders может переключить день на историю. Сначала ждём её
            // снимок и очередь привязок/layout, затем ищем строки в текущем представлении.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellation.IsCancellationRequested || !IsVisible || !ReferenceEquals(model, _viewModel) ||
                    !ReferenceEquals(loading, model.OrdersLoadTask) || !model.CanBrowseJournal || model.IsRefreshingOrders ||
                    model.RequiresReload || OwnedWindows.Count > 0) return;
                _sorting.Synchronize();
                OrdersGrid.UpdateLayout();
                var ids = created.Select(row => row.Id).ToHashSet();
                var rows = OrdersGrid.CollectionView.OfType<OrderRowViewModel>().Where(row => ids.Contains(row.Id)).ToArray();
                if (rows.Length == 0) return;
                var active = rows.FirstOrDefault(row => row.Id == created[0].Id) ?? rows[0];
                OnSelectionRestore(this, new(rows.Select(row => row.Id).ToArray(), active.Id));
                OrdersGrid.UpdateLayout();
                // null-строка означает для DataGrid прокрутку старого слота столбца,
                // что небезопасно после Reset пустого списка. Передаём только живую строку.
                OrdersGrid.ScrollIntoView(active, null);
            }, DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // Запись уже подтверждена. Ошибка её показа не должна выйти в async void
            // обработчик кнопки и завершить приложение или привести к повтору записи.
            if (!cancellation.IsCancellationRequested && IsVisible && ReferenceEquals(model, _viewModel))
                model.ReportCreatedOrdersDisplayFailure(created, exception);
        }
        finally
        {
            if (ReferenceEquals(_createdOrderRevealCancellation, cancellation)) _createdOrderRevealCancellation = null;
        }
    }
}
