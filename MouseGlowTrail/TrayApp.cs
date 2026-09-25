using System.Runtime.InteropServices;
using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

/// <summary>
/// UI thread: the notification-area icon and its quick menu, global hotkeys, settings and the
/// control panel. Rendering lives on <see cref="TrailEngine"/>'s own thread, so an open menu or
/// panel never freezes a trail mid-fade.
/// </summary>
internal sealed unsafe class TrayApp : IPanelHost
{
    private const string WindowClass = "MouseGlowTrail.Tray";
    private const uint TrayMessage = WM_APP + 1;
    private const uint ActivatedMessage = WM_APP + 2;
    private const int HotkeyToggle = 1;
    private const int HotkeyQuit = 2;
    private const nuint SaveTimer = 10;

    private const uint CommandToggle = 1;
    private const uint CommandQuit = 2;
    private const uint CommandSparkles = 4;
    private const uint CommandStartup = 6;
    private const uint CommandPanel = 8;
    private const uint CommandStyle = 100;
    private const uint CommandOpacity = 400;

    private static TrayApp? s_app;

    private readonly EventWaitHandle _activation;
    private readonly Settings _settings = SettingsStore.Load();
    private readonly Dictionary<(TrailStyleId Style, bool Selected), IntPtr> _swatches = new();
    private TrailEngine? _engine;
    private RegisteredWaitHandle? _activationWait;
    private IntPtr _window;
    private IntPtr _icon;
    private IntPtr _balloonIcon;
    private uint _taskbarCreated;
    private bool _hotkeyToggle;
    private bool _hotkeyQuit;
    private string _iconLook = "";

    private readonly bool _demo;
    private readonly bool _openPanel;

    public TrayApp(EventWaitHandle activation, bool demo, bool openPanel)
    {
        _activation = activation;
        _demo = demo;
        _openPanel = openPanel;
    }

    // ---- IPanelHost ----

    public Settings Settings => _settings;

    public void SettingsChanged(bool save)
    {
        _engine?.Apply(_settings.ToRenderConfig());
        RefreshIconsIfLookChanged();
        UpdateTrayIcon();
        // Slider drags change settings many times a second: write the file once things settle.
        SetTimer(_window, SaveTimer, save ? 250u : 1500u, 0);
    }

    public bool StartupEnabled
    {
        get => StartupRegistration.IsEnabled();
        set => StartupRegistration.SetEnabled(value);
    }

    public string RendererStatus
    {
        get
        {
            if (!_settings.GpuAcceleration)
            {
                return Loc.T("关闭：使用 CPU 绘制", "Off: drawing on the CPU");
            }

            if (_engine?.GpuUnavailable == true)
            {
                return Loc.T("显卡当前不可用，已改用 CPU 绘制", "Graphics card unavailable, drawing on the CPU");
            }

            return _engine?.RendererName == "GPU" ? Loc.T("开启：正在使用显卡绘制", "On: drawing on the graphics card") : Loc.T("开启：下一次绘制时切换到显卡", "On: switches to the graphics card with the next frame");
        }
    }

    public string SettingsFolder => SettingsStore.Folder;

    public void PanelClosed()
    {
        KillTimer(_window, SaveTimer);
        SettingsStore.Save(_settings);
        // The panel's caches and bitmaps are gone; hand the memory back rather than keeping it around.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    public int Run()
    {
        s_app = this;
        Loc.Apply(_settings.Language);
        CreateWindow();
        MenuTheme.Initialize(_window);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        EnsureHotkeys();

        _engine = new TrailEngine(_settings.ToRenderConfig());
        _engine.Start();
        RefreshIcons();
        AddTrayIcon();
        if (_demo)
        {
            _engine.RequestPreview();
        }

        var window = _window;
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation,
            (_, _) => PostMessageW(window, ActivatedMessage, 0, 0), null, Timeout.Infinite, false);

        if (!_settings.WelcomeShown)
        {
            ShowBalloon(Loc.T("鼠标流光已在后台运行", "Mouse Glow Trail is running"), Loc.T("点击托盘图标打开控制面板，右键可快速切换样式；Ctrl+Alt+T 随时暂停或继续。", "Click the tray icon for the control panel, right-click it for quick options. Ctrl+Alt+T pauses or resumes at any time."));
            _settings.WelcomeShown = true;
            SettingsStore.Save(_settings);
        }

        if (_openPanel)
        {
            ControlPanel.Show(this);
        }

        MSG message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }

