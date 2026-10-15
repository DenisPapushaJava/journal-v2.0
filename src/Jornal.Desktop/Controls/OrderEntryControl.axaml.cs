using Avalonia.Controls;
using Jornal.Desktop.ViewModels;

namespace Jornal.Desktop.Controls;

public partial class OrderEntryControl : UserControl
{
    public OrderEntryControl() => InitializeComponent();

    public void FocusInput()
    {
        this.BringIntoView();
        if (DataContext is OrderEntryField { HasChoices: true }) Choice.Focus();
        else Input.Focus();
    }
}
