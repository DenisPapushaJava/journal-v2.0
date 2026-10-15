using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SvgControl = Avalonia.Svg.Svg;

namespace Jornal.Desktop.Controls;

public sealed class JournalButton : Button
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<JournalButton, string?>(nameof(Label));
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<JournalButton, string?>(nameof(Icon));

    private readonly SvgControl _icon = new(new Uri("avares://Jornal.Desktop/"))
    {
        Width = 16, Height = 16, Stretch = Stretch.Uniform, IsHitTestVisible = false, IsVisible = false
    };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

    public JournalButton()
    {
        Classes.Add("command");
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { _icon, _label }
        };
    }

    protected override Type StyleKeyOverride => typeof(Button);
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LabelProperty)
        {
            _label.Text = Label;
            Avalonia.Automation.AutomationProperties.SetName(this, Label ?? string.Empty);
        }
        if (change.Property == IconProperty)
        {
            _icon.IsVisible = !string.IsNullOrEmpty(Icon);
            _icon.Path = string.IsNullOrEmpty(Icon) ? null : $"avares://Jornal.Desktop/Assets/Icons/{Icon}.svg";
        }
        if (change.Property == ForegroundProperty && Foreground is ISolidColorBrush brush)
        {
            SvgControl.SetCurrentColor(_icon, brush.Color);
        }
    }
}
