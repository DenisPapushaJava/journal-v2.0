using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Jornal.Core.Orders;

namespace Jornal.Desktop.Controls;

/// <summary>Сохранённый ARGB задаёт фон всей строки; выбор сохраняет его оттенок.</summary>
public sealed class OrderRowBrushConverter : IMultiValueConverter
{
    public bool Foreground { get; set; }

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 5) return AvaloniaProperty.UnsetValue;
        var selected = values[1] is true;
        var hovered = values[2] is true;
        var focused = values[3] is true;
        var theme = values[4] as ThemeVariant ?? ThemeVariant.Light;
        Color Resource(string key) => ((ISolidColorBrush)Application.Current!.FindResource(theme, key)!).Color;
        var background = Resource("JournalSurface");
        if (values[0] is int argb && argb != OrderColors.None)
        {
            var stored = Color.FromUInt32(unchecked((uint)argb));
            background = Mix(background, stored, stored.A / 255d);
            // Как в WinForms: выбранная цветная строка темнее. Для очень тёмных
            // цветов осветляем, иначе чёрный вообще не показывал бы выбор/hover.
            var light = Luminance(background) > 0.18;
            var amount = selected ? (hovered ? 0.34 : 0.26) : hovered || focused ? 0.09 : 0;
            background = Mix(background, light ? Colors.Black : Colors.White, amount);
            if (Foreground)
                return new SolidColorBrush(Luminance(background) > 0.179 ? Colors.Black : Colors.White);
        }
        else
        {
            if (Foreground) return new SolidColorBrush(Resource("JournalText"));
            background = Resource(selected ? "JournalSelection" : hovered || focused ? "JournalRowHover" : "JournalSurface");
            if (selected && hovered) background = Mix(background, Resource("JournalAccent"), 0.08);
        }
        return new SolidColorBrush(background);
    }

    private static Color Mix(Color first, Color second, double amount) => Color.FromRgb(
        (byte)Math.Round(first.R + (second.R - first.R) * amount),
        (byte)Math.Round(first.G + (second.G - first.G) * amount),
        (byte)Math.Round(first.B + (second.B - first.B) * amount));

    private static double Luminance(Color color)
    {
        static double Linear(byte channel) => channel / 255d <= 0.04045
            ? channel / 255d / 12.92 : Math.Pow((channel / 255d + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
