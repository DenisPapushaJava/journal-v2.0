using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class EditOrderWindow : Window
{
    private EditOrderViewModel? _viewModel;

    public EditOrderWindow()
    {
        InitializeComponent();
        ConfigureFileDrop();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) { _viewModel.Saved -= OnSaved; _viewModel.PropertyChanged -= OnImportStateChanged; }
            _viewModel = DataContext as EditOrderViewModel;
            if (_viewModel is not null) { _viewModel.Saved += OnSaved; _viewModel.PropertyChanged += OnImportStateChanged; }
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowSizing.FitToWorkingArea(this);
        if (_viewModel is not null) _ = _viewModel.LoadMachinesAsync(_importLifetime.Token);
        this.GetVisualDescendants().OfType<OrderEntryControl>().First().FocusInput();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel?.SaveCommand.IsRunning == true && _viewModel.SavedRecord is null) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _importLifetime.Cancel();
        _importLifetime.Dispose();
        if (_viewModel is not null)
        {
            _viewModel.CancelImportRead();
            _viewModel.CalculateCommand.Cancel();
            _viewModel.RefreshCommand.Cancel();
            _viewModel.Saved -= OnSaved;
            _viewModel.PropertyChanged -= OnImportStateChanged;
        }
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F5 && _viewModel?.CanRefresh == true) { _viewModel.RefreshCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Escape && _viewModel?.SaveCommand.IsRunning != true)
        {
            if (_viewModel?.IsImporting == true) _viewModel.CancelImportRead();
            else if (_viewModel?.HasPendingImport == true) _viewModel.CancelImportReplacementCommand.Execute(null);
            else Close();
            e.Handled = true;
        }
    }

    private void OnSaved(object? sender, EventArgs e) => Close(_viewModel?.SavedRecord);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
    private void OnSaveClick(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
        this.GetVisualDescendants().OfType<OrderEntryControl>()
            .FirstOrDefault(field => field.DataContext is OrderEntryField { HasError: true })?.FocusInput());
}
