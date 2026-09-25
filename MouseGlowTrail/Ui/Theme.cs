using Microsoft.Win32;

namespace MouseGlowTrail.Ui;

/// <summary>
/// Windows 11 (Fluent) colour tokens for the current app theme and accent colour, as 0xAARRGGBB.
/// Values follow WinUI's light and dark theme resources.
/// </summary>
internal sealed class Theme
{
    public bool Dark { get; private init; }

    /// <summary>The system backdrop (Mica) is available, so window backgrounds stay transparent.</summary>
    public bool Mica { get; private init; }

    public uint TextPrimary { get; private init; }
    public uint TextSecondary { get; private init; }
    public uint TextTertiary { get; private init; }
    public uint TextDisabled { get; private init; }
    public uint TextOnAccent { get; private init; }

    /// <summary>Window background when Mica is unavailable.</summary>
    public uint SolidBackground { get; private init; }

    /// <summary>The content area laid over Mica (Settings-style).</summary>
    public uint LayerFill { get; private init; }
    public uint LayerStroke { get; private init; }

    public uint CardFill { get; private init; }
    public uint CardFillHover { get; private init; }
    public uint CardStroke { get; private init; }

    public uint ControlFill { get; private init; }
    public uint ControlFillHover { get; private init; }
    public uint ControlFillPressed { get; private init; }
    public uint ControlFillDisabled { get; private init; }
    public uint ControlStroke { get; private init; }

    /// <summary>The darker bottom edge of a raised control.</summary>
    public uint ControlStrokeBottom { get; private init; }

    public uint ControlStrongStroke { get; private init; }
    public uint ControlStrongFill { get; private init; }
    public uint ControlAltFill { get; private init; }
    public uint ControlAltFillHover { get; private init; }

    /// <summary>Slider thumb and toggle knob body.</summary>
    public uint ControlSolid { get; private init; }

    public uint SubtleHover { get; private init; }
    public uint SubtlePressed { get; private init; }

    public uint FlyoutFill { get; private init; }
    public uint FlyoutStroke { get; private init; }
    public uint Divider { get; private init; }

    public uint Accent { get; private init; }
    public uint AccentHover { get; private init; }
    public uint AccentPressed { get; private init; }

    /// <summary>Accent-coloured text and glyphs (links, selected navigation icon).</summary>
    public uint AccentText { get; private init; }

    public uint FocusOuter { get; private init; }
    public uint FocusInner { get; private init; }

    public uint CloseHover => 0xFFC42B1Cu;

    /// <summary>The dark stage the trail preview plays on.</summary>
    public uint StageTop => 0xFF191A24u;
    public uint StageBottom => 0xFF0E0F16u;

