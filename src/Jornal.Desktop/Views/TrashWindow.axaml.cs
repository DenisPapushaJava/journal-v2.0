using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class TrashWindow : Window
{
    private TrashViewModel? Model => DataContext as TrashViewModel;
    public TrashWindow() => InitializeComponent();
    protected override async void OnOpened(EventArgs e) { base.OnOpened(e); if (Model is { } model) await model.RefreshCommand.ExecuteAsync(null); }
    protected override void OnClosing(WindowClosingEventArgs e) { if (Model?.IsWriting == true) e.Cancel = true; base.OnClosing(e); }
    protected override void OnClosed(EventArgs e) { Model?.RefreshCommand.Cancel(); base.OnClosed(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    { base.OnKeyDown(e); if (e.Key == Key.Escape && Model?.IsWriting != true) { Close(); e.Handled = true; } }
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private async void OnRestoreClick(object? sender, RoutedEventArgs e) => await ConfirmAsync(Model?.CreateRestoreConfirmation());
    private async void OnPurgeClick(object? sender, RoutedEventArgs e) => await ConfirmAsync(Model?.CreatePurgeConfirmation());
    private async Task ConfirmAsync(OrderConfirmationViewModel? model)
    {
        if (model is null) return;
        var dialog = new OrderConfirmationWindow { DataContext = model, RequestedThemeVariant = ActualThemeVariant };
        if (await dialog.ShowDialog<bool>(this) && Model is { } trash) await trash.RefreshCommand.ExecuteAsync(null);
    }
}
