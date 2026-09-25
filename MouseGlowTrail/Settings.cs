using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace MouseGlowTrail;

internal sealed class Settings
{
    public const int CurrentVersion = 3;

    /// <summary>File format: 3 introduced the control panel's fine-grained options.</summary>
    public int Version { get; set; }

    public bool Enabled { get; set; } = true;
    public TrailStyleId Style { get; set; } = TrailStyleId.Aurora;

    /// <summary>Colour stops of the custom style, "#RRGGBB".</summary>
    public List<string> CustomColors { get; set; } = ["#48DACD", "#6FB9FF", "#B09CFF", "#F69FC2"];

    public ColorFlow CustomFlow { get; set; } = ColorFlow.PingPong;

    /// <summary>How quickly the colours change along the trail, in percent of the style's own pace.</summary>
    public int ColorSpeed { get; set; } = 100;

    /// <summary>Ribbon width in percent (100 = 8 px at 100 % scaling).</summary>
    public int WidthPercent { get; set; } = 100;

    /// <summary>How long a point of the trail stays visible, in milliseconds.</summary>
    public int LengthMs { get; set; } = 1050;

    /// <summary>Overall trail strength in percent (10-100).</summary>
    public int Opacity { get; set; } = TrailOptions.DefaultOpacity;

    /// <summary>Soft glow around the ribbon, in percent of the style's own (0-200).</summary>
    public int Glow { get; set; } = 100;

    /// <summary>White-hot centre line, in percent of the style's own (0-200).</summary>
    public int Core { get; set; } = 100;

    public bool SpeedResponse { get; set; } = true;
    public bool Taper { get; set; } = true;

    /// <summary>0 follows the pointer tightly, 100 smooths the most; 50 is the v2 tuning.</summary>
    public int Smoothing { get; set; } = 50;

    public bool Sparkles { get; set; }
    public ParticleKind ParticleKind { get; set; } = ParticleKind.Stardust;
    public int ParticleDensity { get; set; } = 100;
    public int ParticleSize { get; set; } = 100;
    public int ParticleDrift { get; set; } = 100;

    /// <summary>Null follows the trail's colours; otherwise "#RRGGBB" (likewise for the effect colours below).</summary>
    public string? ParticleColor { get; set; }

    public ClickEffect LeftClick { get; set; } = ClickEffect.Ripple;
    public ClickEffect RightClick { get; set; } = ClickEffect.Ripple;
    public ClickEffect MiddleClick { get; set; } = ClickEffect.None;
    public string? LeftClickColor { get; set; }
    public string? RightClickColor { get; set; }
    public string? MiddleClickColor { get; set; }
    public int EffectSize { get; set; } = 100;

    public WheelEffect Wheel { get; set; } = WheelEffect.None;
    public string? WheelColor { get; set; }

    public TrailOrigin Origin { get; set; } = TrailOrigin.Behind;
    public int OffsetX { get; set; } = 8;
    public int OffsetY { get; set; } = 14;

