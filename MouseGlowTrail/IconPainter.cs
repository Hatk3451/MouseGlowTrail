using System.Runtime.InteropServices;

namespace MouseGlowTrail;

/// <summary>
/// Paints the brand mark (a comet swoosh in the current style's colours) and the menu swatches
/// with the trail rasteriser itself. Shapes use hard edges and rely on 4x supersampling for
/// antialiasing, so the mark stays crisp down to 16 px.
/// </summary>
internal static unsafe class IconPainter
{
    private static readonly ProfileLut Solid = new(1.05f, u => Math.Clamp((1f - u) * 40f + 0.5f, 0f, 1f), _ => 0f);

    private static readonly ProfileLut HeadCore = new(1.05f, u => Math.Clamp((1f - u) * 40f + 0.5f, 0f, 1f),
        u => 0.62f * MathF.Exp(-2.4f * u * u));

    // Compact glow that fully decays inside the icon's margin, so it is never clipped square.
    private static readonly ProfileLut Glow = new(1.9f, u => 0.32f * MathF.Exp(-u * u), _ => 0.12f);

    // Pale styles (Moonlight) are nudged towards steel blue so the mark still reads on light taskbars.
    private const uint PaleAccent = 0x7F93C9;

    /// <summary>Straight-alpha BGRA pixels of the comet mark; muted renders the paused, greyed state.</summary>
    public static byte[] RenderIcon(int size, TrailStyle style, bool muted) =>
        Render(size, size <= 64 ? 4 : 2, muted, premultiplied: false, (canvas, s) => PaintComet(canvas, s, style, size));

    /// <summary>
    /// Premultiplied BGRA colour chip for the style menu. The current style gets a neutral ring,
    /// the colour-picker convention, because a menu bitmap takes the place of the radio dot.
    /// </summary>
    public static byte[] RenderSwatch(int size, TrailStyle style, bool selected) =>
        Render(size, 4, muted: false, premultiplied: true, (canvas, s) => PaintSwatch(canvas, s, style, selected));

    private static uint MarkColor(TrailStyle style, float phase)
    {
        var color = style.ColorAt(phase);
        var luminance = Palette.Luminance(color);
        return luminance > 0.62f ? Palette.Mix(color, PaleAccent, Math.Min(0.6f, (luminance - 0.62f) * 2.2f)) : color;
    }

    private static void PaintComet(Canvas canvas, float s, TrailStyle style, int size)
    {
        var small = size <= 24;
        var withGlow = size >= 40;
        // Small sizes get a bolder, shorter mark: legibility beats elegance at 16 px.
        var tailRadius = (small ? 0.035f : 0.018f) * s;
        var headRibbonRadius = (small ? 0.105f : 0.086f) * s;
        var headRadius = (small ? 0.15f : 0.118f) * s;
        float tailX = 0.83f * s, tailY = 0.85f * s, controlX = 0.79f * s, controlY = 0.31f * s;
        float headX = 0.3f * s, headY = 0.3f * s;
        const int Steps = 56;

        for (var pass = withGlow ? 0 : 1; pass < 2; pass++)
        {
            float previousX = tailX, previousY = tailY;
            for (var i = 1; i <= Steps; i++)
            {
                var t = (float)i / Steps;
                var v = 1f - t;
                var x = v * v * tailX + 2f * v * t * controlX + t * t * headX;
                var y = v * v * tailY + 2f * v * t * controlY + t * t * headY;
                var radius = tailRadius + (headRibbonRadius - tailRadius) * MathF.Pow(t, 1.25f);
                var color = MarkColor(style, 0.5f * (1f - t));
                if (pass == 0)
                {
                    canvas.Capsule(previousX, previousY, x, y, radius * 1.4f, 0.55f * t, color, Glow);
                }
                else
                {
                    canvas.Capsule(previousX, previousY, x, y, radius, 0.3f + 0.7f * MathF.Pow(t, 0.7f), color, Solid);
                }

                previousX = x;
                previousY = y;
            }
        }

        var headColor = MarkColor(style, 0f);
        if (withGlow)
        {
            canvas.Capsule(headX, headY, headX, headY, headRadius * 1.25f, 1f, headColor, Glow);
        }

        canvas.Capsule(headX, headY, headX, headY, headRadius, 1f, headColor, HeadCore);
        if (size >= 48)
        {
            canvas.Sparkle(0.63f * s, 0.67f * s, 0.045f * s, 1f, MarkColor(style, 0.22f), 0.85f);
        }
    }

