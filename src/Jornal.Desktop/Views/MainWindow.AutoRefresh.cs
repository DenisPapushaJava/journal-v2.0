using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.DataSources;

namespace Jornal.Desktop.Views;

public partial class MainWindow
{
    private JournalAutoRefresh? _journalAutoRefresh;
    private readonly TimeProvider? _journalRefreshClock;
    private readonly Func<string, Action, Action, Task<IDisposable>>? _journalSubscribe;
    private readonly bool _runJournalRefreshTimer;
    private readonly DispatcherTimer _journalRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly HashSet<IPointer> _journalPressedPointers = [];

    private void ConfigureJournalPointerTracking()
    {
        AddHandler(PointerPressedEvent, OnJournalPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerReleasedEvent, OnJournalPointerReleased, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, OnJournalPointerMoved, RoutingStrategies.Tunnel, true);
        Deactivated += (_, _) => _journalPressedPointers.Clear();
    }

    private void OnJournalPointerPressed(object? sender, PointerPressedEventArgs e) => _journalPressedPointers.Add(e.Pointer);
    private void OnJournalPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch || !HasPressedButtons(e.GetCurrentPoint(this).Properties))
            _journalPressedPointers.Remove(e.Pointer);
    }
    private void OnJournalPointerMoved(object? sender, PointerEventArgs e)
    {
        // Движение без кнопок также завершает отменённый захват/отпускание вне окна.
        if (e.Pointer.Type == PointerType.Touch) return;
        if (HasPressedButtons(e.GetCurrentPoint(this).Properties)) _journalPressedPointers.Add(e.Pointer);
        else _journalPressedPointers.Remove(e.Pointer);
    }
    private static bool HasPressedButtons(PointerPointProperties properties) => properties.IsLeftButtonPressed ||
        properties.IsRightButtonPressed || properties.IsMiddleButtonPressed || properties.IsXButton1Pressed || properties.IsXButton2Pressed;
    private bool HasJournalPointerGesture
    {
        get
        {
            // Отмена жеста/касания снимает захват без PointerReleased.
            _journalPressedPointers.RemoveWhere(static pointer => pointer.Captured is null);
            return _journalPressedPointers.Count > 0;
        }
    }

    private void InitializeAutoRefresh()
    {
        _journalAutoRefresh?.Dispose();
        var model = _viewModel;
        _journalAutoRefresh = model is null ? null : new(model.RefreshAutomaticallyAsync,
            action => Dispatcher.UIThread.Post(action), _journalRefreshClock, _journalSubscribe);
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

    private async void OnJournalRefreshTick(object? sender, EventArgs e) => await TickJournalRefreshAsync();

    internal async Task TickJournalRefreshAsync()
    {
        if (_journalAutoRefresh is not { } refresh || _viewModel is not { } model) return;
        // Окна редактора, пакетного импорта, прайса и корзины используют тот же источник.
        // Пока они открыты, фон не читает MDB и не меняет экран под пользователем.
        await refresh.TickAsync(IsVisible && WindowState != WindowState.Minimized,
            !HasJournalPointerGesture && OwnedWindows.Count == 0 && model.CanAutomaticallyRefresh &&
            !(IsActive && ResultTextBox.IsKeyboardFocusWithin && !string.IsNullOrWhiteSpace(model.ResultText)) && !HasOpenJournalPopup);
    }
}
