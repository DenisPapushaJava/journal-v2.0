using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Jornal.Desktop.Updates;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Views;

public partial class AppUpdatesWindow : Window
{
    public AppUpdatesWindow() => InitializeComponent();
    protected override void OnOpened(EventArgs e) { base.OnOpened(e); CheckUpdatesButton.Focus(); }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is AppUpdatesViewModel { IsApplying: true, RestartScheduled: false }) e.Cancel = true;
        base.OnClosing(e);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    private async void OnOpenReleasesClick(object? sender, RoutedEventArgs e)
    {
        try { await Launcher.LaunchUriAsync(new Uri(VelopackAppUpdater.ReleasesUrl)); }
        catch (Exception) { if (DataContext is AppUpdatesViewModel model) model.Error = "Не удалось открыть браузер. Выпуски доступны в репозитории journal-v2.0 на GitHub."; }
    }
}
