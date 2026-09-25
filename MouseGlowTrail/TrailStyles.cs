using System.Runtime.InteropServices;

namespace MouseGlowTrail;

internal enum TrailStyleId
{
    Aurora,
    Prism,
    Neon,
    Ember,
    Moonlight,

    /// <summary>The user's own colour stops.</summary>
    Custom
}

/// <summary>How a palette travels through its colour stops.</summary>
internal enum ColorFlow
{
    /// <summary>Round the stops in a closed loop (the last blends back into the first).</summary>
    Loop,

    /// <summary>Through the stops and back again.</summary>
    PingPong
}

/// <summary>
/// Cross-section of a glowing primitive, tabulated by squared normalised distance so the hot loop
/// needs no square roots or exponentials. Alpha is coverage; White is how far the colour is pushed
/// towards white (the hot core).
/// </summary>
internal sealed unsafe class ProfileLut
{
    public const int Size = 1024;

    /// <summary>Entries per table, including the two spare zero entries.</summary>
    public const int Stride = Size + 2;

    private static readonly List<ProfileLut> s_all = [];

    public ProfileLut(float extent, Func<float, float> alpha, Func<float, float> white)
    {
        lock (s_all)
        {
            Row = s_all.Count;
            s_all.Add(this);
        }

        Extent = extent;
        IndexScale = Size / (extent * extent);
        // Two spare entries absorb float rounding at the very edge of the reach.
        Alpha = (float*)NativeMemory.Alloc(Size + 2, sizeof(float));
        White = (float*)NativeMemory.Alloc(Size + 2, sizeof(float));
        for (var i = 0; i < Size + 2; i++)
        {
            var u = MathF.Sqrt((i + 0.5f) / IndexScale);
            Alpha[i] = i < Size ? Math.Clamp(alpha(u), 0f, 1f) : 0f;
            White[i] = i < Size ? Math.Clamp(white(u), 0f, 1f) : 0f;
        }

        // For each opacity level, how far out the profile is still visible (>= 0.35 of a level).
        // Faded tail segments then skip the part of the glow that would round to nothing.
        _reach = new float[ReachLevels];
        for (var level = 0; level < ReachLevels; level++)
        {
            var fade = (level + 1f) / ReachLevels;
            var last = 0;
            for (var i = 0; i < Size; i++)
            {
                if (Alpha[i] * fade * 255f >= 0.35f)
                {
                    last = i;
                }
            }

            _reach[level] = MathF.Min(extent, MathF.Sqrt((last + 1f) / IndexScale));
        }
    }

    private const int ReachLevels = 64;
    private readonly float[] _reach;

    /// <summary>Visible support at the given opacity, in multiples of the radius (never above Extent).</summary>
    public float ReachFor(float fade) => _reach[Math.Clamp((int)(fade * ReachLevels), 0, ReachLevels - 1)];

    /// <summary>Number of tables built so far (the GPU texture needs this many rows).</summary>
    public static int Count
    {
        get
        {
            lock (s_all)
            {
                return s_all.Count;
            }
        }
    }

    /// <summary>Every table ever built, in creation order; <see cref="Row"/> indexes this list (GPU texture row).</summary>
    public static ProfileLut[] All
    {
        get
        {
            lock (s_all)
            {
                return [.. s_all];
            }
        }
    }

    public int Row { get; }

    public float* Alpha { get; }
    public float* White { get; }

    /// <summary>Support of the profile, in multiples of the primitive's radius.</summary>
    public float Extent { get; }

    public float IndexScale { get; }
}

internal static class Profiles
{
    public static readonly ProfileLut ClickDot = new(2.4f,
        u =>
        {
            // A crisp 10 px light (radius 5 px, 1 px antialiased edge) inside a faint halo.
            var disc = 0.9f * Math.Clamp((1f - u) * 5f + 0.5f, 0f, 1f);
            var halo = 0.22f * Gaussian(u, 1.05f);
            return Union(disc, halo);
        },
        u => 0.5f * Gaussian(u, 0.55f));

    public static readonly ProfileLut Particle = new(2.6f,
        u => 0.95f * MathF.Exp(-1.05f * u * u),
        u => 0.65f * MathF.Exp(-2f * u * u));

    /// <summary>
    /// Ribbon cross-section: a flat-topped body (half intensity at the nominal radius), a wide soft
    /// glow, and a white-hot core. Extent is chosen so the glow has decayed below half a level.
    /// Tables are shared per shape, so dragging a slider back and forth builds each one only once.
    /// </summary>
    public static ProfileLut Ribbon(in RibbonShape shape)
    {
        lock (s_ribbons)
        {
            if (!s_ribbons.TryGetValue(shape, out var lut))
            {
                var (body, glow, glowSigma, core, coreSigma) = shape;
                var extent = MathF.Max(2f, glowSigma * MathF.Sqrt(MathF.Log(MathF.Max(glow * 510f, 1.05f))));
                lut = new ProfileLut(extent,
                    u => Union(body * MathF.Exp(-0.6931472f * u * u * u * u), glow * Gaussian(u, glowSigma)),
                    u => core * Gaussian(u, coreSigma));
                s_ribbons.Add(shape, lut);
            }

            return lut;
        }
    }

