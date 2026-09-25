using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseGlowTrail.Ui;
using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

/// <summary>What the control panel needs from the application.</summary>
internal interface IPanelHost
{
    /// <summary>The live settings; the panel edits them in place.</summary>
    Settings Settings { get; }

    /// <summary>Pushes <see cref="Settings"/> to the renderer; saves now or shortly after.</summary>
    void SettingsChanged(bool save);

    bool StartupEnabled { get; set; }

    /// <summary>A short description of the renderer in use.</summary>
    string RendererStatus { get; }

    /// <summary>Settings are kept apart from the installed copy (development runs).</summary>
    string SettingsFolder { get; }

    void PanelClosed();
}

/// <summary>
/// The control panel: a Windows 11 style window (Mica, custom title bar, navigation pane) drawn
/// with Direct2D. The static part of the UI is cached in an offscreen layer and redrawn only when
/// something changes; the live preview and flyouts are composed on top every frame.
/// </summary>
internal sealed unsafe partial class ControlPanel
{
    private const string ClassName = "MouseGlowTrail.Panel";
    private const float TitleHeight = 48f;
    private const float NavWidth = 264f;
    private const float PagePadding = 40f;
    private const float MaxContentWidth = 980f;
    private const nuint CaretTimer = 1;
    private const nuint ConfirmTimer = 2;

    private static ControlPanel? s_panel;
    private static bool s_registered;

    private readonly IPanelHost _host;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly PreviewStage _stage = new();
    private readonly PointerImage _pointer = new();
    private readonly TileArt _art = new();
    private readonly float[] _scrollTargets = new float[PageCount];
    private readonly float[] _contentHeights = new float[PageCount];
    private IntPtr _hwnd;
    private Gfx _gfx = null!;
    private Gui _gui = null!;
    private Theme _theme;
    private void* _layer;
    private void* _appIcon;
    private void* _appIconLarge;
    private int _largeIconGeneration = -1;
    private int _layerGeneration = -1;
    private int _iconGeneration = -1;
    private int _layerWidth;
    private int _layerHeight;
    private bool _baseDirty = true;
    private bool _active = true;
    private int _page;
    private double _lastFrame;
    private RenderConfig _config;
    private bool _configDirty = true;
    private float _width;
    private float _height;
    private RectF _stageRect;
    private RectF _captionButtons;
    private bool _trackingMouse;
    private double _endDrawSeconds;
    private bool _useDwmFlush;
    private bool _scrollToFocus;
    private float _pageEnter = 1f;

    /// <summary>MGT_ALWAYS_PREVIEW=1 keeps the preview playing in the background (screen recordings).</summary>
    private static readonly bool AlwaysPreview = Environment.GetEnvironmentVariable("MGT_ALWAYS_PREVIEW") == "1";

    private ControlPanel(IPanelHost host)
    {
        _host = host;
        _theme = Theme.Load();
        _config = host.Settings.ToRenderConfig();
        _stage.Pointer = _pointer;
    }

    public static bool IsOpen => s_panel is not null;

    /// <summary>Opens the panel, or brings the open one to the front.</summary>
    public static void Show(IPanelHost host, int page = -1)
    {
        if (s_panel is { } existing)
        {
            if (page >= 0)
            {
                existing.SelectPage(page);
            }

            if (IsIconic(existing._hwnd) != 0)
            {
                ShowWindow(existing._hwnd, SW_RESTORE);
            }

            SetForegroundWindow(existing._hwnd);
            return;
        }

        var panel = new ControlPanel(host);
        if (page >= 0)
        {
            panel._page = page;
        }

        if (panel.Create())
        {
            s_panel = panel;
            ShowWindow(panel._hwnd, SW_SHOWNORMAL);
            SetForegroundWindow(panel._hwnd);
        }
    }

