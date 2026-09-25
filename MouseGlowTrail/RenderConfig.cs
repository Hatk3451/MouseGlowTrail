namespace MouseGlowTrail;

/// <summary>v2 length presets; still read from old settings files and offered as quick picks.</summary>
internal enum TrailLength
{
    Short,
    Standard,
    Long
}

/// <summary>v2 width presets; see <see cref="TrailLength"/>.</summary>
internal enum TrailWidth
{
    Thin,
    Standard,
    Bold
}

/// <summary>What is shed along the ribbon.</summary>
internal enum ParticleKind
{
    Stardust,
    Glint,
    Firefly,
    Heart,
    Star,
    Snowflake,
    Bubble,
    Spark
}

/// <summary>What a mouse button press shows at the pointer.</summary>
internal enum ClickEffect
{
    None,
    Ripple,
    DoubleRipple,
    Burst,
    Glint,
    Shockwave,
    Hearts
}

/// <summary>What turning the wheel shows at the pointer.</summary>
internal enum WheelEffect
{
    None,
    Chevrons,
    Stream,
    Ripple
}

/// <summary>Where on the pointer the ribbon starts.</summary>
internal enum TrailOrigin
{
    /// <summary>The hotspot itself (the arrow's tip).</summary>
    Tip,

    /// <summary>Just behind an arrow, below and right of the tip (v1 and v2 behaviour).</summary>
    Behind,

    /// <summary>The middle of whatever the pointer currently looks like.</summary>
    Center,

    /// <summary>A user-chosen offset from the hotspot.</summary>
    Custom
}

/// <summary>A colour choice for an effect: 0 follows the trail's palette, otherwise 0xFFRRGGBB.</summary>
internal static class EffectColor
{
    public const uint FollowTrail = 0;

    public static bool IsCustom(uint color) => (color & 0xFF000000) != 0;

    public static uint Resolve(uint color, TrailStyle style, float phase) =>
        IsCustom(color) ? color & 0xFFFFFF : style.ColorAt(phase);
}

/// <summary>Particle options as the renderer needs them.</summary>
internal sealed record ParticleOptions(
    bool Enabled,
    ParticleKind Kind,
    float Density,
    float Size,
    float Drift,
    uint Color)
{
    public static readonly ParticleOptions Off = new(false, ParticleKind.Stardust, 1f, 1f, 1f, EffectColor.FollowTrail);
}

/// <summary>Immutable snapshot handed from the UI thread to the render thread.</summary>
internal sealed record RenderConfig
{
    public bool Enabled { get; init; } = true;
    public required TrailStyle Style { get; init; }

    /// <summary>Seconds a point of the ribbon stays visible.</summary>
    public double Lifetime { get; init; } = 1.05;

    /// <summary>Ribbon width multiplier (1 = 8 px at 100 % scaling).</summary>
    public float Width { get; init; } = 1f;

    /// <summary>Overall strength (0-1) applied uniformly to ribbon, particles and effects.</summary>
    public float Opacity { get; init; } = 0.6f;

    /// <summary>Quick flicks swell the ribbon slightly, careful moves draw it finer.</summary>
    public bool SpeedResponse { get; init; } = true;

    /// <summary>The tail narrows as it fades (otherwise it only fades).</summary>
    public bool Taper { get; init; } = true;

    /// <summary>One Euro filter minimum cutoff in Hz: lower is smoother, higher follows more tightly.</summary>
    public float SmoothingCutoff { get; init; } = 4f;

    public ParticleOptions Particles { get; init; } = ParticleOptions.Off;

    public ClickEffect LeftClick { get; init; } = ClickEffect.Ripple;
    public ClickEffect RightClick { get; init; } = ClickEffect.Ripple;
    public ClickEffect MiddleClick { get; init; } = ClickEffect.None;
    public uint LeftClickColor { get; init; }
    public uint RightClickColor { get; init; }
    public uint MiddleClickColor { get; init; }

    /// <summary>Size multiplier for click and wheel effects.</summary>
    public float EffectSize { get; init; } = 1f;

    public WheelEffect Wheel { get; init; } = WheelEffect.None;
    public uint WheelColor { get; init; }

    public TrailOrigin Origin { get; init; } = TrailOrigin.Behind;

