using Avalonia.Media;
using Jornal.Desktop.ViewModels;
using Xunit;

namespace Jornal.Tests;

public sealed class ColorWheelTests
{
    [Fact]
    public async Task ExactHsvChoiceKeepsItsPrecisionAndDoesNotApplyUntilConfirmation()
    {
        var writes = 0; var saved = 0;
        var form = Form(color => { writes++; saved = color; return Task.FromResult(true); });
        var chosen = new HsvColor(1, 251.2345, .42731, .71392);
        form.SpectrumColor = chosen;
        Assert.Equal(chosen, form.SpectrumColor); Assert.Equal(unchecked((int)chosen.ToRgb().ToUInt32()), form.Argb);
        Assert.True(form.IsValid); Assert.Equal(0, writes);
        await form.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(1, writes); Assert.Equal(form.Argb, saved);
    }
    [Fact]
    public void CodesAndPresetsUpdateTheWheelAndInvalidInputDoesNotEraseTheLastValidColor()
    {
        var form = Form(); form.Code = "#FF9DFF";
        Assert.Equal(-25089, form.Argb); Assert.Equal(Color.Parse("#FF9DFF"), form.SpectrumColor.ToRgb());
        form.Select(unchecked((int)0xFF123456));
        Assert.Equal(Color.Parse("#123456"), form.SpectrumColor.ToRgb());
        var previous = form.SpectrumColor; form.Code = "#bad";
        Assert.False(form.IsValid); Assert.Equal(previous, form.SpectrumColor);
        form.SpectrumColor = new HsvColor(.2, 30, .5, .8);
        Assert.True(form.IsValid); Assert.Equal(1, form.SpectrumColor.A); Assert.Equal(255u, unchecked((uint)form.Argb) >> 24);
    }
    [Fact]
    public void BlackGrayAndWhiteRetainTheHueForSubsequentSelection()
    {
        var form = Form(); form.SpectrumColor = new(1, 217.3, .8, .7);
        foreach (var code in new[] { "#000000", "#777777", "#FFFFFF" })
        { form.Code = code; Assert.Equal(217.3, form.SpectrumColor.H); }
        form.SpectrumColor = new(1, 97.25, .5, 0);
        form.SpectrumColor = new(1, 112.5, .5, 0);
        Assert.Equal(-16777216, form.Argb); Assert.Equal(112.5, form.SpectrumColor.H);
        form.SpectrumColor = new(1, form.SpectrumColor.H, .5, .8);
        Assert.NotEqual(-16777216, form.Argb); Assert.Equal(112.5, form.SpectrumColor.H);
    }
    private static RowColorViewModel Form(Func<int, Task<bool>>? confirm = null) => new(-25089, 1, [],
        confirm ?? (_ => Task.FromResult(true)), () => null, () => true);
}