    /// <summary>Settings were changed elsewhere (tray menu, hotkey): redraw.</summary>
    public static void SettingsChangedOutside()
    {
        if (s_panel is { } panel)
        {
            panel._configDirty = true;
            panel.Invalidate(baseChanged: true);
        }
    }

    public static void CloseIfOpen()
    {
        if (s_panel is { } panel)
        {
            DestroyWindow(panel._hwnd);
        }
    }

    private Settings S => _host.Settings;

    // ---- Window ----

    private bool Create()
    {
        var instance = GetModuleHandleW(0);
        fixed (char* className = ClassName)
        fixed (char* title = Loc.T("鼠标流光 控制面板", "Mouse Glow Trail"))
        {
            if (!s_registered)
            {
                var windowClass = new WNDCLASSEXW
                {
                    Size = (uint)sizeof(WNDCLASSEXW),
                    WndProc = &WindowProc,
                    Instance = instance,
                    ClassName = className,
                    Cursor = LoadCursorW(0, IDC_ARROW)
                };
                RegisterClassExW(&windowClass);
                s_registered = true;
            }

            // Centred on the monitor under the pointer, at that monitor's scaling.
            POINT point;
            GetCursorPos(&point);
            var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { Size = sizeof(MONITORINFO) };
            GetMonitorInfoW(monitor, &info);
            uint dpiX, dpiY;
            var dpi = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &dpiX, &dpiY) >= 0 && dpiX > 0 ? dpiX : 96u;
            var scale = dpi / 96f;
            var workWidth = info.Work.Right - info.Work.Left;
            var workHeight = info.Work.Bottom - info.Work.Top;
            var width = Math.Min((int)(1080 * scale), workWidth - (int)(32 * scale));
            var height = Math.Min((int)(780 * scale), workHeight - (int)(32 * scale));
            var x = info.Work.Left + (workWidth - width) / 2;
            var y = info.Work.Top + (workHeight - height) / 2;
            s_panel = this; // WM_CREATE and friends arrive before CreateWindowExW returns
            _hwnd = CreateWindowExW(WS_EX_APPWINDOW, className, title,
                WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_CLIPCHILDREN,
                x, y, width, height, 0, 0, instance, 0);
        }

        if (_hwnd == 0)
        {
            s_panel = null;
            return false;
        }

        ApplyWindowTheme();
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(_hwnd, &margins);
        // Recalculate the frame now that WM_NCCALCSIZE removes the caption.
        SetWindowPos(_hwnd, 0, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);

        _gfx = new Gfx();
        if (!_gfx.Initialize(_hwnd, GetDpiForWindow(_hwnd)))
        {
            AppLog.Write("Control panel: Direct2D could not be initialised.");
            DestroyWindow(_hwnd);
            return false;
        }

