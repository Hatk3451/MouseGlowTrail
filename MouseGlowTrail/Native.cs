using System.Runtime.InteropServices;

namespace MouseGlowTrail;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int Cx;
    public int Cy;

    public SIZE(int cx, int cy)
    {
        Cx = cx;
        Cy = cy;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Point;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CURSORINFO
{
    public int Size;
    public int Flags;
    public IntPtr Cursor;
    public POINT ScreenPosition;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RAWINPUTDEVICE
{
    public ushort UsagePage;
    public ushort Usage;
    public uint Flags;
    public IntPtr Target;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UPDATELAYEREDWINDOWINFO
{
    public uint Size;
    public IntPtr DestinationDc;
    public POINT* Destination;
    public SIZE* WindowSize;
    public IntPtr SourceDc;
    public POINT* Source;
    public uint ColorKey;
    public BLENDFUNCTION* Blend;
    public uint Flags;
    public RECT* Dirty;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint Size;
    public int Width;
    public int Height;
    public ushort Planes;
    public ushort BitCount;
    public uint Compression;
    public uint SizeImage;
    public int XPelsPerMeter;
    public int YPelsPerMeter;
    public uint ColorsUsed;
    public uint ColorsImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> WndProc;
    public int ClassExtra;
    public int WindowExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr Background;
    public char* MenuName;
    public char* ClassName;
    public IntPtr SmallIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public int Size;
    public RECT Monitor;
    public RECT Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    public int IsIcon;
    public int HotspotX;
    public int HotspotY;
    public IntPtr Mask;
    public IntPtr Color;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MENUITEMINFOW
{
    public uint Size;
    public uint Mask;
    public uint Type;
    public uint State;
    public uint Id;
    public IntPtr SubMenu;
    public IntPtr CheckedBitmap;
    public IntPtr UncheckedBitmap;
    public nuint ItemData;
    public char* TypeData;
    public uint Length;
    public IntPtr ItemBitmap;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NOTIFYICONDATAW
{
    public int Size;
    public IntPtr Window;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr Icon;
    public fixed char Tip[128];
    public uint State;
    public uint StateMask;
    public fixed char Info[256];
    public uint TimeoutOrVersion;
    public fixed char InfoTitle[64];
    public uint InfoFlags;
    public Guid Item;
    public IntPtr BalloonIcon;
}

// dwmapi.h packs this structure to 1 byte; only the leading fields are read, the rest is padding
// so cbSize matches the native 292 bytes.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct DWM_TIMING_INFO
{
    public uint Size;
    public uint RefreshNumerator;
    public uint RefreshDenominator;
    public ulong RefreshPeriod;
    public uint ComposeNumerator;
    public uint ComposeDenominator;
    public ulong VBlank;
    public fixed byte Rest[256];
}

internal static unsafe partial class Native
{
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_MAXIMIZE = 0x01000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint LWA_ALPHA = 0x00000002;
    public const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    public const int GWL_STYLE = -16;

    public const uint WM_NULL = 0x0000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_INPUT = 0x00FF;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;
    public const int HTTRANSPARENT = -1;
    public const int MA_NOACTIVATE = 3;
    public const uint SPI_SETCURSORS = 0x0057;

    public const uint PM_REMOVE = 0x0001;
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;
    public const uint INFINITE = 0xFFFFFFFF;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_MESSAGE = new(-3);
    public const uint RIDEV_REMOVE = 0x00000001;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public const int SM_CXICON = 11;
    public const int SM_CXSMICON = 49;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int CURSOR_SHOWING = 0x00000001;
    public const int IDC_IBEAM = 32513;

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint MF_STRING = 0x0000;
    public const uint MF_CHECKED = 0x0008;
    public const uint MF_POPUP = 0x0010;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint MF_BYCOMMAND = 0x0000;
    public const uint MIIM_BITMAP = 0x00000080;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint NIM_ADD = 0;
    public const uint NIM_MODIFY = 1;
    public const uint NIM_DELETE = 2;
    public const uint NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_INFO = 0x10;
    public const uint NIF_SHOWTIP = 0x80;
    public const uint NIIF_USER = 0x04;
    public const uint NIIF_NOSOUND = 0x10;
    public const uint NIIF_LARGE_ICON = 0x20;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = WM_USER;
    public const uint NIN_KEYSELECT = WM_USER + 1;

    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    public const uint TIMER_ALL_ACCESS = 0x001F0003;
    public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern ushort RegisterClassExW(WNDCLASSEXW* windowClass);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetMessageW(MSG* message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int PeekMessageW(MSG* message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int TranslateMessage(MSG* message);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr DispatchMessageW(MSG* message);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr* handles, uint milliseconds,
        uint wakeMask, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetCursorPos(POINT* point);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetCursorInfo(CURSORINFO* info);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDestination, POINT* destination,
        SIZE* size, IntPtr hdcSource, POINT* source, uint colorKey, BLENDFUNCTION* blend, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int UpdateLayeredWindowIndirect(IntPtr hwnd, UPDATELAYEREDWINDOWINFO* info);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy,
        uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetMonitorInfoW(IntPtr monitor, MONITORINFO* info);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetWindowRect(IntPtr hwnd, RECT* rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetClassNameW(IntPtr hwnd, char* buffer, int capacity);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int CheckMenuRadioItem(IntPtr menu, uint first, uint last, uint check, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int SetMenuItemInfoW(IntPtr menu, uint item, int byPosition, MENUITEMINFOW* info);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd,
        IntPtr parameters);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr CreateIconIndirect(ICONINFO* info);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern int DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, BITMAPINFOHEADER* info, uint usage, void** bits,
        IntPtr section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern int DeleteObject(IntPtr gdiObject);

    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern int Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);

    [DllImport("shcore.dll", ExactSpelling = true)]
    public static extern int GetDpiForMonitor(IntPtr monitor, int type, uint* dpiX, uint* dpiY);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    public static extern int DwmFlush();

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, void* value, uint size);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    public static extern int DwmGetCompositionTimingInfo(IntPtr hwnd, DWM_TIMING_INFO* info);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr GetModuleHandleW(IntPtr moduleName);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int SetWaitableTimer(IntPtr timer, long* dueTime, int period, IntPtr completion,
        IntPtr argument, int resume);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
}