        Shutdown();
        return 0;
    }

    [UnmanagedCallersOnly]
    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (s_app is { } app)
        {
            try
            {
                if (app.Handle(message, wParam, lParam, out var result))
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
            case TrayMessage:
                var notification = (uint)((long)lParam & 0xFFFF);
                if (notification is NIN_SELECT or NIN_KEYSELECT)
                {
                    ControlPanel.Show(this);
                }
                else if (notification == WM_CONTEXTMENU)
                {
                    // NOTIFYICON_VERSION_4 passes the anchor point in wParam.
                    ShowMenu((short)((long)wParam & 0xFFFF), (short)(((long)wParam >> 16) & 0xFFFF));
                }

                return true;
            case ActivatedMessage:
                // The exe was launched again while already running: show the panel.
                if (!_settings.Enabled)
                {
                    SetEnabled(true);
                }

                ControlPanel.Show(this);
                return true;
            case WM_HOTKEY:
                if ((int)wParam == HotkeyToggle)
                {
                    SetEnabled(!_settings.Enabled);
                }
                else if ((int)wParam == HotkeyQuit)
                {
                    DestroyWindow(_window);
                }

                return true;
            case WM_TIMER when (nuint)wParam == SaveTimer:
                KillTimer(_window, SaveTimer);
                SettingsStore.Save(_settings);
                return true;
            case WM_SETTINGCHANGE:
                if (lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet")
                {
                    MenuTheme.Refresh();
                }

                return false;
            case WM_DPICHANGED:
                RefreshIcons();
                UpdateTrayIcon();
                return true;
            case WM_DISPLAYCHANGE:
                RefreshIcons();
                UpdateTrayIcon();
                return false;
            case WM_DESTROY:
                ControlPanel.CloseIfOpen();
                PostQuitMessage(0);
                return true;
        }

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted: the notification area forgot us.
            RefreshIcons();
            AddTrayIcon();
            return true;
        }

        return false;
    }

    private void ShowMenu(int x, int y)
    {
        if (x == 0 && y == 0)
        {
            POINT point;
            GetCursorPos(&point);
            x = point.X;
            y = point.Y;
        }

        EnsureHotkeys();
        RebuildSwatches();
        var menu = CreatePopupMenu();
        var styles = CreatePopupMenu();
        var opacities = CreatePopupMenu();

        AppendMenuW(menu, MF_STRING, CommandPanel, Loc.T("打开控制面板…", "Open control panel…"));
        SetMenuDefaultItem(menu, CommandPanel, 0);
        AppendMenuW(menu, MF_SEPARATOR, 0, null);
        AppendMenuW(menu, Check(_settings.Enabled), CommandToggle,
            _hotkeyToggle ? Loc.T("启用鼠标流光\tCtrl+Alt+T", "Enable Mouse Glow Trail\tCtrl+Alt+T") : Loc.T("启用鼠标流光", "Enable Mouse Glow Trail"));
        AppendMenuW(menu, MF_SEPARATOR, 0, null);

        var choices = TrailStyles.All.Select(style => (style.Id, Label: $"{style.Name}\t{style.Description}")).ToList();
        choices.Add((TrailStyleId.Custom, Loc.T("自定义\t你的配色", "Custom\tYour colors")));
        foreach (var (id, label) in choices)
        {
            var command = CommandStyle + (uint)id;
            AppendMenuW(styles, MF_STRING, command, label);
            if (_swatches.TryGetValue((id, id == _settings.Style), out var swatch))
            {
                var item = new MENUITEMINFOW { Size = (uint)sizeof(MENUITEMINFOW), Mask = MIIM_BITMAP, ItemBitmap = swatch };
                SetMenuItemInfoW(styles, command, 0, &item);
            }
        }

        CheckMenuRadioItem(styles, CommandStyle, CommandStyle + (uint)TrailStyleId.Custom,
            CommandStyle + (uint)_settings.Style, MF_BYCOMMAND);
        AppendMenuW(menu, MF_POPUP, (nuint)styles, Loc.T("流光样式", "Style"));

        for (var i = 0; i < TrailOptions.Opacities.Length; i++)
        {
            AppendMenuW(opacities, MF_STRING, CommandOpacity + (uint)i, TrailOptions.OpacityLabel(TrailOptions.Opacities[i]));
        }

        var opacityIndex = Array.IndexOf(TrailOptions.Opacities, _settings.Opacity);
        if (opacityIndex >= 0)
        {
            CheckMenuRadioItem(opacities, CommandOpacity, CommandOpacity + (uint)TrailOptions.Opacities.Length - 1,
                CommandOpacity + (uint)opacityIndex, MF_BYCOMMAND);
        }

        AppendMenuW(menu, MF_POPUP, (nuint)opacities, Loc.T("浓淡", "Opacity"));
        AppendMenuW(menu, Check(_settings.Sparkles), CommandSparkles, Loc.T($"粒子（{TrailOptions.Label(_settings.ParticleKind)}）", $"Particles ({TrailOptions.Label(_settings.ParticleKind)})"));
        AppendMenuW(menu, MF_SEPARATOR, 0, null);
        AppendMenuW(menu, Check(StartupRegistration.IsEnabled()), CommandStartup, Loc.T("开机自动启动", "Start with Windows"));
        AppendMenuW(menu, MF_STRING, CommandQuit, _hotkeyQuit ? Loc.T("退出\tCtrl+Alt+Q", "Quit\tCtrl+Alt+Q") : Loc.T("退出", "Quit"));

        // Required so the menu closes when the user clicks elsewhere.
        SetForegroundWindow(_window);
        var chosen = (uint)TrackPopupMenuEx(menu,
            TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY | TPM_BOTTOMALIGN, x, y, _window, 0);
        PostMessageW(_window, WM_NULL, 0, 0);
        DestroyMenu(menu); // also destroys the attached submenus
        if (chosen != 0)
        {
            Execute(chosen);
        }
    }

    /// <summary>
    /// Registers whichever global hotkey is still missing. Another program may hold a combination
    /// at start-up and release it later, so this is retried whenever the menu opens.
    /// </summary>
    private void EnsureHotkeys()
    {
        if (!_hotkeyToggle)
        {
            _hotkeyToggle = RegisterHotKey(_window, HotkeyToggle, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'T') != 0;
        }

        if (!_hotkeyQuit)
        {
            _hotkeyQuit = RegisterHotKey(_window, HotkeyQuit, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'Q') != 0;
        }
    }

    private void Execute(uint command)
    {
        switch (command)
        {
            case CommandPanel:
                ControlPanel.Show(this);
                return;
            case CommandToggle:
                SetEnabled(!_settings.Enabled);
                return;
            case CommandQuit:
                DestroyWindow(_window);
                return;
            case CommandSparkles:
                _settings.Sparkles = !_settings.Sparkles;
                break;
            case CommandStartup:
                StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
                ControlPanel.SettingsChangedOutside();
                return;
            case >= CommandStyle and <= CommandStyle + (uint)TrailStyleId.Custom:
                _settings.Style = (TrailStyleId)(command - CommandStyle);
                _settings.Enabled = true; // picking a look implies wanting to see it
                break;
            case >= CommandOpacity and < CommandOpacity + 16 when command - CommandOpacity < TrailOptions.Opacities.Length:
                _settings.Opacity = TrailOptions.Opacities[command - CommandOpacity];
                break;
            default:
                return;
        }

        // Look-changing choices play a flourish so the result is visible straight away.
        Commit(preview: command >= CommandStyle || (command == CommandSparkles && _settings.Sparkles));
    }

    private void SetEnabled(bool enabled)
    {
        _settings.Enabled = enabled;
        Commit();
    }

    private void Commit(bool preview = false)
    {
        SettingsStore.Save(_settings);
        _engine?.Apply(_settings.ToRenderConfig());
        RefreshIconsIfLookChanged();
        UpdateTrayIcon();
        ControlPanel.SettingsChangedOutside();
        if (preview && _settings.Enabled)
        {
            _engine?.RequestPreview();
        }
    }

    private static uint Check(bool value) => value ? MF_CHECKED : MF_STRING;

    private string Tooltip() => _settings.Enabled
        ? Loc.T($"鼠标流光 · {_settings.BaseStyle().Name}", $"Mouse Glow Trail · {_settings.BaseStyle().Name}")
        : Loc.T("鼠标流光 · 已暂停", "Mouse Glow Trail · paused");

    private NOTIFYICONDATAW NewIconData() => new() { Size = sizeof(NOTIFYICONDATAW), Window = _window, Id = 1 };

    private void AddTrayIcon()
    {
        var data = NewIconData();
        data.Flags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.CallbackMessage = TrayMessage;
        data.Icon = _icon;
        CopyText(Tooltip(), data.Tip, 128);
        Shell_NotifyIconW(NIM_ADD, &data);
        data.TimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, &data);
    }

    private void UpdateTrayIcon()
    {
        var data = NewIconData();
        data.Flags = NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        data.Icon = _icon;
        CopyText(Tooltip(), data.Tip, 128);
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    private void ShowBalloon(string title, string text)
    {
        var data = NewIconData();
        data.Flags = NIF_INFO;
        CopyText(title, data.InfoTitle, 64);
        CopyText(text, data.Info, 256);
        data.InfoFlags = NIIF_USER | NIIF_LARGE_ICON | NIIF_NOSOUND;
        data.BalloonIcon = _balloonIcon;
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }

    private static void CopyText(string text, char* destination, int capacity)
    {
        var length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    /// <summary>The icons show the style's colours and the paused state; redraw them only when those change.</summary>
    private void RefreshIconsIfLookChanged()
    {
        var look = $"{_settings.Style}:{string.Join(",", _settings.CustomColors)}:{_settings.CustomFlow}:{_settings.Enabled}";
        if (look != _iconLook)
        {
            RefreshIcons();
        }
    }

    private void RefreshIcons()
    {
        _iconLook = $"{_settings.Style}:{string.Join(",", _settings.CustomColors)}:{_settings.CustomFlow}:{_settings.Enabled}";
        var dpi = GetDpiForWindow(_window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var style = _settings.BaseStyle();
        var small = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
        var large = GetSystemMetricsForDpi(SM_CXICON, dpi);
        Replace(ref _icon, IconFactory.CreateIcon(IconPainter.RenderIcon(small, style, !_settings.Enabled), small));
        Replace(ref _balloonIcon, IconFactory.CreateIcon(IconPainter.RenderIcon(large, style, false), large));
    }

    private static void Replace(ref IntPtr icon, IntPtr replacement)
    {
        if (icon != 0)
        {
            DestroyIcon(icon);
        }

        icon = replacement;
    }

    /// <summary>Colour chips for the style menu (the custom one follows the user's colours).</summary>
    private void RebuildSwatches()
    {
        ReleaseSwatches();
        var dpi = GetDpiForWindow(_window);
        if (dpi == 0)
        {
            dpi = 96;
        }

        var size = (int)(16 * dpi / 96);
        var styles = TrailStyles.All.Append(TrailStyles.Custom([.. _settings.CustomColors.Select(ColorText.ParseOrDefault)],
            _settings.CustomFlow));
        foreach (var style in styles)
        {
            foreach (var selected in new[] { false, true })
            {
                var bitmap = IconFactory.CreateBitmap(IconPainter.RenderSwatch(size, style, selected), size);
                if (bitmap != 0)
                {
                    _swatches[(style.Id, selected)] = bitmap;
                }
            }
        }
    }

    private void ReleaseSwatches()
    {
        foreach (var bitmap in _swatches.Values)
        {
            DeleteObject(bitmap);
        }

        _swatches.Clear();
    }

    private void CreateWindow()
    {
        var instance = GetModuleHandleW(0);
        fixed (char* className = WindowClass)
        fixed (char* title = Loc.T("鼠标流光", "Mouse Glow Trail"))
        {
            var windowClass = new WNDCLASSEXW
            {
                Size = (uint)sizeof(WNDCLASSEXW),
                WndProc = &WindowProc,
                Instance = instance,
                ClassName = className
            };
            RegisterClassExW(&windowClass);
            // A hidden top-level window (not message-only) so it also receives TaskbarCreated.
            _window = CreateWindowExW(WS_EX_TOOLWINDOW, className, title, WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        }

        if (_window == 0)
        {
            throw new InvalidOperationException("Unable to create the notification window.");
        }
    }

    private void Shutdown()
    {
        _activationWait?.Unregister(null);
        KillTimer(_window, SaveTimer);
        SettingsStore.Save(_settings);
        _engine?.Stop();
        var data = NewIconData();
        Shell_NotifyIconW(NIM_DELETE, &data);
        if (_hotkeyToggle)
        {
            UnregisterHotKey(_window, HotkeyToggle);
        }

        if (_hotkeyQuit)
        {
            UnregisterHotKey(_window, HotkeyQuit);
        }

        Replace(ref _icon, 0);
        Replace(ref _balloonIcon, 0);
        ReleaseSwatches();
    }
}

internal static unsafe class IconFactory
{
    /// <summary>Builds an HICON from straight-alpha BGRA pixels.</summary>
    public static IntPtr CreateIcon(byte[] bgra, int size)
    {
        var color = CreateBitmap(bgra, size);
        if (color == 0)
        {
            return 0;
        }

        var maskStride = (size + 15) / 16 * 2;
        var maskBits = new byte[maskStride * size];
        IntPtr mask;
        fixed (byte* bits = maskBits)
        {
            mask = Native.CreateBitmap(size, size, 1, 1, bits);
        }

        var info = new ICONINFO { IsIcon = 1, Mask = mask, Color = color };
        var icon = CreateIconIndirect(&info);
        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    /// <summary>A top-down 32 bpp DIB holding the given BGRA pixels.</summary>
    public static IntPtr CreateBitmap(byte[] bgra, int size)
    {
        var header = new BITMAPINFOHEADER
        {
            Size = (uint)sizeof(BITMAPINFOHEADER),
            Width = size,
            Height = -size,
            Planes = 1,
            BitCount = 32
        };
        void* bits;
        var bitmap = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (bitmap == 0 || bits == null)
        {
            return 0;
        }

        Marshal.Copy(bgra, 0, (IntPtr)bits, bgra.Length);
        return bitmap;
    }
}

/// <summary>
/// Lets native popup menus follow the system dark/light app theme. Uses the uxtheme ordinals that
/// Explorer and Notepad rely on; silently does nothing on builds that lack them.
/// </summary>
internal static unsafe class MenuTheme
{
    private static delegate* unmanaged<void> s_flushMenuThemes;

    public static void Initialize(IntPtr window)
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 18362)
            {
                return;
            }

            var uxtheme = LoadLibraryExW("uxtheme.dll", 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
            if (uxtheme == 0)
            {
                return;
            }

            var allowDarkModeForWindow = GetProcAddress(uxtheme, 133);
            var setPreferredAppMode = GetProcAddress(uxtheme, 135);
            s_flushMenuThemes = (delegate* unmanaged<void>)GetProcAddress(uxtheme, 136);
            if (setPreferredAppMode != 0)
            {
                ((delegate* unmanaged<int, int>)setPreferredAppMode)(1); // AllowDark
            }

            if (allowDarkModeForWindow != 0)
            {
                ((delegate* unmanaged<IntPtr, byte, byte>)allowDarkModeForWindow)(window, 1);
            }

            Refresh();
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    public static void Refresh()
    {
        if (s_flushMenuThemes != null)
        {
            s_flushMenuThemes();
        }
    }
}
