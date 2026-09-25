using System.Diagnostics;
using Microsoft.Win32;
using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

/// <summary>
/// Owns the click-through overlay on a dedicated thread. While something is visible it samples the
/// pointer once per display refresh (paced by DwmFlush) and hands each frame to a presenter: the CPU
/// one (a layered window) or, with GPU acceleration on, Direct3D 11 + DirectComposition. With nothing
/// to show the overlay is hidden; once the pointer has settled the thread sleeps until the mouse
/// reports input (Raw Input), with only a slow fallback check, so an idle desktop costs next to
/// nothing.
/// </summary>
internal sealed unsafe class TrailEngine
{
    private const string OverlayClass = "MouseGlowTrail.Overlay";

    // v1 tuned these for a Bibata-style arrow: start the ribbon behind the tip, not on it.
    private const float BehindOffsetX = 8f;
    private const float BehindOffsetY = 14f;
    private const uint IdlePollMilliseconds = 10;

    // Wheel effects: at most one per this interval, however fast the wheel spins.
    private const double WheelInterval = 0.07;

    // Settled for this long, the thread stops polling and waits for mouse input; the slow poll
    // still catches pointers moved without it (pen, remote control, programs warping the cursor).
    private const double SettleDelay = 0.3;
    private const uint SettledPollMilliseconds = 100;
    private const double FullscreenCheckInterval = 0.5;
    private const double RelaxAfter = 0.06;
    private const int MaxGpuLosses = 3;

    private static TrailEngine? s_current;

    private readonly AutoResetEvent _wake = new(false);
    private readonly TrailModel _model = new();
    private readonly TrailModel _preview = new();
    private readonly Particles _particles = new();
    private readonly ClickEffects _clicks = new();
    private readonly bool _allowGpu = true;
    private readonly Func<double, POINT>? _script;
    private readonly FrameProbe? _probe;
    private volatile RenderConfig _config;
    private volatile bool _exit;
    private volatile bool _displayChanged = true;
    private volatile bool _cursorsChanged = true;
    private volatile bool _previewRequested;
    private double _previewStart = -1;
    private float _previewX;
    private float _previewY;
    private Thread? _thread;

    private IOverlayPresenter? _presenter;
    private volatile bool _gpuUnavailable;
    private bool _preferredGpu;
    private int _gpuLosses;
    private IntPtr _timer;
    private IntPtr _inputWindow;
    private bool _listening;
    private double _lastActivity;
    private bool _wasEnabled;
    private double _lastFullscreenCheck = double.NegativeInfinity;
    private long _refreshTicks = Stopwatch.Frequency / 60;
    private long _lastPresent;
    private PixelBounds _screen = new(0, 0, 1, 1);
    private IntPtr _monitor;
    private IntPtr _fullscreenMonitor;
    private float _dpiScale = 1f;
    private float _cursorScale = 1f;
    private IntPtr _textCursor;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private int _wheelDelta;
    private int _horizontalWheelDelta;
    private double _lastWheel = double.NegativeInfinity;
    private float _originX = BehindOffsetX;
    private float _originY = BehindOffsetY;
    private double _lastSample = double.NegativeInfinity;
    private long _drawTicks;
    private long _uploadTicks;

    public TrailEngine(RenderConfig config) => _config = config;

    /// <summary>Development harness: a scripted pointer path, per-frame timings and a renderer choice.</summary>
    internal TrailEngine(RenderConfig config, Func<double, POINT> script, FrameProbe probe, bool allowGpu)
    {
        _config = config;
        _script = script;
        _probe = probe;
        _allowGpu = allowGpu;
    }

    /// <summary>Name of the renderer in use ("GPU" / "CPU"), or null before the first frame.</summary>
    public string? RendererName => _presenter?.Name;