    private static readonly Dictionary<RibbonShape, ProfileLut> s_ribbons = [];

    private static float Gaussian(float u, float sigma) => MathF.Exp(-(u / sigma) * (u / sigma));

    private static float Union(float a, float b) => 1f - (1f - a) * (1f - b);
}

/// <summary>Parameters of a ribbon's cross-section; see <see cref="Profiles.Ribbon"/>.</summary>
internal readonly record struct RibbonShape(float Body, float Glow, float GlowSigma, float Core, float CoreSigma)
{
    /// <summary>The same shape with the glow and the white core scaled (1 = unchanged).</summary>
    public RibbonShape Scale(float glow, float core) => this with
    {
        // Quantised so that slider positions map onto a small set of shared tables.
        Glow = MathF.Round(Math.Clamp(Glow * glow, 0f, 0.9f) * 200f) / 200f,
        Core = MathF.Round(Math.Clamp(Core * core, 0f, 1f) * 200f) / 200f
    };
}

internal sealed class TrailStyle
{
    private readonly uint[] _palette;

    private readonly (string Chinese, string English) _name;
    private readonly (string Chinese, string English) _description;

    public TrailStyle(TrailStyleId id, (string, string) name, (string, string) description, float cycleLength,
        uint[] stops, ColorFlow flow, RibbonShape shape)
    {
        Id = id;
        _name = name;
        _description = description;
        CycleLength = cycleLength;
        Stops = stops;
        Flow = flow;
        Shape = shape;
        _palette = Palette.Build(stops, flow);
        Ribbon = Profiles.Ribbon(shape);
    }

    private TrailStyle(TrailStyle source, float cycleLength, RibbonShape shape)
    {
        Id = source.Id;
        _name = source._name;
        _description = source._description;
        CycleLength = cycleLength;
        Stops = source.Stops;
        Flow = source.Flow;
        Shape = shape;
        _palette = source._palette;
        Ribbon = Profiles.Ribbon(shape);
    }

    public TrailStyleId Id { get; }
    public string Name => Loc.T(_name.Chinese, _name.English);
    public string Description => Loc.T(_description.Chinese, _description.English);

    /// <summary>Pixels of pointer travel for one full trip around the palette.</summary>
    public float CycleLength { get; }

    /// <summary>The colour stops the palette is built from (0xRRGGBB).</summary>
    public uint[] Stops { get; }

    public ColorFlow Flow { get; }

    public RibbonShape Shape { get; }

    public ProfileLut Ribbon { get; }

    public uint ColorAt(float phase) => _palette[(int)(phase * Palette.Size) & (Palette.Size - 1)];

    /// <summary>This look with the user's adjustments: colour speed and glow / core strength (1 = as designed).</summary>
    public TrailStyle Adjust(float colorSpeed, float glow, float core)
    {
        var shape = Shape.Scale(glow, core);
        var cycle = CycleLength / Math.Clamp(colorSpeed, 0.05f, 20f);
        return shape == Shape && cycle == CycleLength ? this : new TrailStyle(this, cycle, shape);
    }
}

internal static class TrailStyles
{
    /// <summary>Cross-section used for custom colours (the Aurora tuning).</summary>
    public static readonly RibbonShape CustomShape = new(Body: 0.74f, Glow: 0.26f, GlowSigma: 2f, Core: 0.26f, CoreSigma: 0.32f);

    public static readonly TrailStyle[] All =
    [
        new(TrailStyleId.Aurora, ("极光", "Aurora"), ("青碧 → 粉紫", "Teal → pink violet"), 800f, [0x48DACD, 0x6FB9FF, 0xB09CFF, 0xF69FC2],
            ColorFlow.PingPong, CustomShape),
        new(TrailStyleId.Prism, ("幻彩", "Prism"), ("柔和全光谱", "Soft full spectrum"), 1600f,
            [0x5EE7DF, 0x62A8FF, 0x9C8BFF, 0xF08BE0, 0xFF9D8A, 0xFFD27A, 0x9BEA8C], ColorFlow.Loop,
            new RibbonShape(Body: 0.72f, Glow: 0.24f, GlowSigma: 1.9f, Core: 0.24f, CoreSigma: 0.32f)),
        new(TrailStyleId.Neon, ("霓虹", "Neon"), ("高亮电光", "Electric brights"), 700f, [0x00E5FF, 0x4F6BFF, 0xB84DFF, 0xFF3DC8], ColorFlow.PingPong,
            new RibbonShape(Body: 0.82f, Glow: 0.36f, GlowSigma: 2.3f, Core: 0.4f, CoreSigma: 0.32f)),
        new(TrailStyleId.Ember, ("余烬", "Ember"), ("暖金 → 玫红", "Warm gold → rose"), 900f, [0xFFD36B, 0xFF9A55, 0xFF5F6D, 0xFF4F9B], ColorFlow.PingPong,
            new RibbonShape(Body: 0.76f, Glow: 0.28f, GlowSigma: 2f, Core: 0.26f, CoreSigma: 0.32f)),
        new(TrailStyleId.Moonlight, ("月白", "Moonlight"), ("低调素雅", "Quiet and pale"), 1200f, [0xE3EAFB, 0xAFC2EE, 0xCBC3F2], ColorFlow.PingPong,
            new RibbonShape(Body: 0.64f, Glow: 0.2f, GlowSigma: 1.8f, Core: 0.45f, CoreSigma: 0.4f)),
    ];

