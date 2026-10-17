using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Jornal.Tests;

internal static class SettingsTestControls
{
    // Страницы настроек имеют собственные XAML NameScope.
    internal static T? FindDescendant<T>(this Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name);
}
