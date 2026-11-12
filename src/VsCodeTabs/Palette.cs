namespace VsCodeTabs;

internal readonly record struct Palette(
    Color Background,
    Color ActiveBackground,
    Color HoverBackground,
    Color ButtonHover,
    Color ActiveText,
    Color InactiveText,
    Color DetailText)
{
    public static Palette From(Color background)
    {
        var ink = background.GetBrightness() < 0.5f ? Color.White : Color.Black;
        return new Palette(
            background,
            Blend(background, ink, 0.10f),
            Blend(background, ink, 0.05f),
            Blend(background, ink, 0.16f),
            Blend(background, ink, 0.92f),
            Blend(background, ink, 0.55f),
            Blend(background, ink, 0.40f));
    }

    static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));
}
