using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Jornal.Desktop.ViewModels;
using Jornal.Core.Orders;
using Jornal.Desktop.Customization;
using Jornal.Desktop.Clipboard;
using Jornal.Desktop.Updates;

namespace Jornal.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private readonly IUpdateRestartStore? _restartStore;
    private readonly CancellationTokenSource _updateChecksLifetime = new();
    private Task? _initialOrdersLoad;
    private bool _updateDraftRestored;

    private readonly MainWindowCustomization _customization;

    public MainWindow() : this(new MemoryUiPreferencesStore()) { }

    public MainWindow(IUiPreferencesStore preferencesStore, IUpdateRestartStore? restartStore = null)
    {
        InitializeComponent();
        ConfigureSpecificationDrop();
        _restartStore = restartStore;
        _customization = new MainWindowCustomization(this, preferencesStore);
        DataContextChanged += (_, _) => ConnectViewModel();
        foreach (var column in OrdersGrid.Columns.Skip(1))
        {
            var option = new CheckBox { Content = column.Header?.ToString(), MinHeight = 28 };
            option.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(DataGridColumn.IsVisible))
                { Source = column, Mode = BindingMode.TwoWay });
            ColumnChoices.Children.Add(option);
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _customization.Initialize();
        if (_viewModel is not null)
        {
            if (!_updateDraftRestored && _restartStore is not null)
            {
                _updateDraftRestored = true;
                _viewModel.RestoreUpdateDraft(_restartStore);
            }
            await LoadInitialOrdersAsync();
            if (IsVisible) await _viewModel.CleanupTrashAsync();
            if (IsVisible) await _viewModel.Updates.RunAutomaticChecksAsync(_updateChecksLifetime.Token);
        }
    }

    public Task LoadInitialOrdersAsync(CancellationToken cancellationToken = default) =>
        _initialOrdersLoad ??= LoadInitialOrdersCoreAsync(cancellationToken);

    private async Task LoadInitialOrdersCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_viewModel is not { } model) return;
        using var registration = cancellationToken.Register(model.ReloadCommand.Cancel);
        await model.ReloadCommand.ExecuteAsync(null).WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel?.IsUpdatingOrders == true || _viewModel?.Updates is { IsApplying: true, RestartScheduled: false }) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _updateChecksLifetime.Cancel();
        _updateChecksLifetime.Dispose();
        _customization.Close();
        if (_viewModel is not null)
        {
            _viewModel.ReloadCommand.Cancel();
            _viewModel.Updates.CheckCommand.Cancel();
            _viewModel.Updates.DownloadCommand.Cancel();
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.SelectionRestoreRequested -= OnSelectionRestore;
        }
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_viewModel?.IsImportingSpecification == true && e.Key == Key.Escape)
        { _specificationImportCancellation?.Cancel(); e.Handled = true; return; }
        if (_viewModel?.IsCustomizing == true)
        {
            if (e.Key == Key.Escape)
            {
                if (!_customization.CancelDrag()) _customization.Cancel();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            { _customization.Save(); e.Handled = true; return; }
        }
        base.OnKeyDown(e);
        if (_viewModel?.CanUseJournal == true && e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ConnectViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.SelectionRestoreRequested -= OnSelectionRestore;
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.SelectionRestoreRequested += OnSelectionRestore;
            _viewModel.Updates.ConfigureRestart(
                () => _viewModel.CanCheckUpdates && !OwnedWindows.Any(window => window is not AppUpdatesWindow),
                () =>
                {
                    AppUpdateSafety.EnsureExternalDatabase(_viewModel.CaptureUpdateDraft().DatabasePath, AppContext.BaseDirectory);
                    if (!_customization.Flush()) throw new IOException("Не удалось сохранить настройки экрана.");
                    if (_restartStore is null) throw new IOException("Хранилище результата недоступно.");
                    _restartStore.Save(_viewModel.CaptureUpdateDraft());
                },
                () => { foreach (var window in OwnedWindows.ToArray()) window.Close(); Close(); });
        }
        UpdateTheme();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsImportingSpecification)) UpdateSpecificationFocus();
        if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
        { UpdateTheme(); _customization.AppearanceChanged(); }
        if (e.PropertyName is nameof(MainWindowViewModel.ResultFontSizePoints) or nameof(MainWindowViewModel.ResultTextColor))
            _customization.AppearanceChanged();
    }

    private void UpdateTheme() => RequestedThemeVariant = _viewModel?.IsDarkTheme == true
        ? ThemeVariant.Dark : ThemeVariant.Light;

    private async void OnNewOrderClick(object? sender, RoutedEventArgs e)
    {
        if (OwnedWindows.Count > 0 || _viewModel?.CreateOrderForm() is not { } form) return;
        await ShowNewOrderAsync(form);
    }
    private async Task ShowNewOrderAsync(NewOrderViewModel form)
    {
        var dialog = new NewOrderWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        var order = await dialog.ShowDialog<OrderSummary?>(this);
        if (order is null) return;
        _viewModel!.AcceptCreatedOrder(order);
        OrdersGrid.SelectedItem = _viewModel.Orders.FirstOrDefault(row => row.Id == order.Id);
        OrdersGrid.ScrollIntoView(OrdersGrid.SelectedItem, OrdersGrid.Columns[2]);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelection();
    private async void OnEditOrderClick(object? sender, RoutedEventArgs e) => await EditOrderAsync();
    private async Task EditOrderAsync()
    {
        if (_viewModel?.CanEditOrder != true || OwnedWindows.Count > 0) return;
        if (await _viewModel.CreateEditFormAsync(_updateChecksLifetime.Token) is not { } form || !IsVisible) return;
        var dialog = new EditOrderWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        if (await dialog.ShowDialog<OrderRecord?>(this) is { } row) _viewModel.AcceptEditedOrder(row);
    }
    private async void OnOrdersDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is not Control control || control.FindAncestorOfType<DataGridRow>() is null ||
            control is CheckBox || control.GetVisualAncestors().OfType<CheckBox>().Any()) return;
        await EditOrderAsync();
    }
    private async void OnTrashOrderClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || OwnedWindows.Count > 0 || await _viewModel.CreateTrashFormAsync(_updateChecksLifetime.Token) is not { } form || !IsVisible) return;
        var dialog = new OrderConfirmationWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog<bool>(this);
    }
    private async void OnTrashClick(object? sender, RoutedEventArgs e)
    {
        if (OwnedWindows.Count > 0 || _viewModel?.CreateTrashViewModel() is not { } form) return;
        var dialog = new TrashWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog(this);
    }
    private void OnCurrentCellChanged(object? sender, EventArgs e) => UpdateSelection();
    private void UpdateSelection()
    {
        _viewModel?.UpdateSelection(OrdersGrid.SelectedItems.Cast<OrderRowViewModel>(),
            OrdersGrid.SelectedItem as OrderRowViewModel);
        var count = OrdersGrid.SelectedItems.Count;
        SelectAllRowsBox.IsChecked = count == 0 ? false : count == _viewModel?.Orders.Count ? true : null;
    }

    private void OnSelectAllRows(object? sender, RoutedEventArgs e)
    {
        // Частичный выбор при следующем нажатии дополняется до полного.
        if (OrdersGrid.SelectedItems.Count < _viewModel?.Orders.Count) OrdersGrid.SelectAll();
        else OrdersGrid.SelectedItems.Clear();
        UpdateSelection();
        e.Handled = true;
    }

    private async void OnDeliveryNumberClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CreateDeliveryNumberForm() is not { } form) return;
        var dialog = new DeliveryNumberWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog<bool>(this);
    }

    private async void OnDatabaseSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CreateDatabaseSettingsForm() is not { } form) return;
        var dialog = new DatabaseSettingsWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog<bool>(this);
    }

    private async void OnUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CanCheckUpdates != true || OwnedWindows.Count > 0) return;
        var dialog = new AppUpdatesWindow { DataContext = _viewModel.Updates, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog(this);
    }

    private void OnSelectionRestore(object? sender, SelectionRestoreRequest request)
    {
        var ids = request.Ids.ToHashSet();
        OrdersGrid.CollectionView.Refresh();
        var rows = _viewModel!.Orders.Where(row => ids.Contains(row.Id)).ToArray();
        OrdersGrid.SelectedItems.Clear();
        OrdersGrid.SelectedItem = rows.FirstOrDefault(row => row.Id == request.ActiveId) ?? rows.FirstOrDefault();
        foreach (var row in rows)
            if (!OrdersGrid.SelectedItems.Contains(row)) OrdersGrid.SelectedItems.Add(row);
        UpdateSelection();
    }

    private void OnAddResultClick(object? sender, RoutedEventArgs e)
    {
        var selected = OrdersGrid.SelectedItems.Cast<OrderRowViewModel>().ToHashSet();
        _viewModel?.AddOrdersToResult(OrdersGrid.CollectionView.Cast<OrderRowViewModel>().Where(selected.Contains));
    }
    private async void OnCopyResultClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is { } model) await model.CopyResultAsync(new ResultClipboard(() => Clipboard));
    }

    private void OnCustomizeClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.IsCustomizing == true) _customization.Cancel();
        else _customization.Begin();
    }
    private void OnSaveLayoutClick(object? sender, RoutedEventArgs e) => _customization.Save();
    private void OnCancelLayoutClick(object? sender, RoutedEventArgs e) => _customization.Cancel();
    private void OnResetLayoutClick(object? sender, RoutedEventArgs e) => _customization.Reset();
    private void OnDismissPreferencesError(object? sender, RoutedEventArgs e)
    { if (_viewModel is not null) _viewModel.PreferencesError = null; }

    private void OnShowAllColumns(object? sender, RoutedEventArgs e) => SetAllColumnsVisible(true);
    private void OnHideAllColumns(object? sender, RoutedEventArgs e) => SetAllColumnsVisible(false);
    private void SetAllColumnsVisible(bool visible)
    {
        foreach (var column in OrdersGrid.Columns.Skip(1)) column.IsVisible = visible;
    }
}