    /// <summary>Custom origin, in pixels for a standard 32 px pointer at 100 % scaling.</summary>
    public float OffsetX { get; init; } = 8f;

    public float OffsetY { get; init; } = 14f;

    /// <summary>No ribbon while drag-selecting text (it would sit right on the selection).</summary>
    public bool HideWhileSelecting { get; init; } = true;

    public bool HideInFullscreen { get; init; } = true;

    /// <summary>Draw with Direct3D and DirectComposition when a GPU is available.</summary>
    public bool GpuAcceleration { get; init; }
}

internal static class TrailOptions
{
    /// <summary>Opacity presets in percent, faintest first. Settings store the raw percentage.</summary>
    public static readonly int[] Opacities = [30, 45, 60, 80, 100];

    public const int DefaultOpacity = 60;

    public static string OpacityLabel(int percent) => percent switch
    {
        30 => Loc.T("轻盈\t30%", "Light\t30%"),
        45 => Loc.T("淡雅\t45%", "Subtle\t45%"),
        60 => Loc.T("柔和\t60%", "Soft\t60%"),
        80 => Loc.T("明亮\t80%", "Bright\t80%"),
        100 => Loc.T("浓郁\t100%", "Vivid\t100%"),
        _ => $"{percent}%"
    };

    public static double Lifetime(TrailLength length) => length switch
    {
        TrailLength.Short => 0.6,
        TrailLength.Long => 1.6,
        _ => 1.05
    };

    public static float WidthScale(TrailWidth width) => width switch
    {
        TrailWidth.Thin => 0.72f,
        TrailWidth.Bold => 1.4f,
        _ => 1f
    };

    /// <summary>Settings smoothing (0-100) to the filter's minimum cutoff: 50 is the v2 tuning (4 Hz).</summary>
    public static float SmoothingCutoff(int smoothing) =>
        16f * MathF.Pow(1f / 16f, Math.Clamp(smoothing, 0, 100) / 100f);

    public static string Label(ParticleKind kind) => kind switch
    {
        ParticleKind.Stardust => Loc.T("星尘", "Stardust"),
        ParticleKind.Glint => Loc.T("星芒", "Glints"),
        ParticleKind.Firefly => Loc.T("萤火", "Fireflies"),
        ParticleKind.Heart => Loc.T("爱心", "Hearts"),
        ParticleKind.Star => Loc.T("星星", "Stars"),
        ParticleKind.Snowflake => Loc.T("雪花", "Snowflakes"),
        ParticleKind.Bubble => Loc.T("泡泡", "Bubbles"),
        ParticleKind.Spark => Loc.T("火花", "Sparks"),
        _ => kind.ToString()
    };

    public static string Label(ClickEffect effect) => effect switch
    {
        ClickEffect.None => Loc.T("无", "None"),
        ClickEffect.Ripple => Loc.T("涟漪", "Ripple"),
        ClickEffect.DoubleRipple => Loc.T("双层涟漪", "Double ripple"),
        ClickEffect.Burst => Loc.T("星光绽放", "Starburst"),
        ClickEffect.Glint => Loc.T("闪耀星芒", "Twinkle"),
        ClickEffect.Shockwave => Loc.T("冲击波", "Shockwave"),
        ClickEffect.Hearts => Loc.T("爱心绽放", "Heart burst"),
        _ => effect.ToString()
    };

    public static string Label(WheelEffect effect) => effect switch
    {
        WheelEffect.None => Loc.T("无", "None"),
        WheelEffect.Chevrons => Loc.T("方向箭头", "Arrows"),
        WheelEffect.Stream => Loc.T("光点流", "Light stream"),
        WheelEffect.Ripple => Loc.T("涟漪", "Ripple"),
        _ => effect.ToString()
    };

    public static string Label(TrailOrigin origin) => origin switch
    {
        TrailOrigin.Tip => Loc.T("指针尖端", "Pointer tip"),
        TrailOrigin.Behind => Loc.T("指针后方", "Behind the pointer"),
        TrailOrigin.Center => Loc.T("指针中心", "Pointer center"),
        TrailOrigin.Custom => Loc.T("自定义位置", "Custom offset"),
        _ => origin.ToString()
    };
}