    public static Theme Load()
    {
        // MGT_THEME=dark|light overrides the system theme (for checking both looks).
        var forced = Environment.GetEnvironmentVariable("MGT_THEME");
        var dark = forced is "dark" || (forced is not "light" && ReadAppsDark());
        var (light2, dark1) = ReadAccent();
        // MGT_NO_MICA=1 keeps the window background solid (for steady screen recordings).
        var mica = Environment.OSVersion.Version.Build >= 22621 && Environment.GetEnvironmentVariable("MGT_NO_MICA") != "1";
        return dark
            ? new Theme
            {
                Dark = true,
                Mica = mica,
                TextPrimary = 0xFFFFFFFFu,
                TextSecondary = 0xC5FFFFFFu,
                TextTertiary = 0x87FFFFFFu,
                TextDisabled = 0x5DFFFFFFu,
                TextOnAccent = 0xFF000000u,
                SolidBackground = 0xFF202020u,
                LayerFill = 0x4C3A3A3Au,
                LayerStroke = 0x19000000u,
                CardFill = 0x0DFFFFFFu,
                CardFillHover = 0x14FFFFFFu,
                CardStroke = 0x19000000u,
                ControlFill = 0x0FFFFFFFu,
                ControlFillHover = 0x15FFFFFFu,
                ControlFillPressed = 0x08FFFFFFu,
                ControlFillDisabled = 0x0BFFFFFFu,
                ControlStroke = 0x12FFFFFFu,
                ControlStrokeBottom = 0x12FFFFFFu,
                ControlStrongStroke = 0x8BFFFFFFu,
                ControlStrongFill = 0x8BFFFFFFu,
                ControlAltFill = 0x19000000u,
                ControlAltFillHover = 0x0BFFFFFFu,
                ControlSolid = 0xFF454545u,
                SubtleHover = 0x0FFFFFFFu,
                SubtlePressed = 0x0AFFFFFFu,
                FlyoutFill = 0xFF2C2C2Cu,
                FlyoutStroke = 0x33000000u,
                Divider = 0x15FFFFFFu,
                Accent = light2,
                AccentHover = WithAlpha(light2, 0xE6),
                AccentPressed = WithAlpha(light2, 0xCC),
                AccentText = light2,
                FocusOuter = 0xFFFFFFFFu,
                FocusInner = 0xB3000000u
            }
            : new Theme
            {
                Dark = false,
                Mica = mica,
                TextPrimary = 0xE4000000u,
                TextSecondary = 0x9E000000u,
                TextTertiary = 0x72000000u,
                TextDisabled = 0x5C000000u,
                TextOnAccent = 0xFFFFFFFFu,
                SolidBackground = 0xFFF3F3F3u,
                LayerFill = 0x80FFFFFFu,
                LayerStroke = 0x0F000000u,
                CardFill = 0xB3FFFFFFu,
                CardFillHover = 0xD9FFFFFFu,
                CardStroke = 0x0F000000u,
                ControlFill = 0xB3FFFFFFu,
                ControlFillHover = 0x80F9F9F9u,
                ControlFillPressed = 0x4DF9F9F9u,
                ControlFillDisabled = 0x4DF9F9F9u,
                ControlStroke = 0x0F000000u,
                ControlStrokeBottom = 0x29000000u,
                ControlStrongStroke = 0x72000000u,
                ControlStrongFill = 0x72000000u,
                ControlAltFill = 0x06000000u,
                ControlAltFillHover = 0x0F000000u,
                ControlSolid = 0xFFFFFFFFu,
                SubtleHover = 0x09000000u,
                SubtlePressed = 0x06000000u,
                FlyoutFill = 0xFFF9F9F9u,
                FlyoutStroke = 0x0F000000u,
                Divider = 0x0F000000u,
                Accent = dark1,
                AccentHover = WithAlpha(dark1, 0xE6),
                AccentPressed = WithAlpha(dark1, 0xCC),
                AccentText = dark1,
                FocusOuter = 0xE4000000u,
                FocusInner = 0xB3FFFFFFu
            };
    }

    public static uint WithAlpha(uint argb, uint alpha) => (argb & 0xFFFFFF) | (alpha << 24);

    /// <summary>Scales a colour's alpha (0-1).</summary>
    public static uint Fade(uint argb, float amount) =>
        (argb & 0xFFFFFF) | ((uint)Math.Clamp((argb >> 24) * amount + 0.5f, 0f, 255f) << 24);

    /// <summary>Linear blend of two colours, alpha included.</summary>
    public static uint Mix(uint a, uint b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        uint Channel(int shift) =>
            (uint)Math.Clamp((int)MathF.Round(((a >> shift) & 255) + ((int)((b >> shift) & 255) - (int)((a >> shift) & 255)) * t), 0, 255) << shift;
        return Channel(24) | Channel(16) | Channel(8) | Channel(0);
    }

    private static bool ReadAppsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The accent's "Light 2" (for dark mode) and "Dark 1" (for light mode) shades.</summary>
    private static (uint Light2, uint Dark1) ReadAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] { Length: >= 32 } palette)
            {
                // Eight RGBA entries: Light3, Light2, Light1, Accent, Dark1, Dark2, Dark3, (unused).
                uint Entry(int index) => 0xFF000000u | ((uint)palette[index * 4] << 16) |
                                          ((uint)palette[index * 4 + 1] << 8) | palette[index * 4 + 2];
                return (Entry(1), Entry(4));
            }
        }
        catch
        {
            // Fall through to the Windows default blue.
        }

        return (0xFF60CDFFu, 0xFF005FB8u);
    }
}
