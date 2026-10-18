using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

namespace Jornal.Tests;

internal static class TableStatusTestActions
{
    // Прежние сценарии записи теперь проходят через оставшийся пользовательский вход — меню строки.
    internal static bool TryExecute(Window window, string formerButton)
    {
        var name = formerButton switch { "SetWorkButton" => "OrderMenuWork", "SetReadyButton" => "OrderMenuReady",
            "SetShippedButton" => "OrderMenuShipped", _ => null };
        if (name is null) return false;
        var table = window.FindControl<DataGrid>("OrdersGrid")!;
        table.Focus();
        window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        window.KeyRelease(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        Dispatcher.UIThread.RunJobs();
        var menu = table.ContextMenu!; Assert.True(menu.IsOpen);
        var group = menu.Items.OfType<MenuItem>().Single(item => item.Name == "OrderMenuColors");
        var action = group.Items.OfType<MenuItem>().Single(item => item.Name == name);
        Assert.True(group.IsEnabled); Assert.True(action.IsEnabled);
        action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        return true;
    }
}