    public bool HideWhileSelecting { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>
    /// Off by default: on light settings the CPU path costs about the same and the graphics driver
    /// alone needs some 40-50 MB; bold or long trails are where the GPU pays off.
    /// </summary>
    public bool GpuAcceleration { get; set; }

    public bool WelcomeShown { get; set; }

    public UiLanguage Language { get; set; } = UiLanguage.Auto;

    // v2 fields, read once for migration and never written again.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TrailLength? Length { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TrailWidth? Width { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClickEffects { get; set; }

    /// <summary>Takes over every value of another instance (the live object is shared with the panel).</summary>
    public void CopyFrom(Settings other)
    {
        Version = other.Version;
        Enabled = other.Enabled;
        Style = other.Style;
        CustomColors = [.. other.CustomColors];
        CustomFlow = other.CustomFlow;
        ColorSpeed = other.ColorSpeed;
        WidthPercent = other.WidthPercent;
        LengthMs = other.LengthMs;
        Opacity = other.Opacity;
        Glow = other.Glow;
        Core = other.Core;
        SpeedResponse = other.SpeedResponse;
        Taper = other.Taper;
        Smoothing = other.Smoothing;
        Sparkles = other.Sparkles;
        ParticleKind = other.ParticleKind;
        ParticleDensity = other.ParticleDensity;
        ParticleSize = other.ParticleSize;
        ParticleDrift = other.ParticleDrift;
        ParticleColor = other.ParticleColor;
        LeftClick = other.LeftClick;
        RightClick = other.RightClick;
        MiddleClick = other.MiddleClick;
        LeftClickColor = other.LeftClickColor;
        RightClickColor = other.RightClickColor;
        MiddleClickColor = other.MiddleClickColor;
        EffectSize = other.EffectSize;
        Wheel = other.Wheel;
        WheelColor = other.WheelColor;
        Origin = other.Origin;
        OffsetX = other.OffsetX;
        OffsetY = other.OffsetY;
        HideWhileSelecting = other.HideWhileSelecting;
        HideInFullscreen = other.HideInFullscreen;
        GpuAcceleration = other.GpuAcceleration;
        WelcomeShown = other.WelcomeShown;
        Language = other.Language;
    }

    /// <summary>Everything "restore defaults" resets; on/off, GPU and first-run state are kept.</summary>
    public Settings WithDefaults()
    {
        var defaults = new Settings
        {
            Enabled = Enabled,
            GpuAcceleration = GpuAcceleration,
            WelcomeShown = WelcomeShown,
            Language = Language
        };
        defaults.Normalize();
        return defaults;
    }

    /// <summary>Brings a v2 file forward and clamps every value into its supported range.</summary>
    public void Normalize()
    {
        if (Version < 3)
        {
            if (Length is { } length)
            {
                LengthMs = (int)Math.Round(TrailOptions.Lifetime(length) * 1000);
            }

            if (Width is { } width)
            {
                WidthPercent = (int)Math.Round(TrailOptions.WidthScale(width) * 100);
            }

            if (ClickEffects == false)
            {
                LeftClick = RightClick = ClickEffect.None;
            }
        }

        Version = CurrentVersion;
        Length = null;
        Width = null;
        ClickEffects = null;

        if (!Enum.IsDefined(Style))
        {
            Style = TrailStyleId.Aurora;
        }

        CustomColors = CustomColors?.Where(c => ColorText.TryParse(c, out _)).Select(c => ColorText.Clean(c)!)
            .Take(ColorText.MaxStops).ToList() ?? [];
        if (CustomColors.Count == 0)
        {
            CustomColors = [.. TrailStyles.All[0].Stops.Select(ColorText.Format)];
        }

        CustomFlow = Enum.IsDefined(CustomFlow) ? CustomFlow : ColorFlow.PingPong;
        ColorSpeed = Math.Clamp(ColorSpeed, 25, 400);
        WidthPercent = Math.Clamp(WidthPercent, 40, 250);
        LengthMs = Math.Clamp(LengthMs, 200, 3000);
        if (Opacity is < 10 or > 100)
        {
            Opacity = TrailOptions.DefaultOpacity;
        }

        Glow = Math.Clamp(Glow, 0, 200);
        Core = Math.Clamp(Core, 0, 200);
        Smoothing = Math.Clamp(Smoothing, 0, 100);
        ParticleKind = Enum.IsDefined(ParticleKind) ? ParticleKind : ParticleKind.Stardust;
        ParticleDensity = Math.Clamp(ParticleDensity, 25, 300);
        ParticleSize = Math.Clamp(ParticleSize, 50, 250);
        ParticleDrift = Math.Clamp(ParticleDrift, 0, 300);
        LeftClick = Enum.IsDefined(LeftClick) ? LeftClick : ClickEffect.Ripple;
        RightClick = Enum.IsDefined(RightClick) ? RightClick : ClickEffect.Ripple;
        MiddleClick = Enum.IsDefined(MiddleClick) ? MiddleClick : ClickEffect.None;
        EffectSize = Math.Clamp(EffectSize, 50, 250);
        Wheel = Enum.IsDefined(Wheel) ? Wheel : WheelEffect.None;
        Origin = Enum.IsDefined(Origin) ? Origin : TrailOrigin.Behind;
        Language = Enum.IsDefined(Language) ? Language : UiLanguage.Auto;
        OffsetX = Math.Clamp(OffsetX, -48, 48);
        OffsetY = Math.Clamp(OffsetY, -48, 48);
        ParticleColor = ColorText.Clean(ParticleColor);
        LeftClickColor = ColorText.Clean(LeftClickColor);
        RightClickColor = ColorText.Clean(RightClickColor);
        MiddleClickColor = ColorText.Clean(MiddleClickColor);
        WheelColor = ColorText.Clean(WheelColor);
    }

    /// <summary>The look as picked: a preset or the custom stops, before the user's adjustments.</summary>
    public TrailStyle BaseStyle() => Style == TrailStyleId.Custom
        ? TrailStyles.Custom([.. CustomColors.Select(ColorText.ParseOrDefault)], CustomFlow)
        : TrailStyles.Get(Style);

    public RenderConfig ToRenderConfig() => new()
    {
        Enabled = Enabled,
        Style = BaseStyle().Adjust(ColorSpeed / 100f, Glow / 100f, Core / 100f),
        Lifetime = LengthMs / 1000.0,
        Width = WidthPercent / 100f,
        Opacity = Opacity / 100f,
        SpeedResponse = SpeedResponse,
        Taper = Taper,
        SmoothingCutoff = TrailOptions.SmoothingCutoff(Smoothing),
        Particles = new ParticleOptions(Sparkles, ParticleKind, ParticleDensity / 100f, ParticleSize / 100f,
            ParticleDrift / 100f, ColorText.ToEffectColor(ParticleColor)),
        LeftClick = LeftClick,
        RightClick = RightClick,
        MiddleClick = MiddleClick,
        LeftClickColor = ColorText.ToEffectColor(LeftClickColor),
        RightClickColor = ColorText.ToEffectColor(RightClickColor),
        MiddleClickColor = ColorText.ToEffectColor(MiddleClickColor),
        EffectSize = EffectSize / 100f,
        Wheel = Wheel,
        WheelColor = ColorText.ToEffectColor(WheelColor),
        Origin = Origin,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        HideWhileSelecting = HideWhileSelecting,
        HideInFullscreen = HideInFullscreen,
        GpuAcceleration = GpuAcceleration
    };
}

/// <summary>"#RRGGBB" colour text as stored in the settings file.</summary>
internal static class ColorText
{
    public const int MaxStops = 6;

