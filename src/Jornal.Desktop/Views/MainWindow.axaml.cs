using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
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
        if (_viewModel is not null) await _viewModel.ReloadCommand.ExecuteAsync(null);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ReloadCommand.Cancel();
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ConnectViewModel()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelChanged;
        UpdateTheme();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme)) UpdateTheme();
    }

    private void UpdateTheme() => RequestedThemeVariant = _viewModel?.IsDarkTheme == true
        ? ThemeVariant.Dark : ThemeVariant.Light;

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void OnCurrentCellChanged(object? sender, EventArgs e) => UpdateSelection();
    private void UpdateSelection() => _viewModel?.UpdateSelection(
        OrdersGrid.SelectedItems.Cast<OrderRowViewModel>(), OrdersGrid.SelectedItem as OrderRowViewModel);
}
