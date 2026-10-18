using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
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
        if (Jornal.Infrastructure.Diagnostics.AppDiagnostics.Current?.HasPreviousFatal == true)
            DiagnosticsButton.Label = "Сбой предыдущего запуска · Диагностика";
        _customization.Initialize();
        if (OrdersGrid.CollectionView is { CanSort: true } orders && orders.SortDescriptions.Count == 0)
            orders.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(OrderRowViewModel.OrderDateSortKey), ListSortDirection.Ascending));
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

    public void InitializeStartupPeriod()
    {
        _customization.Initialize();
        _viewModel?.InitializeStartupPeriod();
    }

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
        if (_viewModel?.IsUpdatingOrders == true || _viewModel?.IsCopyingSelection == true || _viewModel?.Updates is { IsApplying: true, RestartScheduled: false }) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SearchBox.CancelPendingInput();
        _updateChecksLifetime.Cancel();
        _updateChecksLifetime.Dispose();
        _customization.Close();
        if (_viewModel is not null)
        {
            _viewModel.ReloadCommand.Cancel();
            _viewModel.CancelOrderLoading();
            _viewModel.CancelOrderLoading();
            _viewModel.Updates.CheckCommand.Cancel();
            _viewModel.Updates.DownloadCommand.Cancel();
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.SelectionRestoreRequested -= OnSelectionRestore;
            _viewModel.SelectionClearingRequested -= OnSelectionClearingRequested;
        }
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None && _viewModel?.CanEnterSearch == true)
        {
            SearchBox.SubmitInput();
            _viewModel.ReloadCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (_viewModel?.IsImportingSpecification == true && e.Key == Key.Escape)
        { _specificationImportCancellation?.Cancel(); e.Handled = true; return; }
        if (_viewModel?.HasSpecificationImportError == true && e.Key == Key.Escape)
        { _viewModel.DismissSpecificationImportErrorCommand.Execute(null); e.Handled = true; return; }
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
        if (_viewModel?.CanEnterSearch == true && e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ConnectViewModel()
    {
        SearchBox.CancelPendingInput();
        if (_viewModel is not null)
        {
            _viewModel.CancelOrderLoading();
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.SelectionRestoreRequested -= OnSelectionRestore;
            _viewModel.SelectionClearingRequested -= OnSelectionClearingRequested;
        }
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.SelectionRestoreRequested += OnSelectionRestore;
            _viewModel.SelectionClearingRequested += OnSelectionClearingRequested;
            _viewModel.Updates.ConfigureRestart(
                () => _viewModel.CanCheckUpdates && !OwnedWindows.Any(window => window is not AppUpdatesWindow && window is not SettingsWindow { CanRestart: true }),
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
        if (e.PropertyName == nameof(MainWindowViewModel.SearchText)) SearchBox.CancelPendingInput();
        if (e.PropertyName is nameof(MainWindowViewModel.SearchScopeIndex) or nameof(MainWindowViewModel.SearchExactMatch)
            && SearchBox.IsPending) SearchBox.SubmitInput();
        if (e.PropertyName is nameof(MainWindowViewModel.IsImportingSpecification) or nameof(MainWindowViewModel.HasSpecificationImportError)) UpdateSpecificationFocus();
        if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme))
        { UpdateTheme(); _customization.AppearanceChanged(); }
        if (e.PropertyName is nameof(MainWindowViewModel.ResultFontSizePoints) or nameof(MainWindowViewModel.ResultTextColor) or
            nameof(MainWindowViewModel.UseResultMinimumPrice) or nameof(MainWindowViewModel.ResultMinimumPrice) or nameof(MainWindowViewModel.ShowTodayOnStartup) or
            nameof(MainWindowViewModel.UseTechnologistColor) or nameof(MainWindowViewModel.TechnologistColorArgb) or nameof(MainWindowViewModel.EntrySuggestionsSummary))
            _customization.AppearanceChanged();
    }

    private void OnSearchPendingChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null) return;
        _viewModel.IsSearchPending = SearchBox.IsPending;
        if (SearchBox.IsPending) OnSelectionClearingRequested(this, EventArgs.Empty);
    }
    private void OnSelectionClearingRequested(object? sender, EventArgs e)
    { OrdersGrid.SelectedItems.Clear(); UpdateSelection(); }
    private void OnSearchClearRequested(object? sender, EventArgs e) => _viewModel?.ClearSearchCommand.Execute(null);
    private void OnClearSearchClick(object? sender, RoutedEventArgs e)
    { SearchBox.ClearInput(); SearchBox.Focus(); }

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
        var created = form.CreatedOrders.Count > 0 ? form.CreatedOrders : [order];
        _viewModel!.AcceptCreatedOrders(created);
        var ids = created.Select(row => row.Id).ToHashSet();
        OrdersGrid.SelectedItems.Clear();
        foreach (var row in _viewModel.Orders.Where(row => ids.Contains(row.Id))) OrdersGrid.SelectedItems.Add(row);
        if (created.Count == 1) OrdersGrid.SelectedItem = _viewModel.Orders.FirstOrDefault(row => row.Id == order.Id);
        OrdersGrid.ScrollIntoView(_viewModel.Orders.FirstOrDefault(row => row.Id == order.Id), OrdersGrid.Columns[2]);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelection();
    private async void OnDiagnosticsClick(object? sender, RoutedEventArgs e)
    {
        if (OwnedWindows.Count > 0 || Jornal.Infrastructure.Diagnostics.AppDiagnostics.Current is not { } service) return;
        service.AcknowledgePreviousFatal();
        DiagnosticsButton.Label = "Диагностика";
        await ShowSettingsAsync(SettingsSection.Diagnostics);
    }
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
        if (_viewModel?.CanUseJournal != true || OwnedWindows.Count > 0 || e.Source is not Control control ||
            control is CheckBox || control.GetVisualAncestors().OfType<CheckBox>().Any()) return;
        var row = control as DataGridRow ?? control.FindAncestorOfType<DataGridRow>();
        if (row?.DataContext is not OrderRowViewModel target || !_viewModel.Orders.Contains(target)) return;
        OrdersGrid.SelectedItems.Clear();
        OrdersGrid.SelectedItem = target;
        UpdateSelection();
        e.Handled = true;
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
    private void UpdateSelection() =>
        _viewModel?.UpdateSelection(OrdersGrid.SelectedItems.Cast<OrderRowViewModel>(),
            OrdersGrid.SelectedItem as OrderRowViewModel);

    private void OnClearSelection(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CanClearSelection != true) return;
        OrdersGrid.SelectedItems.Clear();
        UpdateSelection();
        e.Handled = true;
    }

    private async void OnDeliveryNumberClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CreateDeliveryNumberForm() is not { } form) return;
        var dialog = new DeliveryNumberWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog<bool>(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e) => await ShowSettingsAsync(SettingsSection.General);

    private async void OnPricesClick(object? sender, RoutedEventArgs e)
    {
        if (OwnedWindows.Count > 0 || _viewModel?.CreatePriceEditor() is not { } form) return;
        var dialog = new PriceEditorWindow { DataContext = form, RequestedThemeVariant = ActualThemeVariant };
        await dialog.ShowDialog(this);
    }

    private async Task ShowSettingsAsync(SettingsSection section)
    {
        if (_viewModel is null || OwnedWindows.Count > 0) return;
        var form = new SettingsViewModel(_viewModel, Jornal.Infrastructure.Diagnostics.AppDiagnostics.Current, section);
        var dialog = new SettingsWindow { DataContext = form };
        await dialog.ShowDialog(this);
        if (Jornal.Infrastructure.Diagnostics.AppDiagnostics.Current?.HasPreviousFatal == false) DiagnosticsButton.Label = "Диагностика";
        if (dialog.CustomizeRequested && _viewModel.CanConfigureScreen) _customization.Begin();
    }

    private async void OnUpdatesClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.CanCheckUpdates != true || OwnedWindows.Count > 0) return;
        await ShowSettingsAsync(SettingsSection.Updates);
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

    private void OnDecreaseRowHeight(object? sender, RoutedEventArgs e) => ChangeRowHeight(-1);
    private void OnIncreaseRowHeight(object? sender, RoutedEventArgs e) => ChangeRowHeight(1);
    private void OnResetRowHeight(object? sender, RoutedEventArgs e) => _customization.SetRowHeight(TableDensity.DefaultHeight);

    private void ChangeRowHeight(int step) => _customization.SetRowHeight(Math.Clamp(_customization.RowHeight + step,
        TableDensity.MinimumHeight, TableDensity.MaximumHeight));

    private void OnRowHeightTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Незаконченный ввод («2» перед «24») не меняет таблицу и не сохраняется.
        if (_customization is not null && sender is TextBox input &&
            int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            value is >= (int)TableDensity.MinimumHeight and <= (int)TableDensity.MaximumHeight && value != _customization.RowHeight)
            _customization.SetRowHeight(value);
    }

    private void CommitRowHeight()
    {
        var value = _customization.RowHeight;
        if (decimal.TryParse(RowHeightInput.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entered))
            value = (double)Math.Clamp(entered, (decimal)TableDensity.MinimumHeight, (decimal)TableDensity.MaximumHeight);
        _customization.SetRowHeight(value);
    }

    private void OnRowHeightLostFocus(object? sender, RoutedEventArgs e) => CommitRowHeight();

    private void OnRowHeightKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRowHeight(); e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down) { ChangeRowHeight(e.Key == Key.Up ? 1 : -1); e.Handled = true; }
    }

    private void SetAllColumnsVisible(bool visible)
    {
        foreach (var column in OrdersGrid.Columns.Skip(1)) column.IsVisible = visible;
    }
}