    private static void PaintSwatch(Canvas canvas, float s, TrailStyle style, bool selected)
    {
        var centre = 0.5f * s;
        var radius = (selected ? 0.27f : 0.33f) * s;
        if (selected)
        {
            canvas.Ring(centre, centre, 0.42f * s, 0.035f * s, 1f, 0x8A8A8A, 0f);
        }

        // Diagonal gradient across the chip: concentric rings coloured by angle would band, so each
        // horizontal chord is drawn in the colour of its position along the diagonal.
        const int Chords = 48;
        for (var i = 0; i <= Chords; i++)
        {
            var y = centre - radius + 2f * radius * i / Chords;
            var half = MathF.Sqrt(MathF.Max(0f, radius * radius - (y - centre) * (y - centre)));
            const int Pieces = 12;
            var previous = centre - half;
            for (var k = 1; k <= Pieces; k++)
            {
                var x = centre - half + 2f * half * k / Pieces;
                var along = Math.Clamp(((x - centre) + (y - centre)) / (4f * radius) + 0.5f, 0f, 1f);
                canvas.Capsule(previous, y, x, y, radius / Chords * 1.6f, 1f, MarkColor(style, 0.5f * along), Solid);
                previous = x;
            }
        }
    }

    private static byte[] Render(int size, int supersample, bool muted, bool premultiplied,
        Action<Canvas, float> paint)
    {
        var side = size * supersample;
        var buffer = (uint*)NativeMemory.AllocZeroed((nuint)(side * side), sizeof(uint));
        try
        {
            var canvas = new Canvas();
            canvas.Attach(buffer, side, side);
            canvas.BeginFrame(side, side);
            paint(canvas, side);

            var result = new byte[size * size * 4];
            var inverse = 1f / (supersample * supersample);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    float a = 0, r = 0, g = 0, b = 0;
                    for (var sy = 0; sy < supersample; sy++)
                    {
                        var row = buffer + (long)(y * supersample + sy) * side + x * supersample;
                        for (var sx = 0; sx < supersample; sx++)
                        {
                            var pixel = row[sx];
                            a += pixel >> 24;
                            r += (pixel >> 16) & 255;
                            g += (pixel >> 8) & 255;
                            b += pixel & 255;
                        }
                    }

                    a *= inverse;
                    r *= inverse;
                    g *= inverse;
                    b *= inverse;
                    if (muted)
                    {
                        // Paused: a neutral mid grey that reads on both light and dark taskbars.
                        var grey = (0.2126f * r + 0.7152f * g + 0.0722f * b) * 0.62f;
                        r = g = b = grey;
                        a *= 0.8f;
                        r *= 0.8f;
                        g *= 0.8f;
                        b *= 0.8f;
                    }

                    if (a < 0.5f)
                    {
                        continue;
                    }

                    var unpremultiply = premultiplied ? 1f : 255f / a;
                    var offset = (y * size + x) * 4;
                    result[offset] = (byte)Math.Min(255f, b * unpremultiply + 0.5f);
                    result[offset + 1] = (byte)Math.Min(255f, g * unpremultiply + 0.5f);
                    result[offset + 2] = (byte)Math.Min(255f, r * unpremultiply + 0.5f);
                    result[offset + 3] = (byte)Math.Min(255f, a + 0.5f);
                }
            }

            return result;
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }
}