    /// <summary>GPU acceleration is switched on but no usable GPU could be set up.</summary>
    public bool GpuUnavailable => _gpuUnavailable;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "MouseGlowTrail renderer" };
        _thread.Start();
    }

    public void Apply(RenderConfig config)
    {
        _config = config;
        _wake.Set();
    }

    /// <summary>Plays a short figure-of-eight flourish near the pointer so a new look can be seen at once.</summary>
    public void RequestPreview()
    {
        _previewRequested = true;
        _wake.Set();
    }

    public void Stop()
    {
        _exit = true;
        _wake.Set();
        _thread?.Join(2000);
    }

    private void Run()
    {
        s_current = this;
        if (_probe is not null)
        {
            _probe.RenderThreadId = (int)GetCurrentThreadId();
        }

        try
        {
            RegisterOverlayClass();
            _inputWindow = CreateInputWindow();
            _timer = CreateWaitableTimerExW(0, 0, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            var clock = Stopwatch.StartNew();
            var previous = 0.0;
            var failures = 0;
            while (!_exit)
            {
                try
                {
                    var input = Pump();
                    var wantWheel = _config.Enabled && _config.Wheel != WheelEffect.None;
                    if (_displayChanged)
                    {
                        _displayChanged = false;
                        RefreshDisplay();
                    }

                    if (_cursorsChanged)
                    {
                        _cursorsChanged = false;
                        RefreshCursors();
                    }

                    var config = _config;
                    var now = clock.Elapsed.TotalSeconds;
                    if (!config.Enabled)
                    {
                        // Paused: give back the window, the GPU device and every buffer.
                        _wasEnabled = false;
                        _wheelDelta = _horizontalWheelDelta = 0;
                        ResetScene();
                        ClosePresenter();
                        Listen(false);
                        Wait(INFINITE, QS_ALLINPUT);
                        // Without a window no display or cursor change notifications arrived meanwhile.
                        _displayChanged = _cursorsChanged = true;
                        previous = clock.Elapsed.TotalSeconds;
                        continue;
                    }

                    var frameStart = Stopwatch.GetTimestamp();
                    Sample(now, config);
                    AdvancePreview(now, config);
                    _model.Expire(now, config.Lifetime);
                    _preview.Expire(now, config.Lifetime);
                    _particles.Update((float)Math.Clamp(now - previous, 0, 0.05));
                    _clicks.Expire(now);
                    previous = now;

                    var updated = Stopwatch.GetTimestamp();
                    var presenter = EnsurePresenter(config.GpuAcceleration);
                    var presented = Present(presenter, now, config, out var bounds);
                    _probe?.Add(new FrameRecord
                    {
                        Time = now,
                        UpdateTicks = updated - frameStart,
                        DrawTicks = _drawTicks,
                        UploadTicks = _uploadTicks,
                        Width = presented ? bounds.Width : 0,
                        Height = presented ? bounds.Height : 0,
                        UploadArea = presented ? presenter.LastUploadArea : 0,
                        Presented = presented,
                        Failed = !presented && _drawTicks != 0
                    });
                    if (presented || input || _model.LastMovement >= now || _previewStart >= 0)
                    {
                        _lastActivity = now;
                    }

                    if (presented)
                    {
                        // Pure fade-outs look identical at half the rate on a high refresh display.
                        var relaxed = now - Math.Max(_model.LastMovement, _preview.LastMovement) > RelaxAfter &&
                                      _particles.Count == 0;
                        // Wheel effects need Raw Input all the time (there is no way to poll the wheel).
                        Listen(wantWheel);
                        Pace(relaxed);
                    }
                    else
                    {
                        presenter.Hide(now);
                        presenter.Idle(now);
                        var settled = now - _lastActivity > SettleDelay && Listen(true);
                        if (!settled)
                        {
                            // Just woken by input (or no Raw Input): poll, so motion is caught at once.
                            Listen(wantWheel);
                        }

                        // While polling, mouse input must not wake the thread (a gaming mouse reports
                        // thousands of times a second); it is read in batches on each poll instead.
                        Wait(settled ? SettledPollMilliseconds : IdlePollMilliseconds,
                            settled ? QS_ALLINPUT : QS_ALLINPUT & ~QS_RAWINPUT);
                    }

                    failures = 0;
                }
                catch (Exception exception) when (++failures < 5)
                {
                    AppLog.Write(exception);
                    Thread.Sleep(200);
                }
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
        finally
        {
            ClosePresenter();
            Listen(false);
            if (_inputWindow != 0)
            {
                DestroyWindow(_inputWindow);
                _inputWindow = 0;
            }

            if (_timer != 0)
            {
                CloseHandle(_timer);
                _timer = 0;
            }
        }
    }

    /// <summary>
    /// The GPU presenter when acceleration is on and a GPU can be set up, otherwise the CPU one. A GPU
    /// lost mid-way (driver update, device reset) gets a fresh device; after repeated losses, or when
    /// none can be created, the CPU path takes over.
    /// </summary>
    private IOverlayPresenter EnsurePresenter(bool preferGpu)
    {
        if (_presenter is { IsLost: true } lost)
        {
            if (lost is CompositionPresenter)
            {
                _gpuLosses++;
            }

            lost.Dispose();
            _presenter = null;
        }

        if (preferGpu && !_preferredGpu)
        {
            // Switched on (again): give a GPU that failed earlier another chance.
            _gpuUnavailable = false;
            _gpuLosses = 0;
        }

        _preferredGpu = preferGpu;
        var wantGpu = preferGpu && _allowGpu && !_gpuUnavailable && _gpuLosses < MaxGpuLosses;
        if (_presenter is not null && (_presenter is CompositionPresenter) != wantGpu)
        {
            ClosePresenter(); // the setting changed
        }

        if (_presenter is not null)
        {
            return _presenter;
        }

        if (wantGpu)
        {
            var gpu = CompositionPresenter.TryCreate(OverlayClass, _screen, out var error);
            if (gpu is not null)
            {
                return _presenter = gpu;
            }

            // Retried when the display configuration changes (new driver, remote session ended...).
            _gpuUnavailable = true;
            AppLog.Write($"GPU renderer unavailable ({error}); using the CPU renderer.");
        }

        return _presenter = new LayeredPresenter(OverlayClass, _screen);
    }

    private void ClosePresenter()
    {
        _presenter?.Dispose();
        _presenter = null;
    }

    private bool Present(IOverlayPresenter presenter, double now, RenderConfig config, out PixelBounds bounds)
    {
        _drawTicks = _uploadTicks = 0;
        if (!FrameComposer.TryGetBounds(_model, _particles, _clicks, config, out bounds, _preview))
        {
            return false;
        }

        var drawStart = Stopwatch.GetTimestamp();
        var sink = presenter.BeginFrame(bounds, out var originX, out var originY);
        if (sink is null)
        {
            return false;
        }

        FrameComposer.Draw(sink, _model, _particles, _clicks, config, now, originX, originY, _preview);
        var uploadStart = Stopwatch.GetTimestamp();
        var presented = presenter.EndFrame(now);
        _drawTicks = uploadStart - drawStart;
        _uploadTicks = Stopwatch.GetTimestamp() - uploadStart;
        return presented;
    }

    private void Sample(double now, RenderConfig config)
    {
        POINT point;
        if (_script is not null)
        {
            point = _script(now);
        }
        else
        {
            GetCursorPos(&point);
        }

        var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        if (monitor != _monitor)
        {
            _monitor = monitor;
            uint dpiX, dpiY;
            _dpiScale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, &dpiX, &dpiY) >= 0 && dpiX > 0 ? dpiX / 96f : 1f;
        }

        if (!config.HideInFullscreen)
        {
            _fullscreenMonitor = 0;
        }
        else if (now - _lastFullscreenCheck > FullscreenCheckInterval)
        {
            _lastFullscreenCheck = now;
            _fullscreenMonitor = FindFullscreenMonitor();
        }

        var suppressed = _fullscreenMonitor != 0 && _fullscreenMonitor == monitor && _script is null;

        // GetAsyncKeyState reports physical buttons; "left" here means the primary (logical) one.
        var swapped = _script is null && GetSystemMetrics(SM_SWAPBUTTON) != 0;
        var physicalLeft = _script is null && GetAsyncKeyState(0x01) < 0;
        var physicalRight = _script is null && GetAsyncKeyState(0x02) < 0;
        var left = swapped ? physicalRight : physicalLeft;
        var right = swapped ? physicalLeft : physicalRight;
        var middle = _script is null && GetAsyncKeyState(0x04) < 0;
        _model.CycleLength = config.Style.CycleLength;
        _model.SmoothingCutoff = config.SmoothingCutoff;
        _model.SpeedResponse = config.SpeedResponse;
        _particles.Options = config.Particles;
        _model.Spawner = config.Particles.Enabled ? _particles : null;

        // Follow the pointer whatever its shape, so sweeping across text boxes, links and window
        // edges never cuts the ribbon. The one exception is drag-selecting text, where the ribbon
        // would sit right on top of the selection.
        var cursor = new CURSORINFO { Size = sizeof(CURSORINFO) };
        var visible = _script is not null || (GetCursorInfo(&cursor) != 0 && (cursor.Flags & CURSOR_SHOWING) != 0);
        var selectingText = config.HideWhileSelecting && left && _textCursor != 0 && cursor.Cursor == _textCursor;
        UpdateOrigin(config, cursor.Cursor, now);
        if (!suppressed && visible && !selectingText)
        {
            var offset = _dpiScale * _cursorScale;
            _model.AddSample(point.X + _originX * offset, point.Y + _originY * offset, now, _dpiScale);
        }
        else
        {
            _model.EndStroke();
        }

        if (!_wasEnabled)
        {
            // Just switched on: a single ripple at the pointer confirms it without any text.
            _wasEnabled = true;
            if (!suppressed)
            {
                _clicks.Add(point.X, point.Y, now, _model.Phase, _dpiScale);
            }
        }

        if (!suppressed)
        {
            var effectScale = _dpiScale * config.EffectSize;
            if (left && !_leftDown)
            {
                EffectSpawner.Click(_clicks, _particles, config.LeftClick, point.X, point.Y, now, _model.Phase,
                    effectScale, config.LeftClickColor);
            }

            if (right && !_rightDown)
            {
                EffectSpawner.Click(_clicks, _particles, config.RightClick, point.X, point.Y, now, _model.Phase,
                    effectScale, config.RightClickColor);
            }

            if (middle && !_middleDown)
            {
                EffectSpawner.Click(_clicks, _particles, config.MiddleClick, point.X, point.Y, now, _model.Phase,
                    effectScale, config.MiddleClickColor);
            }

            if ((_wheelDelta != 0 || _horizontalWheelDelta != 0) && now - _lastWheel >= WheelInterval)
            {
                // Wheel forward scrolls the page up; tilting right scrolls right.
                var vertical = Math.Abs(_wheelDelta) >= Math.Abs(_horizontalWheelDelta);
                var directionX = vertical ? 0f : Math.Sign(_horizontalWheelDelta);
                var directionY = vertical ? -Math.Sign(_wheelDelta) : 0f;
                EffectSpawner.Wheel(_clicks, _particles, config.Wheel, point.X, point.Y, directionX, directionY, now,
                    _model.Phase, effectScale, config.WheelColor);
                _lastWheel = now;
                _wheelDelta = _horizontalWheelDelta = 0;
            }
        }
        else
        {
            _wheelDelta = _horizontalWheelDelta = 0;
        }

        _leftDown = left;
        _rightDown = right;
        _middleDown = middle;
    }

    /// <summary>
    /// Moves the ribbon's origin to where the settings put it. For "pointer centre" it glides over a
    /// few frames when the pointer changes shape, so the ribbon bends instead of jumping.
    /// </summary>
    private void UpdateOrigin(RenderConfig config, IntPtr cursor, double now)
    {
        var (targetX, targetY) = config.Origin switch
        {
            TrailOrigin.Tip => (0f, 0f),
            TrailOrigin.Center => _script is null ? CursorGeometry.CenterOffset(cursor) : (6f, 10f),
            TrailOrigin.Custom => (config.OffsetX, config.OffsetY),
            _ => (BehindOffsetX, BehindOffsetY)
        };
        var elapsed = now - _lastSample;
        _lastSample = now;
        if (config.Origin != TrailOrigin.Center || elapsed is > 0.25 or < 0)
        {
            _originX = targetX;
            _originY = targetY;
            return;
        }

        var blend = 1f - MathF.Exp(-(float)elapsed / 0.05f);
        _originX += (targetX - _originX) * blend;
        _originY += (targetY - _originY) * blend;
    }

    private void AdvancePreview(double now, RenderConfig config)
    {
        const double Duration = 0.8;
        if (_previewRequested)
        {
            _previewRequested = false;
            POINT point;
            GetCursorPos(&point);
            var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { Size = sizeof(MONITORINFO) };
            GetMonitorInfoW(monitor, &info);
            // Keep the flourish clear of the menu and the taskbar, inside the work area.
            var margin = 190f * _dpiScale;
            _previewX = Math.Clamp(point.X - 170f * _dpiScale, info.Work.Left + margin, MathF.Max(info.Work.Left + margin, info.Work.Right - margin));
            _previewY = Math.Clamp(point.Y - 150f * _dpiScale, info.Work.Top + margin * 0.6f, MathF.Max(info.Work.Top + margin * 0.6f, info.Work.Bottom - margin * 0.6f));
            _preview.Reset();
            _preview.CycleLength = config.Style.CycleLength;
            _previewStart = now;
        }

        if (_previewStart < 0)
        {
            return;
        }

        var t = (now - _previewStart) / Duration;
        if (t > 1)
        {
            _preview.EndStroke();
            _previewStart = -1;
            return;
        }

        // Ease in and out so the flourish accelerates and settles like a real gesture.
        var eased = t * t * (3 - 2 * t);
        var angle = 2 * Math.PI * eased;
        _preview.AddSample(_previewX + (float)(120 * Math.Sin(angle)) * _dpiScale,
            _previewY + (float)(46 * Math.Sin(2 * angle)) * _dpiScale, now, _dpiScale);
    }

    private void Pace(bool relaxed)
    {
        var frames = relaxed && _refreshTicks < Stopwatch.Frequency / 100 ? 2 : 1;
        for (var i = 0; i < frames; i++)
        {
            var result = DwmFlush();
            var elapsed = Stopwatch.GetTimestamp() - _lastPresent;
            // DwmFlush can return at once when nothing is pending; never exceed the refresh rate.
            if (result < 0 || elapsed < _refreshTicks * 3 / 5)
            {
                SleepTicks(_refreshTicks - elapsed);
            }

            _lastPresent = Stopwatch.GetTimestamp();
        }
    }

    private void SleepTicks(long ticks)
    {
        if (ticks <= 0)
        {
            return;
        }

        var dueTime = -Math.Max(1, ticks * 10_000_000 / Stopwatch.Frequency);
        if (_timer != 0 && SetWaitableTimer(_timer, &dueTime, 0, 0, 0, 0) != 0)
        {
            WaitForSingleObject(_timer, 100);
        }
        else
        {
            Thread.Sleep(1);
        }
    }

    private void Wait(uint milliseconds, uint wakeMask)
    {
        var handle = _wake.SafeWaitHandle.DangerousGetHandle();
        MsgWaitForMultipleObjectsEx(1, &handle, milliseconds, wakeMask, MWMO_INPUTAVAILABLE);
    }

    /// <summary>Dispatches pending messages; true when mouse input (WM_INPUT) was among them.</summary>
    private bool Pump()
    {
        var input = false;
        MSG message;
        while (PeekMessageW(&message, 0, 0, 0, PM_REMOVE) != 0)
        {
            if (message.Message == WM_INPUT)
            {
                input = true;
                ReadWheel(message.LParam);
            }

            TranslateMessage(&message);
            DispatchMessageW(&message);
        }

        return input;
    }

    /// <summary>Adds up wheel turns reported by Raw Input (RAWINPUTHEADER is 24 bytes, RAWMOUSE follows).</summary>
    private void ReadWheel(IntPtr rawInput)
    {
        const uint HeaderSize = 24;
        var buffer = stackalloc byte[64];
        uint size = 64;
        var read = GetRawInputData(rawInput, RID_INPUT, buffer, &size, HeaderSize);
        if (read == uint.MaxValue || read < HeaderSize + 8 || *(uint*)buffer != RIM_TYPEMOUSE)
        {
            return;
        }

        var flags = *(ushort*)(buffer + HeaderSize + 4);
        var delta = *(short*)(buffer + HeaderSize + 6);
        if ((flags & RI_MOUSE_WHEEL) != 0)
        {
            _wheelDelta += delta;
        }

        if ((flags & RI_MOUSE_HWHEEL) != 0)
        {
            _horizontalWheelDelta += delta;
        }
    }

    /// <summary>
    /// Starts or stops receiving mouse Raw Input, which wakes the thread from a settled wait. It is
    /// only on while settled: a gaming mouse reports up to 8000 times a second while moving.
    /// Returns whether it is on.
    /// </summary>
    private bool Listen(bool on)
    {
        if (on == _listening || _inputWindow == 0)
        {
            return _listening;
        }

        var device = new RAWINPUTDEVICE
        {
            UsagePage = 0x01, // generic desktop
            Usage = 0x02, // mouse
            Flags = on ? RIDEV_INPUTSINK : RIDEV_REMOVE,
            Target = on ? _inputWindow : 0
        };
        if (RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)) != 0)
        {
            _listening = on;
        }

        return _listening;
    }

    /// <summary>A message-only window that receives Raw Input for this thread.</summary>
    private static IntPtr CreateInputWindow()
    {
        fixed (char* className = OverlayClass)
        {
            return CreateWindowExW(0, className, null, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, GetModuleHandleW(0), 0);
        }
    }

    private void ResetScene()
    {
        _model.Reset();
        _preview.Reset();
        _previewStart = -1;
        _particles.Clear();
        _clicks.Clear();
    }

    private void RefreshDisplay()
    {
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        _screen = new PixelBounds(left, top, left + Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN)),
            top + Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN)));
        _monitor = 0;
        _presenter?.SetScreen(_screen);
        if (_gpuUnavailable)
        {
            // A new display configuration may come with a usable GPU; try it again.
            _gpuUnavailable = false;
            ClosePresenter();
        }

        var timing = new DWM_TIMING_INFO { Size = (uint)sizeof(DWM_TIMING_INFO) };
        long period = 0;
        if (DwmGetCompositionTimingInfo(0, &timing) >= 0)
        {
            period = (long)timing.RefreshPeriod;
            if (period <= 0 && timing.RefreshNumerator > 0 && timing.RefreshDenominator > 0)
            {
                period = Stopwatch.Frequency * timing.RefreshDenominator / timing.RefreshNumerator;
            }
        }

        _refreshTicks = period >= Stopwatch.Frequency / 500 && period <= Stopwatch.Frequency / 24
            ? period
            : Stopwatch.Frequency / 60;
    }

    private void RefreshCursors()
    {
        _textCursor = LoadCursorW(0, IDC_IBEAM);
        CursorGeometry.Reset();
        _cursorScale = 1f;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            if (key?.GetValue("CursorBaseSize") is int size && size is >= 32 and <= 256)
            {
                _cursorScale = size / 32f;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write(exception);
        }
    }

    /// <summary>Monitor covered by a borderless foreground window (game, video, slideshow), or 0.</summary>
    private static IntPtr FindFullscreenMonitor()
    {
        var window = GetForegroundWindow();
        if (window == 0 || window == GetShellWindow() || window == GetDesktopWindow())
        {
            return 0;
        }

        // Captioned or maximized windows are ordinary apps, even when an auto-hiding taskbar lets a
        // maximized frameless window span the whole monitor.
        var style = (long)GetWindowLongPtrW(window, GWL_STYLE);
        if ((style & WS_CAPTION) == WS_CAPTION || (style & WS_MAXIMIZE) != 0)
        {
            return 0;
        }

        var name = stackalloc char[64];
        var length = GetClassNameW(window, name, 64);
        var className = new ReadOnlySpan<char>(name, Math.Max(0, length));
        if (className.SequenceEqual("WorkerW") || className.SequenceEqual("Progman") ||
            className.SequenceEqual("Shell_TrayWnd"))
        {
            return 0;
        }

        RECT rect;
        if (GetWindowRect(window, &rect) == 0)
        {
            return 0;
        }

        var monitor = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { Size = sizeof(MONITORINFO) };
        if (GetMonitorInfoW(monitor, &info) == 0)
        {
            return 0;
        }

        return rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right &&
               rect.Bottom >= info.Monitor.Bottom
            ? monitor
            : 0;
    }

    private static void RegisterOverlayClass()
    {
        fixed (char* className = OverlayClass)
        {
            var windowClass = new WNDCLASSEXW
            {
                Size = (uint)sizeof(WNDCLASSEXW),
                WndProc = &OverlayProc,
                Instance = GetModuleHandleW(0),
                ClassName = className
            };
            RegisterClassExW(&windowClass);
        }
    }

    [System.Runtime.InteropServices.UnmanagedCallersOnly]
    private static IntPtr OverlayProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_NCHITTEST:
                return HTTRANSPARENT;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_DPICHANGED:
                // The overlay sizes itself explicitly; ignore the suggested rectangle.
                return 0;
            case WM_DISPLAYCHANGE:
                if (s_current is { } displayEngine)
                {
                    displayEngine._displayChanged = true;
                }

                break;
            case WM_SETTINGCHANGE:
                if (s_current is { } cursorEngine && (uint)wParam == SPI_SETCURSORS)
                {
                    cursorEngine._cursorsChanged = true;
                }

                break;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