    public static TrailStyle Get(TrailStyleId id) => (uint)id < (uint)All.Length ? All[(int)id] : All[0];

    /// <summary>A style from the user's own colour stops.</summary>
    public static TrailStyle Custom(uint[] stops, ColorFlow flow) =>
        new(TrailStyleId.Custom, ("自定义", "Custom"), ("你的配色", "Your colors"), 900f, stops.Length > 0 ? stops : All[0].Stops, flow, CustomShape);
}

/// <summary>Cyclic colour tables interpolated in OKLab, so blends stay vivid instead of turning grey.</summary>
internal static class Palette
{
    public const int Size = 512;

    public static uint[] Build(uint[] stops, ColorFlow flow)
    {
        if (stops.Length == 1)
        {
            var table = new uint[Size];
            Array.Fill(table, stops[0] & 0xFFFFFF);
            return table;
        }

        return flow == ColorFlow.Loop ? Loop(stops) : PingPong(stops);
    }

    /// <summary>Glides through the stops and back again, easing at both ends.</summary>
    public static uint[] PingPong(params uint[] stops)
    {
        var lab = stops.Select(ToOklab).ToArray();
        var table = new uint[Size];
        for (var i = 0; i < Size; i++)
        {
            var sweep = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / Size);
            var position = sweep * (lab.Length - 1);
            var index = Math.Min((int)position, lab.Length - 2);
            var t = position - index;
            var a = lab[index];
            var b = lab[index + 1];
            table[i] = FromOklab(a.L + (b.L - a.L) * t, a.A + (b.A - a.A) * t, a.B + (b.B - a.B) * t);
        }

        return table;
    }

    /// <summary>Travels round the stops in a closed loop (last blends back into first).</summary>
    public static uint[] Loop(params uint[] stops)
    {
        var lab = stops.Select(ToOklab).ToArray();
        var table = new uint[Size];
        for (var i = 0; i < Size; i++)
        {
            var position = (float)i * lab.Length / Size;
            var index = (int)position;
            var t = position - index;
            var a = lab[index];
            var b = lab[(index + 1) % lab.Length];
            table[i] = FromOklab(a.L + (b.L - a.L) * t, a.A + (b.A - a.A) * t, a.B + (b.B - a.B) * t);
        }

        return table;
    }

    /// <summary>Relative luminance (0-1) of an sRGB colour.</summary>
    public static float Luminance(uint rgb) =>
        0.2126f * ToLinear(((rgb >> 16) & 255) / 255f) + 0.7152f * ToLinear(((rgb >> 8) & 255) / 255f) +
        0.0722f * ToLinear((rgb & 255) / 255f);

    /// <summary>Mixes two colours in OKLab.</summary>
    public static uint Mix(uint from, uint to, float t)
    {
        var a = ToOklab(from);
        var b = ToOklab(to);
        return FromOklab(a.L + (b.L - a.L) * t, a.A + (b.A - a.A) * t, a.B + (b.B - a.B) * t);
    }

    private static (float L, float A, float B) ToOklab(uint rgb)
    {
        var r = ToLinear(((rgb >> 16) & 255) / 255f);
        var g = ToLinear(((rgb >> 8) & 255) / 255f);
        var b = ToLinear((rgb & 255) / 255f);
        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);
        return (0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

    private static uint FromOklab(float lightness, float a, float b)
    {
        var l = lightness + 0.3963377774f * a + 0.2158037573f * b;
        var m = lightness - 0.1055613458f * a - 0.0638541728f * b;
        var s = lightness - 0.0894841775f * a - 1.2914855480f * b;
        l = l * l * l;
        m = m * m * m;
        s = s * s * s;
        var red = ToSrgb(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s);
        var green = ToSrgb(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s);
        var blue = ToSrgb(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        return (red << 16) | (green << 8) | blue;
    }

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static uint ToSrgb(float c)
    {
        c = Math.Clamp(c, 0f, 1f);
        var encoded = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        return (uint)Math.Clamp((int)(encoded * 255f + 0.5f), 0, 255);
    }
}
