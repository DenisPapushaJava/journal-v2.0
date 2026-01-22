using System;
using System.Drawing;
using System.Windows.Forms;

public class ButtonNewOrderStyleManager : IDisposable
{
    private readonly Button _targetButton;
    private readonly Font _normalFont;
    private readonly Font _activeFont;
    private readonly Font _errorFont;

    public Color NormalColor { get; set; } = SystemColors.ActiveCaption;
    public Color ActiveColor { get; set; } = Color.LightGreen;
    public Color ErrorColor { get; set; } = Color.LightPink;


    public ButtonNewOrderStyleManager(Button targetButton)
    {
        _targetButton = targetButton ?? throw new ArgumentNullException(nameof(targetButton));
         // Инициализация шрифтов на основе текущего шрифта кнопки
        _normalFont = new Font(targetButton.Font.FontFamily, 8.25f, targetButton.Font.Style);
        _activeFont = new Font(targetButton.Font.FontFamily, 20f, targetButton.Font.Style);
        _errorFont = new Font(targetButton.Font.FontFamily, 20f, targetButton.Font.Style);

    }

    public void SetActiveAppearance()
    {
        ApplyAppearance(
            backColor: ActiveColor,
            text: "✅",
            foreColor: Color.LimeGreen,
            font: _activeFont,
            flatStyle: FlatStyle.Standard
        );
    }

    public void SetErrorAppearance()
    {
        ApplyAppearance(
            backColor: ErrorColor,
            text: "⛔",
            foreColor: Color.Red,
            font: _errorFont
        );
    }

    public void ResetAppearance()
    {
        ApplyAppearance(
            backColor: NormalColor,
            text: "Новая заявка",
            foreColor: SystemColors.ControlText,
            font: _normalFont,
            flatStyle: FlatStyle.Flat
        );
    }

    private void ApplyAppearance(
        Color backColor,
        string text,
        Color foreColor,
        Font font = null,
        Size? size = null,
        Point? position = null,
        FlatStyle? flatStyle = null)
    {
        _targetButton.SuspendLayout();

        _targetButton.BackColor = backColor;
        _targetButton.Text = text;
        _targetButton.ForeColor = foreColor;

        if (font != null)
            _targetButton.Font = font;

        if (size.HasValue)
            _targetButton.Size = size.Value;

        if (position.HasValue)
            _targetButton.Location = position.Value;

        if (flatStyle.HasValue)
            _targetButton.FlatStyle = flatStyle.Value;

        _targetButton.ResumeLayout();
    }

    public void Dispose()
    {
        _normalFont?.Dispose();
        _activeFont?.Dispose();
        _errorFont?.Dispose();
    }
}