    public static string Format(uint rgb) => $"#{rgb & 0xFFFFFF:X6}";

    public static bool TryParse(string? text, out uint rgb)
    {
        rgb = 0;
        if (text is null)
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.StartsWith("#"))
        {
            span = span[1..];
        }

        if (span.Length == 3)
        {
            // Short form: #RGB.
            Span<char> wide = [span[0], span[0], span[1], span[1], span[2], span[2]];
            return uint.TryParse(wide, System.Globalization.NumberStyles.HexNumber, null, out rgb);
        }

        return span.Length == 6 && uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, null, out rgb);
    }

    public static uint ParseOrDefault(string text) => TryParse(text, out var rgb) ? rgb : 0xFFFFFF;

    public static string? Clean(string? text) => TryParse(text, out var rgb) ? Format(rgb) : null;

    public static uint ToEffectColor(string? text) =>
        TryParse(text, out var rgb) ? 0xFF000000 | rgb : EffectColor.FollowTrail;
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}

/// <summary>Settings live in %APPDATA%\MouseGlowTrail\settings.json and are written atomically.</summary>
internal static class SettingsStore
{
    /// <summary>Where settings.json lives; development runs point it elsewhere.</summary>
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseGlowTrail");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJsonContext.Default.Settings);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        var fresh = new Settings();
        fresh.Normalize();
        return fresh;
    }

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }
}

/// <summary>
/// Launch-at-sign-in through the per-user Run key. The v1 Startup-folder shortcut is recognised
/// (and migrated) so users who enabled it with the old scripts see the correct state.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "MouseGlowTrail";

    private static string LegacyShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MouseGlowTrail.lnk");

    /// <summary>Development runs keep the state in memory and never touch the registry.</summary>
    public static bool Simulated { get; set; }

    private static bool s_simulated;

    public static bool IsEnabled()
    {
        if (Simulated)
        {
            return s_simulated;
        }

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is string)
            {
                // Task Manager's "Disable" leaves the value and flags it here (odd first byte).
                using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
                return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }

        return File.Exists(LegacyShortcut);
    }

    public static void SetEnabled(bool enabled)
    {
        if (Simulated)
        {
            s_simulated = enabled;
            return;
        }

        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled && Environment.ProcessPath is { } path)
                {
                    run.SetValue(ValueName, $"\"{path}\"");
                }
                else
                {
                    run.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }

            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
            {
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            // One mechanism only: the Run value replaces the old shortcut, and disabling removes both.
            if (File.Exists(LegacyShortcut))
            {
                File.Delete(LegacyShortcut);
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }
}

internal static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MouseGlowTrail");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
