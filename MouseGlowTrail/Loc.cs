using System.Runtime.InteropServices;

namespace MouseGlowTrail;

/// <summary>Language of the user interface.</summary>
internal enum UiLanguage
{
    /// <summary>Chinese when Windows shows Chinese, English otherwise.</summary>
    Auto,
    Chinese,
    English
}

/// <summary>
/// The app speaks Chinese and English. Strings sit next to their translation at the point of use
/// (<c>Loc.T("中文", "English")</c>), which keeps a two-language UI this small easy to follow.
/// </summary>
internal static partial class Loc
{
    public static bool Chinese { get; private set; } = SystemIsChinese();

    public static void Apply(UiLanguage language) => Chinese = language switch
    {
        UiLanguage.Chinese => true,
        UiLanguage.English => false,
        _ => SystemIsChinese()
    };

    public static string T(string chinese, string english) => Chinese ? chinese : english;

    /// <summary>
    /// The Windows display language (the process runs with invariant globalization, so the culture
    /// APIs cannot tell). LANG_CHINESE is primary language 0x04.
    /// </summary>
    private static bool SystemIsChinese() => (GetUserDefaultUILanguage() & 0x3FF) == 0x04;

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
