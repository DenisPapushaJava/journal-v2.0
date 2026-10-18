using Avalonia.Media;
using Jornal.Core.Orders;
using Jornal.Desktop.Tables;
using Xunit;

namespace Jornal.Tests;

public sealed class RowContrastTests
{
    [Fact]
    public void HoverDoesNotChangeSelectedTextColor()
    {
        var surface = Color.Parse("#F0F4F8");
        RowAppearance State(bool hovered) => RowAppearance.Resolve(unchecked((int)0xFFB9D5AC), true, hovered,
            false, 50, surface, Colors.Black, Colors.DarkCyan, surface);
        var selected = State(false);
        var hovered = State(true);
        Assert.Equal(selected.Foreground, hovered.Foreground);
        Assert.True(RowAppearance.Contrast(hovered.Foreground, hovered.Background) >= 4.5);
        Assert.NotEqual(selected.Background, hovered.Background);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RgbPaletteLatticeKeepsSafeMinimumAndMonotonicHighlightAcrossTheSlider(bool dark)
    {
        var surface = Color.Parse(dark ? "#20252D" : "#F0F4F8");
        var text = Color.Parse(dark ? "#E5E9EF" : "#253141");
        var selection = Colors.DarkCyan;
        var hover = Color.Parse(dark ? "#2B3542" : "#E2EBF5");
        int?[] special = [null, OrderColors.None, OrderColors.Work, OrderColors.Ready, OrderColors.Shipped,
            unchecked((int)0xFFB9D5AC), unchecked((int)0xFF3E566D), unchecked((int)0x0080FF00), 0x4080FF00,
            unchecked((int)surface.ToUInt32()), unchecked((int)0xFFFEFEFE), unchecked((int)0xFF010101)];
        var palette = from r in Enumerable.Range(0, 18) from g in Enumerable.Range(0, 18) from b in Enumerable.Range(0, 18)
                      select (int?)unchecked((int)Color.FromRgb((byte)(r * 15), (byte)(g * 15), (byte)(b * 15)).ToUInt32());
        foreach (var stored in special.Concat(palette))
        {
            RowAppearance State(bool selected, bool hovered, double strength, bool focused = false) =>
                RowAppearance.Resolve(stored, selected, hovered, focused, strength, surface, text, selection, hover);
            var plain = State(false, false, 0);
            if (stored is null or OrderColors.None) Assert.Equal(surface, plain.Background);
            else if (Color.FromUInt32(unchecked((uint)stored.Value)) is { A: 255 } opaque)
                Assert.Equal(opaque, plain.Background);
            double previousHover = 1, previousSelection = 1;
            foreach (var strength in new[] { 0d, 25, 50, 75, 100 })
            {
                var hovered = State(false, true, strength);
                var selected = State(true, false, strength);
                var both = State(true, true, strength);
                foreach (var state in new[] { plain, hovered, selected, both, State(false, false, strength, focused: true) })
                    Assert.True(RowAppearance.Contrast(state.Foreground, state.Background) >= 4.5, $"Текст: {stored:X8}, {state}");
                var hoverContrast = RowAppearance.Contrast(plain.Background, hovered.Background);
                var selectedContrast = RowAppearance.Contrast(plain.Background, selected.Background);
                Assert.True(hoverContrast >= 1.12 && hoverContrast + .005 >= previousHover, $"Наведение: {stored:X8}, сила {strength}");
                Assert.True(selectedContrast >= 1.5 && selectedContrast + .005 >= previousSelection, $"Выбор: {stored:X8}, сила {strength}");
                Assert.True(selectedContrast >= hoverContrast * 1.15);
                // A hover may be limited near the text contrast boundary; it must keep the
                // selected text color and never weaken the underlying selection.
                Assert.Equal(selected.Foreground, both.Foreground);
                Assert.True(RowAppearance.Contrast(plain.Background, both.Background) + .005 >= selectedContrast);
                Assert.NotEqual(hovered.Background, selected.Background);
                Assert.NotEqual(selected.Background, both.Background);
                Assert.Equal(plain.Background, State(false, false, strength).Background);
                Assert.Equal(hovered, State(false, false, strength, focused: true));
                Assert.Equal(selected, State(true, false, strength, focused: true));
                Assert.All(new[] { plain, hovered, selected, both }, state => Assert.Equal(255, state.Background.A));
                previousHover = hoverContrast; previousSelection = selectedContrast;
            }
            Assert.True(RowAppearance.Contrast(plain.Background, State(false, true, 100).Background) >= 1.65);
            Assert.True(RowAppearance.Contrast(plain.Background, State(true, false, 100).Background) >= 3.4);
            Assert.True(RowAppearance.Contrast(plain.Background, State(true, true, 100).Background) >= 3.4);
        }
    }

    [Theory]
    [InlineData("#FF9DFF", "#995E99")]
    [InlineData("#B9D5AC", "#6F7F67")]
    [InlineData("#F28A8A", "#915252")]
    public void StandardSelectionUsesTheLegacyMultiplierAndHoverIsMilder(string source, string selectedColor)
    {
        var surface = Color.Parse("#F0F4F8"); var stored = Color.Parse(source);
        RowAppearance State(bool selected, bool hovered) => RowAppearance.Resolve(unchecked((int)stored.ToUInt32()),
            selected, hovered, false, 50, surface, Colors.Black, Colors.DarkCyan, surface);
        var plain = State(false, false).Background;
        var hover = State(false, true).Background;
        var selected = State(true, false).Background;
        Assert.Equal(stored, plain);
        Assert.Equal(Color.Parse(selectedColor), selected);
        Assert.True(hover.R < plain.R && hover.G < plain.G && hover.B < plain.B);
        Assert.True(hover.R > selected.R && hover.G > selected.G && hover.B > selected.B);
    }

    [Theory]
    [InlineData("#101010")]
    [InlineData("#3E566D")]
    [InlineData("#1010FF")]
    public void DarkMarksAreLightenedAndRemainReadable(string source)
    {
        var stored = Color.Parse(source);
        var state = RowAppearance.Resolve(unchecked((int)stored.ToUInt32()), true, false, false, 50,
            Color.Parse("#20252D"), Colors.White, Colors.DarkCyan, Colors.Gray);
        Assert.True(state.Background.R > stored.R && state.Background.G > stored.G && state.Background.B >= stored.B);
        Assert.True(RowAppearance.Contrast(state.Foreground, state.Background) >= 4.5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnmarkedSelectionKeepsTheFamiliarDarkCyanAtTheStandardStrength(bool dark)
    {
        var state = RowAppearance.Resolve(null, true, false, false, 50, Color.Parse(dark ? "#20252D" : "#F0F4F8"),
            Colors.Black, Colors.DarkCyan, Color.Parse(dark ? "#2B3542" : "#E2EBF5"));
        Assert.Equal(Colors.DarkCyan, state.Background);
        Assert.True(RowAppearance.Contrast(state.Foreground, state.Background) >= 4.5);
    }

    [Fact]
    public void StoredAlphaIsCompositedWithoutMakingTheRowOrTextTransparent()
    {
        var surface = Color.Parse("#20252D");
        RowAppearance State(int color) => RowAppearance.Resolve(color, false, false, false, 50,
            surface, Colors.White, surface, surface);
        var transparent = State(0x0080FF00); var partial = State(0x4080FF00);
        var opaque = State(unchecked((int)0xFF80FF00));
        Assert.Equal(surface, transparent.Background);
        Assert.InRange(partial.Background.G, surface.G + 1, opaque.Background.G - 1);
        Assert.All(new[] { transparent, partial, opaque }, state =>
        {
            Assert.Equal(255, state.Background.A); Assert.Equal(255, state.Foreground.A);
            Assert.True(RowAppearance.Contrast(state.Foreground, state.Background) >= 4.5);
        });
    }
}
