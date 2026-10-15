using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class NewOrderWindow : Window
{
    private NewOrderViewModel? _viewModel;

    public NewOrderWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.Saved -= OnSaved;
            _viewModel = DataContext as NewOrderViewModel;
            if (_viewModel is not null) _viewModel.Saved += OnSaved;
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.GetVisualDescendants().OfType<OrderEntryControl>().First().FocusInput();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel?.SaveCommand.IsRunning == true && !_viewModel.IsSaved) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null) _viewModel.Saved -= OnSaved;
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _viewModel?.SaveCommand.IsRunning != true)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnSaved(object? sender, EventArgs e) => Close(_viewModel?.CreatedOrder);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
    private void OnSaveClick(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() =>
        this.GetVisualDescendants().OfType<OrderEntryControl>()
            .FirstOrDefault(field => field.DataContext is OrderEntryField { HasError: true })?.FocusInput());
}