        _gui = new Gui(_gfx, _theme) { ReduceMotion = !ClientAnimations() };
        UpdateSize();
        SetWindowIcons();
        _lastFrame = _clock.Elapsed.TotalSeconds;
        return true;
    }

    private void ApplyWindowTheme()
    {
        var dark = _theme.Dark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &dark, sizeof(int));
        if (_theme.Mica)
        {
            var backdrop = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(_hwnd, DWMWA_SYSTEMBACKDROP_TYPE, &backdrop, sizeof(uint));
        }
    }

    private static bool ClientAnimations()
    {
        int enabled = 1;
        return SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0) == 0 || enabled != 0;
    }

    private void SetWindowIcons()
    {
        var dpi = GetDpiForWindow(_hwnd);
        var style = S.BaseStyle();
        var small = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        var large = GetSystemMetricsForDpi(SM_CXICON, dpi);
        var smallIcon = IconFactory.CreateIcon(IconPainter.RenderIcon(small, style, false), small);
        var largeIcon = IconFactory.CreateIcon(IconPainter.RenderIcon(large, style, false), large);
        ReplaceIcon(SendMessageW(_hwnd, WM_SETICON, 0, smallIcon));
        ReplaceIcon(SendMessageW(_hwnd, WM_SETICON, 1, largeIcon));
    }

    private static void ReplaceIcon(IntPtr previous)
    {
        if (previous != 0)
        {
            DestroyIcon(previous);
        }
    }

    private void UpdateSize()
    {
        RECT client;
        GetClientRect(_hwnd, &client);
        var scale = _gfx.Scale;
        _width = (client.Right - client.Left) / scale;
        _height = (client.Bottom - client.Top) / scale;
        _gfx.Resize(client.Right - client.Left, client.Bottom - client.Top);
        _baseDirty = true;
    }

    private void Invalidate(bool baseChanged = false)
    {
        if (baseChanged)
        {
            _baseDirty = true;
        }

        if (_hwnd != 0)
        {
            InvalidateRect(_hwnd, null, 0);
        }
    }

    private void SelectPage(int page)
    {
        if (page != _page)
        {
            _page = page;
            ClosePopup();
            _stage.Restart();
            // The new page slides in and fades up, like a WinUI page transition.
            _gui.Snap(Gui.Id("page.enter"), 0f);
            Invalidate(baseChanged: true);
        }
    }

    [UnmanagedCallersOnly]
    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (s_panel is { } panel)
        {
            try
            {
                if (panel._hwnd == 0)
                {
                    panel._hwnd = hwnd;
                }

                // DWM draws the caption buttons in the extended frame and hit-tests them.
                IntPtr dwmResult;
                if (DwmDefWindowProc(hwnd, message, wParam, lParam, &dwmResult) != 0)
                {
                    return dwmResult;
                }

                if (panel.Handle(message, wParam, lParam, out var result))
                {
                    return result;
                }
            }
            catch (Exception exception)
            {
                AppLog.Write(exception);
            }
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private bool Handle(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = 0;
        switch (message)
        {
            case WM_NCCALCSIZE when wParam != 0:
            {
                // Keep the standard resize borders at the sides and bottom; the client area takes the
                // caption's place (the title bar is drawn by the panel).
                var parameters = (NCCALCSIZE_PARAMS*)lParam;
                var top = parameters->Proposed.Top;
                DefWindowProcW(_hwnd, message, wParam, lParam);
                // Maximised windows hang their frame off the screen edge; keep the content on screen.
                var dpi = GetDpiForWindow(_hwnd);
                var frame = GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
                parameters->Proposed.Top = IsZoomed(_hwnd) != 0 ? top + frame : top;
                return true;
            }

            case WM_NCHITTEST:
                result = HitTest(lParam);
                return true;

            case WM_GETMINMAXINFO:
            {
                var info = (MINMAXINFO*)lParam;
                var dpi = _hwnd != 0 ? GetDpiForWindow(_hwnd) : 96u;
                info->MinTrackSize = new POINT((int)(980 * dpi / 96), (int)(600 * dpi / 96));
                return true;
            }

            case WM_ERASEBKGND:
                result = 1;
                return true;

            case WM_PAINT:
                Paint();
                return true;

            case WM_SIZE:
                if (_gfx is not null)
                {
                    UpdateSize();
                    Invalidate();
                }

                return false;

            case WM_DPICHANGED:
            {
                var suggested = (RECT*)lParam;
                _gfx.SetDpi(HighWord(wParam) is var dpi && dpi > 0 ? dpi : 96);
                SetWindowPos(_hwnd, 0, suggested->Left, suggested->Top, suggested->Right - suggested->Left,
                    suggested->Bottom - suggested->Top, SWP_NOZORDER | SWP_NOACTIVATE);
                UpdateSize();
                SetWindowIcons();
                Invalidate(baseChanged: true);
                return true;
            }

            case WM_ACTIVATE:
                _active = LowWord(wParam) != 0;
                _lastFrame = _clock.Elapsed.TotalSeconds;
                Invalidate(baseChanged: true);
                return false;

            case WM_SETTINGCHANGE:
                if (lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet")
                {
                    ReloadTheme();
                }

                return false;

            case 0x0320: // WM_DWMCOLORIZATIONCOLORCHANGED: the accent colour changed
                ReloadTheme();
                return false;

            case WM_MOUSEMOVE:
                TrackLeave();
                Pointer(lParam);
                return true;

            case WM_MOUSELEAVE:
                _trackingMouse = false;
                Input(gui => gui.MouseInWindow = false);
                return true;

            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                SetCapture(_hwnd);
                Pointer(lParam, gui =>
                {
                    gui.Pressed = true;
                    gui.LeftDown = true;
                });
                return true;

            case WM_LBUTTONUP:
                // Release at this message's position first: letting go of the capture sends
                // WM_CAPTURECHANGED, which would otherwise end the press wherever the pointer was last seen.
                Pointer(lParam, gui =>
                {
                    gui.Released = true;
                    gui.LeftDown = false;
                });
                ReleaseCapture();
                return true;

            case WM_CAPTURECHANGED:
                if (_gui is { LeftDown: true })
                {
                    Input(gui =>
                    {
                        gui.Released = true;
                        gui.LeftDown = false;
                    });
                }

                return false;

            case WM_MOUSEWHEEL:
            {
                var point = new POINT(LowWord(lParam), HighWord(lParam));
                ScreenToClient(_hwnd, &point);
                var scale = _gfx.Scale;
                var (wheelX, wheelY) = (point.X / scale, point.Y / scale);
                Input(gui =>
                {
                    gui.MouseX = wheelX;
                    gui.MouseY = wheelY;
                    gui.MouseInWindow = true;
                    gui.Wheel = HighWord(wParam) / 120f;
                });
                return true;
            }

            case WM_SETCURSOR when LowWord(lParam) == HTCLIENT:
                SetCursor(LoadCursorW(0, _gui?.Cursor switch
                {
                    CursorKind.Hand => IDC_HAND,
                    CursorKind.IBeam => IDC_IBEAM,
                    _ => IDC_ARROW
                }));
                result = 1;
                return true;

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN when (int)wParam != 0x12:
                var key = (int)wParam;
                if (key == VK_ESCAPE && _popup == PopupKind.None && _editing == 0)
                {
                    // Escape with nothing to dismiss closes the window, like a dialog.
                    DestroyWindow(_hwnd);
                    return true;
                }

                Input(gui => gui.Key = new KeyPress(key, GetKeyState(VK_SHIFT) < 0, GetKeyState(VK_CONTROL) < 0));
                return message == WM_KEYDOWN;

            case WM_CHAR:
                var character = (char)wParam;
                if (!char.IsControl(character))
                {
                    Input(gui => gui.Typed = character.ToString());
                }

                return true;

            case WM_TIMER:
                if ((nuint)wParam == CaretTimer)
                {
                    _caretVisible = !_caretVisible;
                    Invalidate(baseChanged: _popup == PopupKind.None);
                }
                else if ((nuint)wParam == ConfirmTimer)
                {
                    KillTimer(_hwnd, ConfirmTimer);
                    _confirmReset = false;
                    Invalidate(baseChanged: true);
                }

                return true;

            case WM_CLOSE:
                DestroyWindow(_hwnd);
                return true;

            case WM_DESTROY:
                Destroy();
                return true;
        }

        return false;
    }

    private void ReloadTheme()
    {
        _theme = Theme.Load();
        _gui.T = _theme;
        ApplyWindowTheme();
        _art.Clear();
        Invalidate(baseChanged: true);
    }

    private IntPtr HitTest(IntPtr lParam)
    {
        var hit = DefWindowProcW(_hwnd, WM_NCHITTEST, 0, lParam);
        if ((int)hit != HTCLIENT)
        {
            return hit;
        }

        var point = new POINT(LowWord(lParam), HighWord(lParam));
        ScreenToClient(_hwnd, &point);
        var scale = _gfx?.Scale ?? 1f;
        var x = point.X / scale;
        var y = point.Y / scale;
        if (y < 5f && IsZoomed(_hwnd) == 0)
        {
            return x < 16f ? HTTOPLEFT : x > _width - 16f ? HTTOPRIGHT : HTTOP;
        }

        return y < TitleHeight && !_captionButtons.Contains(x, y) ? HTCAPTION : HTCLIENT;
    }

    private void TrackLeave()
    {
        if (_trackingMouse)
        {
            return;
        }

        var track = new TRACKMOUSEEVENT { Size = (uint)sizeof(TRACKMOUSEEVENT), Flags = TME_LEAVE, Window = _hwnd };
        _trackingMouse = TrackMouseEvent(&track) != 0;
    }

    private void Pointer(IntPtr lParam, Action<Gui>? change = null)
    {
        var scale = _gfx.Scale;
        var x = LowWord(lParam) / scale;
        var y = HighWord(lParam) / scale;
        Input(gui =>
        {
            gui.MouseX = x;
            gui.MouseY = y;
            gui.MouseInWindow = true;
            change?.Invoke(gui);
        });
    }

    /// <summary>Feeds one input event through the layout (without drawing) and schedules a redraw.</summary>
    private void Input(Action<Gui> change)
    {
        if (_gui is null)
        {
            return;
        }

        var cursorBefore = _gui.Cursor;
        change(_gui);
        _scrollToFocus |= _gui.Key?.Key == VK_TAB;
        _gui.BeginPass();
        _gfx.Rendering = false;
        _gui.Blocked = _popup != PopupKind.None;
        DrawBase();
        _gui.Blocked = false;
        DrawOverlay();
        _gui.EndPass();
        if (_gui.Changed)
        {
            _configDirty = true;
            _host.SettingsChanged(_gui.Committed);
        }
        else if (_gui.Committed)
        {
            _host.SettingsChanged(true);
        }

        _gui.Pressed = _gui.Released = false;
        _gui.Wheel = 0;
        _gui.Key = null;
        _gui.Typed = "";
        if (_gui.Cursor != cursorBefore)
        {
            SetCursor(LoadCursorW(0, _gui.Cursor switch
            {
                CursorKind.Hand => IDC_HAND,
                CursorKind.IBeam => IDC_IBEAM,
                _ => IDC_ARROW
            }));
        }

        Invalidate(baseChanged: true);
    }

    // ---- Drawing ----

    private void Paint()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = Math.Clamp(now - _lastFrame, 0, 0.1);
        _lastFrame = now;
        if (_gfx is null || !_gfx.EnsureTarget())
        {
            ValidateRect(_hwnd, null);
            return;
        }

        if (_configDirty)
        {
            _config = S.ToRenderConfig();
            _configDirty = false;
        }

        // The preview plays while the panel is in front, or while the pointer rests over it.
        var stageLive = _stageRect.W > 0 && (_active || _gui.MouseInWindow || AlwaysPreview) && IsIconic(_hwnd) == 0;
        if (stageLive)
        {
            _stage.Update(dt, _config, _stageRect.W, _stageRect.H, _gfx.Scale);
        }

        // A long pause should not turn into one big animation step.
        _gui.Dt = (float)Math.Min(dt, 1.0 / 30);
        var animating = false;
        if (EnsureLayer() && _baseDirty)
        {
            _gfx.BeginLayer(_layer);
            _gfx.Rendering = true;
            _gui.BeginPass();
            _gui.Blocked = _popup != PopupKind.None;
            DrawBase();
            _gui.Blocked = false;
            animating = _gui.Animating;
            _gfx.EndLayer(_layer);
            _baseDirty = animating;
        }

        _gfx.BeginWindow();
        _gfx.Clear(_theme.Mica ? 0u : _theme.SolidBackground);
        if (_layer != null)
        {
            _gfx.DrawLayer(_layer, _width, _height);
        }

        if (_stageRect.W > 0)
        {
            _gfx.Opacity = _pageEnter;
            _stage.Draw(_gfx, _theme, _stageRect, _config);
            _gfx.Opacity = 1f;
        }

        _gfx.Rendering = true;
        _gui.BeginPass();
        DrawOverlay();
        animating |= _gui.Animating;
        var endStart = Stopwatch.GetTimestamp();
        var presented = _gfx.EndWindow();
        _endDrawSeconds = _endDrawSeconds * 0.9 + 0.1 * (Stopwatch.GetTimestamp() - endStart) / (double)Stopwatch.Frequency;
        ValidateRect(_hwnd, null);
        if (!presented)
        {
            ReleaseDeviceResources();
            Invalidate(baseChanged: true);
            return;
        }

        if (animating || stageLive)
        {
            // Present waits for the display on most systems; where it does not, pace with DWM.
            _useDwmFlush = _endDrawSeconds < 0.0008;
            if (_useDwmFlush)
            {
                DwmFlush();
            }

            Invalidate();
        }
    }

    private bool EnsureLayer()
    {
        RECT client;
        GetClientRect(_hwnd, &client);
        var width = client.Right - client.Left;
        var height = client.Bottom - client.Top;
        if (_layer != null && _layerGeneration == _gfx.Generation && width == _layerWidth && height == _layerHeight)
        {
            return true;
        }

        D2D.Release(ref _layer);
        _layer = _gfx.CreateLayer(width, height);
        _layerGeneration = _gfx.Generation;
        _layerWidth = width;
        _layerHeight = height;
        _baseDirty = true;
        return _layer != null;
    }

    private void ReleaseDeviceResources()
    {
        D2D.Release(ref _layer);
        D2D.Release(ref _appIcon);
        D2D.Release(ref _appIconLarge);
        _stage.ReleaseDeviceResources();
        _pointer.ReleaseDeviceResources();
        _art.Clear();
    }

    private void Destroy()
    {
        KillTimer(_hwnd, CaretTimer);
        KillTimer(_hwnd, ConfirmTimer);
        ReleaseDeviceResources();
        _stage.Dispose();
        _gfx?.Dispose();
        ReplaceIcon(SendMessageW(_hwnd, WM_SETICON, 0, 0));
        ReplaceIcon(SendMessageW(_hwnd, WM_SETICON, 1, 0));
        s_panel = null;
        _hwnd = 0;
        _host.SettingsChanged(true);
        _host.PanelClosed();
    }

    /// <summary>Everything except the preview's moving picture and flyouts.</summary>
    private void DrawBase()
    {
        DrawTitleBar();
        DrawNavigation();
        DrawContent();
    }

    private void DrawTitleBar()
    {
        var g = _gfx;
        if (!_theme.Mica)
        {
            g.Fill(new RectF(0, 0, _width, TitleHeight), _theme.SolidBackground);
        }

        DrawAppIcon(new RectF(16f, (TitleHeight - 16f) * 0.5f, 16f, 16f));
        g.TextIn(Loc.T("鼠标流光", "Mouse Glow Trail"), TextStyle.Caption, new RectF(44f, 0, 200f, TitleHeight),
            _active ? _theme.TextPrimary : _theme.TextTertiary);

        // The minimise / maximise / close buttons are the system's own, drawn by DWM in the frame.
        _captionButtons = new RectF(_width - 3 * 46f, 0, 3 * 46f, 32f);
    }

    private void DrawAppIcon(RectF rect)
    {
        if (!_gfx.Rendering)
        {
            return;
        }

        if (_appIcon == null || _iconGeneration != _gfx.Generation)
        {
            D2D.Release(ref _appIcon);
            _iconGeneration = _gfx.Generation;
            _appIcon = IconBitmap((int)MathF.Round(rect.W * _gfx.Scale));
        }

        _gfx.DrawBitmap(_appIcon, rect);
    }

    /// <summary>The brand mark in the current style's colours as a bitmap of the given pixel size.</summary>
    private void* IconBitmap(int size)
    {
        var pixels = IconPainter.RenderIcon(size, S.BaseStyle(), false);
        // Straight alpha to premultiplied.
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            pixels[i] = (byte)(pixels[i] * alpha / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * alpha / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * alpha / 255);
        }

        var bitmap = _gfx.CreateBitmap(size, size);
        if (bitmap != null)
        {
            var target = new D2D_RECT_U { Right = (uint)size, Bottom = (uint)size };
            fixed (byte* data = pixels)
            {
                D2D.CopyFromMemory(bitmap, &target, data, (uint)(size * 4));
            }
        }

        return bitmap;
    }

    /// <summary>Everything with text in it is redrawn in the new language.</summary>
    private void LanguageChanged()
    {
        _art.Clear();
        SetWindowTextW(_hwnd, Loc.T("鼠标流光", "Mouse Glow Trail"));
        Invalidate(baseChanged: true);
    }

    /// <summary>The style (and so the brand mark's colours) changed.</summary>
    private void StyleChanged()
    {
        D2D.Release(ref _appIcon);
        D2D.Release(ref _appIconLarge);
        SetWindowIcons();
    }

    private static (char Glyph, string Title)[] Pages =>
    [
        ('\uE790', Loc.T("外观", "Appearance")),
        ('\uF4A5', Loc.T("粒子", "Particles")),
        ('\uE962', Loc.T("点击与滚轮", "Clicks & wheel")),
        ('\uE8B0', Loc.T("指针与显示", "Pointer & display")),
        ('\uE713', Loc.T("通用", "General"))
    ];

    private const int PageCount = 5;

    private void DrawNavigation()
    {
        var g = _gfx;
        var y = TitleHeight + 4f;
        const float ItemHeight = 36f;
        for (var i = 0; i < Pages.Length; i++)
        {
            var rect = new RectF(8f, y, NavWidth - 16f, ItemHeight);
            if (_gui.NavItem(Gui.Id("nav", i), rect, Pages[i].Glyph, Pages[i].Title, i == _page))
            {
                SelectPage(i);
            }

            y += ItemHeight + 4f;
        }

        // The selection pill slides between items.
        var pillY = _gui.Animate(Gui.Id("nav.pill"), TitleHeight + 4f + _page * (ItemHeight + 4f), 18f);
        g.Fill(new RectF(8f, pillY + (ItemHeight - 16f) * 0.5f, 3f, 16f), _theme.Accent, 1.5f);

        // Footer: the master switch, always at hand.
        var card = new RectF(12f, _height - 84f, NavWidth - 24f, 72f);
        g.Fill(card, _theme.CardFill, 6f);
        g.Stroke(card, _theme.CardStroke, 1f, 6f);
        var enabled = S.Enabled;
        g.Text(enabled ? Loc.T("流光已开启", "Trail is on") : Loc.T("流光已暂停", "Trail is paused"), TextStyle.BodyStrong, card.X + 14f, card.Y + 14f, _theme.TextPrimary);
        g.Text("Ctrl + Alt + T", TextStyle.Caption, card.X + 14f, card.Y + 38f, _theme.TextSecondary);
        if (_gui.Toggle(Gui.Id("nav.enabled"), card.Right - 14f, card.CenterY, ref enabled))
        {
            S.Enabled = enabled;
        }
    }

    private void DrawContent()
    {
        var g = _gfx;
        var layer = new RectF(NavWidth, TitleHeight, _width - NavWidth, _height - TitleHeight);
        g.FillTopLeftRounded(layer, 8f, _theme.LayerFill, _theme.LayerStroke);

        var contentWidth = MathF.Min(layer.W - PagePadding * 2, MaxContentWidth);
        var left = layer.X + (layer.W - contentWidth) * 0.5f;
        _pageEnter = _gui.Animate(Gui.Id("page.enter"), 1f, 12f);
        var slide = (1f - _pageEnter) * 28f;
        g.Opacity = _pageEnter;
        g.Text(Pages[_page].Title, TextStyle.Title, left, layer.Y + 22f + slide, _theme.TextPrimary);

        var top = layer.Y + 78f + slide;
        if (_page != 4)
        {
            var stageHeight = Math.Clamp(layer.H * 0.3f, 150f, 250f);
            _stageRect = new RectF(left, top, contentWidth, stageHeight);
            _stage.Scenario = _page switch
            {
                2 => PreviewScenario.Clicks,
                3 => PreviewScenario.Origin,
                _ => PreviewScenario.Gesture
            };
            g.Stroke(_stageRect.Inflate(1f), _theme.CardStroke, 1f, 9f);
            top = _stageRect.Bottom + 12f;
        }
        else
        {
            _stageRect = default;
        }

        var viewport = new RectF(layer.X, top, layer.W, layer.Bottom - top);
        var offset = _gui.Scroll(Gui.Id("scroll", _page), viewport, _contentHeights[_page] + 28f,
            ref _scrollTargets[_page]);
        var clip = viewport;
        g.PushClip(clip);
        _gui.Clip = clip;
        var y = viewport.Y + 4f - offset;
        var start = y;
        _row = new Rows(this, left, contentWidth);
        _row.Y = y;
        switch (_page)
        {
            case 0:
                AppearancePage();
                break;
            case 1:
                ParticlesPage();
                break;
            case 2:
                ClicksPage();
                break;
            case 3:
                PointerPage();
                break;
            default:
                GeneralPage();
                break;
        }

        _contentHeights[_page] = _row.Y - start;
        g.Opacity = 1f;
        if (_scrollToFocus && _gfx.Rendering && _gui.FocusRect is { } focused)
        {
            // Tabbing to a control below or above the visible part scrolls it into view.
            _scrollToFocus = false;
            if (focused.Bottom > viewport.Bottom - 8f)
            {
                _scrollTargets[_page] += focused.Bottom - viewport.Bottom + 24f;
            }
            else if (focused.Y < viewport.Y + 8f)
            {
                _scrollTargets[_page] -= viewport.Y - focused.Y + 24f;
            }
        }
        _gui.Clip = new RectF(-1e6f, -1e6f, 2e6f, 2e6f);
        g.PopClip();
    }

    /// <summary>Drawn every frame above the preview: its toolbar and any open flyout.</summary>
    private void DrawOverlay()
    {
        if (_stageRect.W > 0)
        {
            DrawStageToolbar();
        }

        DrawPopup();
    }

    private void DrawStageToolbar()
    {
        var button = new RectF(_stageRect.Right - 40f, _stageRect.Y + 8f, 32f, 32f);
        var light = _stage.LightStage;
        var glyphColor = light ? 0xE4000000u : 0xE6FFFFFFu;
        var hover = _gui.Hover(button) && _popup == PopupKind.None;
        if (hover)
        {
            _gfx.Fill(button, light ? 0x14000000u : 0x1FFFFFFFu, 4f);
        }

        _gfx.Icon(light ? '\uE708' : '\uE706', button, glyphColor);
        if (_popup == PopupKind.None && _gui.Behavior(Gui.Id("stage.light"), button, out _, out _))
        {
            _stage.LightStage = !light;
        }

        // Label in the corner: what the preview shows.
        var label = _page switch
        {
            2 => Loc.T("实时预览 · 自动演示点击与滚轮", "Live preview · clicks and scrolling play automatically"),
            3 => Loc.T("实时预览 · 观察光迹从指针哪里发出", "Live preview · watch where the trail leaves the pointer"),
            _ => Loc.T("实时预览", "Live preview")
        };
        _gfx.Text(label, TextStyle.Caption, _stageRect.X + 14f, _stageRect.Y + 12f, light ? 0x9E000000u : 0x99FFFFFFu);
    }
}
