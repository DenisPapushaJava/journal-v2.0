using Avalonia.Controls;
using Avalonia.Interactivity;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Controls.Settings;

public partial class NewOrderColorPanel : UserControl
{
    public NewOrderColorPanel() => InitializeComponent();
    private void OnCodeLostFocus(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.CommitTechnologistColorCode();
    private void OnChooseColor(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { UseTechnologistColor: true } model && sender is Button { DataContext: TechnologistColorOption option })
            model.SelectTechnologistColor(option.Argb);
        TechnologistColorButton.Flyout?.Hide();
    }
}
