using System.Runtime.InteropServices;

namespace MouseGlowTrail;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int Type;
    public int Width;
    public int Height;
    public int WidthBytes;
    public ushort Planes;
    public ushort BitsPixel;
    public IntPtr Bits;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MARGINS
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint Size;
    public uint Flags;
    public IntPtr Window;
    public uint HoverTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NCCALCSIZE_PARAMS
{
    public RECT Proposed;
    public RECT OldWindow;
    public RECT OldClient;
    public IntPtr Position;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MINMAXINFO
{
    public POINT Reserved;
    public POINT MaxSize;
    public POINT MaxPosition;
    public POINT MinTrackSize;
    public POINT MaxTrackSize;
}

/// <summary>Window, input and clipboard functions used by the control panel and the pointer tracking.</summary>
internal static unsafe partial class Native
{
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_EX_APPWINDOW = 0x00040000;

    public const uint WM_CREATE = 0x0001;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SHOWWINDOW = 0x0018;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_SETCURSOR = 0x0020;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_SETICON = 0x0080;
    public const uint WM_NCCALCSIZE = 0x0083;
    public const uint WM_NCACTIVATE = 0x0086;
    public const uint WM_NCMOUSEMOVE = 0x00A0;
    public const uint WM_NCLBUTTONDOWN = 0x00A1;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_MOUSEHWHEEL = 0x020E;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_NCMOUSELEAVE = 0x02A2;
    public const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;
    public const uint WM_THEMECHANGED = 0x031A;

    public const int HTCLIENT = 1;
    public const int HTCAPTION = 2;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;

    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint TME_LEAVE = 0x00000002;
    public const int IDC_ARROW = 32512;
    public const int IDC_HAND = 32649;
    public const int IDC_SIZEWE = 32644;
    public const int SM_SWAPBUTTON = 23;
    public const int SM_CXSIZEFRAME = 32;
    public const int SM_CXPADDEDBORDER = 92;
    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_DELETE = 0x2E;
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;
    public const ushort RI_MOUSE_WHEEL = 0x0400;
    public const ushort RI_MOUSE_HWHEEL = 0x0800;
    public const uint QS_RAWINPUT = 0x0400;
    public const uint SPI_GETCLIENTAREAANIMATION = 0x1042;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const uint DWMSBT_MAINWINDOW = 2;
    public const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetIconInfo(IntPtr icon, ICONINFO* info);

    public const uint DI_NORMAL = 0x0003;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, uint step,
        IntPtr brush, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int SetWindowTextW(IntPtr hwnd, string text);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetRawInputData(IntPtr rawInput, uint command, void* data, uint* size, uint headerSize);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int InvalidateRect(IntPtr hwnd, RECT* rect, int erase);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int ValidateRect(IntPtr hwnd, RECT* rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetClientRect(IntPtr hwnd, RECT* rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int ReleaseCapture();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetCapture();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int TrackMouseEvent(TRACKMOUSEEVENT* track);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr SetCursor(IntPtr cursor);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int AdjustWindowRectExForDpi(RECT* rect, uint style, int menu, uint exStyle, uint dpi);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int CloseClipboard();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int EmptyClipboard();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern nuint SetTimer(IntPtr hwnd, nuint id, uint milliseconds, IntPtr callback);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int KillTimer(IntPtr hwnd, nuint id);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr SendMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int SystemParametersInfoW(uint action, uint parameter, void* value, uint winIni);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int ScreenToClient(IntPtr hwnd, POINT* point);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int ClientToScreen(IntPtr hwnd, POINT* point);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetDoubleClickTime();

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int IsWindow(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetActiveWindow();

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern int GetObjectW(IntPtr gdiObject, int size, void* buffer);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    public static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, void* bits,
        BITMAPINFOHEADER* info, uint usage);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, MARGINS* margins);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    public static extern int DwmDefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, IntPtr* result);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int IsZoomed(IntPtr hwnd);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern void* GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern int GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr ShellExecuteW(IntPtr hwnd, string? operation, string file, string? parameters,
        string? directory, int show);

    public static int LowWord(IntPtr value) => (short)((long)value & 0xFFFF);

    public static int HighWord(IntPtr value) => (short)(((long)value >> 16) & 0xFFFF);
}
