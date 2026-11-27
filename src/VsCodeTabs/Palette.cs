namespace VsCodeTabs;

internal readonly record struct Palette(
    Color Background,
    Color Foreground,
    Color Edge,
    Color ActiveBackground,
    Color ActiveBorder,
    Color HoverBackground,
    Color ButtonHover,
    Color ActiveText,
    Color InactiveText,
    Color DetailText,
    Color Separator)
{
    const int MinEdgeDistance = 80;
    const int MaxEdgeDistance = 200;

    public static Palette From(Color background, Color foreground, Color edge)
    {
        var ink = background.GetBrightness() < 0.5f ? Color.White : Color.Black;
        var surface = !edge.IsEmpty
            && Distance(edge, background) is >= MinEdgeDistance and <= MaxEdgeDistance
            && Distance(edge, ink) < Distance(background, ink)
            ? edge
            : Color.Empty;
        Color Fill(float toEdge, float toInk) => surface.IsEmpty ? Blend(background, ink, toInk) : Blend(background, surface, toEdge);
        Color Text(float toForeground, float toInk) => foreground.IsEmpty ? Blend(background, ink, toInk) : Blend(background, foreground, toForeground);
        return new Palette(
            background,
            foreground,
            edge,
            Fill(0.40f, 0.10f),
            surface,
            Fill(0.25f, 0.05f),
            Blend(background, ink, 0.16f),
            Text(1.00f, 0.92f),
            Text(0.78f, 0.55f),
            Text(0.55f, 0.40f),
            Fill(0.20f, 0.08f));
    }

    static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));
}
