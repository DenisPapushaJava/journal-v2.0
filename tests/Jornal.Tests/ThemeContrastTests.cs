using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Jornal.Desktop.Controls;
using Xunit;

namespace Jornal.Tests;

public sealed class ThemeContrastTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticTextAndControlsHaveReadableContrast(bool dark)
    {
        var window = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show(); Pump();
            foreach (var background in new[] { "JournalBackground", "JournalSurface", "JournalInput", "JournalRaised",
                         "JournalSelection", "JournalRowHover", "JournalAccentSoft", "JournalControlPressed" })
                foreach (var text in new[] { "JournalText", "JournalMuted", "JournalSubtle" })
                    Readable(Resource(window, text), Resource(window, background), 4.5, $"{text}/{background}");
            foreach (var accent in new[] { "JournalAccent", "JournalAccentHover", "JournalAccentPressed" })
                Readable(Resource(window, "JournalOnAccent"), Resource(window, accent), 4.5, accent);
            foreach (var background in new[] { "JournalBackground", "JournalSurface", "JournalInput" })
                Readable(Resource(window, "JournalControlBorder"), Resource(window, background), 3, background);
            foreach (var prefix in new[] { "Work", "Ready", "Shipped", "Neutral", "JournalDanger" })
                Readable(Resource(window, prefix + "Foreground"), Resource(window, prefix + "Background"), 4.5, prefix);
            if (!dark)
                foreach (var key in new[] { "JournalBackground", "JournalSurface", "JournalInput", "JournalRaised" })
                {
                    var color = Resource(window, key);
                    Assert.True(color.R < color.B && color.R < 250 && color.G < 250, $"{key}: {color}");
                }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldsKeepPaletteOnHoverFocusSelectionErrorAndThemeChange(bool dark)
    {
        var input = new TextBox { Text = "Материал 5083H111", MinHeight = 36 };
        var placeholder = new TextBox { PlaceholderText = "Поиск заявок", MinHeight = 36 };
        var combo = new ComboBox { ItemsSource = new[] { "Лазер", "Плазма", "HansLaser" }, SelectedIndex = 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        var check = new CheckBox { Content = "Использовать свой цвет", IsChecked = true };
        var button = new JournalButton { Label = "Рассчитать", Icon = "refresh-cw" };
        var primary = new JournalButton { Label = "Сохранить", Classes = { "primary" } };
        var window = new Window
        {
            Width = 560, Height = 430, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new Border
            {
                Margin = new Thickness(20), Padding = new Thickness(20), Classes = { "panel" },
                Child = new StackPanel { Spacing = 12, Children = { input, placeholder, combo, check, button, primary } }
            }
        };
        try
        {
            window.Show(); Pump(); window.MouseMove(new Point(1, 1));
            foreach (var theme in new[] { dark, !dark })
            {
                window.RequestedThemeVariant = theme ? ThemeVariant.Dark : ThemeVariant.Light; Pump();
                var border = Part<Border>(input, "PART_BorderElement");
                Assert.Equal(Resource(window, "JournalInput"), ColorOf(border.Background));
                Readable(ColorOf(border.BorderBrush), ColorOf(border.Background), 3, "Обычное поле");
                Readable(ColorOf(input.Foreground), ColorOf(border.Background), 4.5, "Текст поля");
                var hint = Part<TextBlock>(placeholder, "PART_Placeholder");
                Assert.Equal(1, hint.Opacity);
                Readable(ColorOf(hint.Foreground), Resource(window, "JournalInput"), 4.5, "Поиск");
                Move(window, input);
                Assert.Equal(Resource(window, "JournalInput"), ColorOf(border.Background));
                Assert.Equal(Resource(window, "JournalAccent"), ColorOf(border.BorderBrush));
                Readable(ColorOf(input.Foreground), ColorOf(border.Background), 4.5, "Поле при наведении");
                input.Focus(); Pump(); input.SelectAll();
                Assert.Equal(Resource(window, "JournalInput"), ColorOf(border.Background));
                Readable(ColorOf(input.Foreground), ColorOf(border.Background), 4.5, "Поле с фокусом");
                Assert.Equal(Resource(window, "JournalText"), ColorOf(input.CaretBrush));
                Readable(ColorOf(input.SelectionForegroundBrush), ColorOf(input.SelectionBrush), 4.5, "Выделенный текст");
                input.Classes.Add("invalid"); Pump();
                Assert.Equal(Resource(window, "JournalDangerForeground"), ColorOf(border.BorderBrush));
                input.Classes.Remove("invalid");
                Move(window, combo);
                Assert.Equal(Resource(window, "JournalInput"), ColorOf(Part<Border>(combo, "Background").Background));
                combo.Focus(NavigationMethod.Tab); Pump();
                var highlight = Part<Border>(combo, "HighlightBackground");
                Assert.True(highlight.IsVisible);
                Assert.Equal(Resource(window, "JournalAccent"), ColorOf(highlight.BorderBrush));
                Readable(ColorOf(Part<ContentControl>(combo, "ContentPresenter").Foreground), Resource(window, "JournalInput"), 4.5, "Выбранный станок");
                Move(window, check);
                Readable(ColorOf(Part<ContentPresenter>(check, "PART_ContentPresenter").Foreground), Resource(window, "JournalSurface"), 4.5, "Текст чекбокса");
                check.IsChecked = false; Pump();
                Readable(ColorOf(Part<Border>(check, "NormalRectangle").BorderBrush), Resource(window, "JournalInput"), 3, "Пустой чекбокс");
                check.IsChecked = true;
                Move(window, button);
                Readable(ColorOf(button.Foreground), ColorOf(button.Background), 4.5, "Команда при наведении");
                button.Focus(NavigationMethod.Tab); Pump();
                Assert.Null(button.FocusAdorner);
                Assert.Equal(Resource(window, "JournalAccent"), ColorOf(button.BorderBrush));
                Move(window, primary);
                Readable(ColorOf(primary.Foreground), ColorOf(primary.Background), 4.5, "Основная команда");
                primary.Focus(NavigationMethod.Tab); Pump();
                Readable(ColorOf(primary.BorderBrush), ColorOf(primary.Background), 3, "Фокус основной команды");
                input.IsEnabled = false; Pump();
                Assert.Equal(Resource(window, "JournalRaised"), ColorOf(border.Background));
                input.IsEnabled = true; button.Focus(); window.MouseMove(new Point(1, 1)); Pump();
                Save(window, $"theme-controls-{theme}");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DateInputStaysTintedAndHasOneVisibleFocusBorder(bool dark)
    {
        var date = new CalendarDatePicker { SelectedDate = new DateTime(2026, 10, 17), Width = 220 };
        var other = new JournalButton { Label = "Сегодня" };
        var window = new Window { Width = 460, Height = 200, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = new StackPanel { Spacing = 12, Children = { date, other } } };
        try
        {
            window.Show(); Pump();
            var input = Part<TextBox>(date, "PART_TextBox");
            other.Focus(); Move(window, input);
            var inner = Part<Border>(input, "PART_BorderElement");
            Assert.Equal(Colors.Transparent, ColorOf(inner.Background));
            input.Focus(NavigationMethod.Tab); Pump();
            Assert.Equal(Colors.Transparent, ColorOf(inner.Background));
            Assert.Equal(new Thickness(0), inner.BorderThickness);
            var outer = date.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Background" && border.TemplatedParent == date);
            Assert.Equal(Resource(window, "JournalInput"), ColorOf(outer.Background));
            Assert.Equal(Resource(window, "JournalAccent"), ColorOf(outer.BorderBrush));
            Readable(ColorOf(input.Foreground), ColorOf(outer.Background), 4.5, "Ввод даты");
            Save(window, $"theme-date-{dark}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PopupUsesJournalSurfaceAndSelectedItemRemainsReadable(bool dark)
    {
        var combo = new ComboBox { ItemsSource = new[] { "Все поля", "Заказчик", "Материал" }, SelectedIndex = 1, Width = 220 };
        var window = new Window { Width = 480, Height = 300, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = combo };
        try
        {
            window.Show(); Pump(); combo.IsDropDownOpen = true; Pump();
            var popup = Part<Popup>(combo, "PART_Popup");
            var root = Assert.IsType<Border>(popup.Child);
            Assert.Equal(Resource(window, "JournalSurface"), ColorOf(root.Background));
            Readable(ColorOf(root.BorderBrush), ColorOf(root.Background), 3, "Меню");
            var item = Assert.IsType<ComboBoxItem>(combo.ContainerFromIndex(1));
            var presenter = Part<ContentPresenter>(item, "PART_ContentPresenter");
            Readable(ColorOf(presenter.Foreground), ColorOf(presenter.Background), 4.5, "Выбранный пункт");
            Save(TopLevel.GetTopLevel(root)!, $"theme-popup-{dark}");
            combo.IsDropDownOpen = false;
        }
        finally { window.Close(); }
    }

    private static T Part<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static Color Resource(Control root, string key) => ColorOf((IBrush?)root.FindResource(root.ActualThemeVariant, key));
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void Pump() => Dispatcher.UIThread.RunJobs();
    private static void Move(Window window, Control control)
    {
        window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value); Pump();
    }
    private static void Readable(Color text, Color background, double minimum, string label)
    {
        static double Luminance(Color c)
        {
            static double Linear(byte x) => x / 255d <= 0.04045 ? x / 255d / 12.92 : Math.Pow((x / 255d + .055) / 1.055, 2.4);
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        var first = Luminance(text); var second = Luminance(background);
        var ratio = (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        Assert.True(ratio >= minimum, $"{label}: {text}/{background} = {ratio:F2}:1 < {minimum}:1");
    }
    private static void Save(TopLevel window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("JOURNAL_SCREENSHOT_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1));
        bitmap.Render(window); bitmap.Save(Path.Combine(directory, name + ".png"), new PngBitmapEncoderOptions());
    }
}
