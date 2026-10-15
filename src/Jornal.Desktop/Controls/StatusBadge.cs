using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Jornal.Desktop.Controls;

public sealed class StatusBadge : Border
{
    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Status), "neutral");
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(Text));
    private readonly TextBlock _label = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

    public StatusBadge()
    {
        CornerRadius = new CornerRadius(6);
        Height = 24;
        Padding = new Thickness(8, 0);
        Margin = new Thickness(4, 0);
        Child = _label;
        ActualThemeVariantChanged += (_, _) => UpdateBrushes();
    }

    public string Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            _label.Text = Text;
            ToolTip.SetTip(this, Text);
        }
        if (change.Property == StatusProperty) UpdateBrushes();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateBrushes();
    }

    private void UpdateBrushes()
    {
        var prefix = Status switch { "work" => "Work", "ready" => "Ready", "shipped" => "Shipped", _ => "Neutral" };
        Background = this.FindResource(ActualThemeVariant, prefix + "Background") as IBrush;
        _label.Foreground = this.FindResource(ActualThemeVariant, prefix + "Foreground") as IBrush;
    }
}
