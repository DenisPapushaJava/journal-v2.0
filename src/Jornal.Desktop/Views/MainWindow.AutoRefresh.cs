using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;

namespace Jornal.Desktop.Views;

public partial class MainWindow
{
    private JournalAutoRefresh? _journalAutoRefresh;
    private readonly DispatcherTimer _journalRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private void InitializeAutoRefresh()
    {
        _journalAutoRefresh?.Dispose();
        var model = _viewModel;
        _journalAutoRefresh = model is null ? null : new(model.RefreshAutomaticallyAsync, action => Dispatcher.UIThread.Post(action));
        ConfigureAutoRefresh();
    }

    private void ConfigureAutoRefresh() => _journalAutoRefresh?.Configure(_viewModel?.DatabasePath,
        _viewModel?.AutomaticJournalRefresh == true);

    private void OnExplicitReadStarted(object? sender, long id) => _journalAutoRefresh?.ManualReadStarted(id);
    private void OnExplicitReadCompleted(object? sender, ViewModels.JournalReadCompletion result) =>
        _journalAutoRefresh?.ManualReadCompleted(result.Id, result.Succeeded);

    private bool HasOpenJournalPopup => _ordersMenu.Menu.IsOpen || _resultMenu.IsOpen || ColumnsButton.Flyout?.IsOpen == true ||
        SearchScopeSelector.IsDropDownOpen || DateFilter.IsDropDownOpen ||
        OrdersGrid.GetVisualDescendants().OfType<DataGridColumnHeadersPresenter>().FirstOrDefault()?.GetVisualDescendants()
            .Any(control => control is ColumnFilterHeader { IsFilterOpen: true } or StatusFilterHeader { IsFilterOpen: true }) == true ||
        ResultPanel.GetVisualDescendants().Any(control => control is ComboBox { IsDropDownOpen: true } || control is Button { Flyout.IsOpen: true });

    private async void OnJournalRefreshTick(object? sender, EventArgs e)
    {
        if (_journalAutoRefresh is not { } refresh || _viewModel is not { } model) return;
        // Окна редактора, пакетного импорта, прайса и корзины используют тот же источник.
        // Пока они открыты, фон не читает MDB и не меняет экран под пользователем.
        await refresh.TickAsync(IsVisible && WindowState != WindowState.Minimized,
            OwnedWindows.Count == 0 && model.CanAutomaticallyRefresh &&
            !(IsActive && ResultTextBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(model.ResultText)) && !HasOpenJournalPopup);
    }
}
