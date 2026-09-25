using static MouseGlowTrail.D3D;
using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

/// <summary>Puts a drawn frame on screen in a click-through, topmost overlay window.</summary>
internal interface IOverlayPresenter : IDisposable
{
    /// <summary>"GPU" or "CPU", for logs and the benchmark.</summary>
    string Name { get; }

    /// <summary>The presenter can no longer draw (for example the GPU was reset) and must be replaced.</summary>
    bool IsLost { get; }

    /// <summary>Pixels handed to the compositor by the last frame (benchmark only).</summary>
    int LastUploadArea { get; }

    /// <summary>
    /// Starts a frame showing <paramref name="bounds"/> (screen pixels). Returns where to draw, and the
    /// origin to subtract from screen coordinates; null when there is nothing to draw on.
    /// </summary>
    IPrimitiveSink? BeginFrame(in PixelBounds bounds, out float originX, out float originY);

    /// <summary>Puts the frame on screen; false if that failed.</summary>
    bool EndFrame(double now);

    /// <summary>Takes everything off the screen.</summary>
    void Hide(double now);

    /// <summary>Called while there is nothing to show; frees memory once hidden for a while.</summary>
    void Idle(double now);

    void SetScreen(in PixelBounds screen);
}

internal static unsafe class OverlayWindow
{
    public const double ZOrderInterval = 0.25;

    /// <summary>How long the overlay stays hidden before its buffers are freed (shortened by the benchmark).</summary>
    public static double ReleaseDelay { get; set; } = 8.0;

    public static IntPtr Create(string className, uint extraStyle, in PixelBounds rect)
    {
        IntPtr window;
        fixed (char* name = className)
        fixed (char* title = "MouseGlowTrail")
        {
            window = CreateWindowExW(
                WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | extraStyle,
                name, title, WS_POPUP, rect.Left, rect.Top, rect.Width, rect.Height, 0, 0, GetModuleHandleW(0), 0);
        }

        if (window == 0)
        {
            throw new InvalidOperationException("Unable to create the trail overlay window.");
        }

        // No fade or zoom animation when the overlay appears or disappears.
        var disable = 1;
        DwmSetWindowAttribute(window, DWMWA_TRANSITIONS_FORCEDISABLED, &disable, sizeof(int));
        return window;
    }

    public static void Show(IntPtr window) => SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_SHOWWINDOW);

    /// <summary>Other topmost windows can be raised above us; reclaim the top without activating.</summary>
    public static void RaiseToTop(IntPtr window) => SetWindowPos(window, HWND_TOPMOST, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    public static void Hide(IntPtr window) => SetWindowPos(window, 0, 0, 0, 0, 0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);
}

/// <summary>
/// Software path: <see cref="Canvas"/> draws into a DIB shown by a layered window that is only a bit
/// larger than the trail. The window stays put while the trail fits inside it, and then each frame
/// hands the compositor just the rectangle that changed; when the trail outgrows it, one
/// UpdateLayeredWindow moves, resizes and repaints it atomically.
/// </summary>
internal sealed unsafe class LayeredPresenter : IOverlayPresenter
{
    private readonly Canvas _canvas = new();
    private readonly IntPtr _window;
    private IntPtr _memoryDc;
    private IntPtr _surface;
    private IntPtr _previousBitmap;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private int _windowWidth;
    private int _windowHeight;
    private int _x;
    private int _y;
    private PixelBounds _screen;
    private PixelBounds _content;
    private PixelBounds _previous;
    private bool _placed;
    private bool _visible;
    private double _hiddenSince;
    private double _lastZOrder;

    public LayeredPresenter(string windowClass, in PixelBounds screen)
    {
        _screen = screen;
        _window = OverlayWindow.Create(windowClass, WS_EX_LAYERED, new PixelBounds(0, 0, 1, 1));
    }

    public string Name => "CPU";

    public bool IsLost => false;

    public int LastUploadArea { get; private set; }

    public IPrimitiveSink? BeginFrame(in PixelBounds bounds, out float originX, out float originY)
    {
        originX = originY = 0;
        var content = bounds.Intersect(_screen);
        if (content.Width <= 0 || content.Height <= 0)
        {
            return null;
        }

        var width = TrailEngineMath.PickExtent(_windowWidth, content.Width, _screen.Width);
        var height = TrailEngineMath.PickExtent(_windowHeight, content.Height, _screen.Height);
        if (!_placed || width != _windowWidth || height != _windowHeight ||
            content.Intersect(new PixelBounds(_x, _y, _x + width, _y + height)) != content)
        {
            // Re-centre only when the trail no longer fits: moving shifts every pixel of the window.
            _windowWidth = width;
            _windowHeight = height;
            _x = Math.Clamp(content.Left - (width - content.Width) / 2, _screen.Left, _screen.Right - width);
            _y = Math.Clamp(content.Top - (height - content.Height) / 2, _screen.Top, _screen.Bottom - height);
            _placed = false;
        }

        if (!EnsureSurface(_windowWidth, _windowHeight))
        {
            return null;
        }

        _content = content;
        _canvas.BeginFrame(_windowWidth, _windowHeight);
        originX = _x;
        originY = _y;
        return _canvas;
    }

    public bool EndFrame(double now)
    {
        // UpdateLayeredWindow moves, resizes and repaints atomically, so the trail never flashes at a
        // stale position; only z-order is managed separately.
        var destination = new POINT(_x, _y);
        var size = new SIZE(_windowWidth, _windowHeight);
        var source = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        // What changed since the last frame: where the trail is now and where it was (window pixels).
        var changed = _content.Union(_previous);
        var dirty = new RECT
        {
            Left = changed.Left - _x,
            Top = changed.Top - _y,
            Right = changed.Right - _x,
            Bottom = changed.Bottom - _y
        };
        var partial = _placed && _visible;
        var info = new UPDATELAYEREDWINDOWINFO
        {
            Size = (uint)sizeof(UPDATELAYEREDWINDOWINFO),
            Destination = &destination,
            WindowSize = &size,
            SourceDc = _memoryDc,
            Source = &source,
            Blend = &blend,
            Flags = ULW_ALPHA,
            Dirty = partial ? &dirty : null
        };
        if (UpdateLayeredWindowIndirect(_window, &info) == 0)
        {
            _placed = false;
            return false;
        }

        LastUploadArea = partial ? changed.Width * changed.Height : _windowWidth * _windowHeight;
        _placed = true;
        _previous = _content;
        if (!_visible)
        {
            OverlayWindow.Show(_window);
            _visible = true;
            _lastZOrder = now;
        }
        else if (now - _lastZOrder > OverlayWindow.ZOrderInterval)
        {
            OverlayWindow.RaiseToTop(_window);
            _lastZOrder = now;
        }

        return true;
    }

    public void Hide(double now)
    {
        if (!_visible)
        {
            return;
        }

        OverlayWindow.Hide(_window);
        _visible = false;
        _placed = false;
        _hiddenSince = now;
        _canvas.ClearDirty();
    }

    public void Idle(double now)
    {
        if (!_visible && _surface != 0 && now - _hiddenSince > OverlayWindow.ReleaseDelay)
        {
            ReleaseSurface();
            _windowWidth = _windowHeight = 0;
        }
    }

    public void SetScreen(in PixelBounds screen)
    {
        _screen = screen;
        _placed = false;
    }

    /// <summary>
    /// Makes the DIB at least <paramref name="width"/> x <paramref name="height"/>. It only grows (with
    /// headroom, so a trail that is still lengthening does not reallocate every frame) until it is
    /// released after a long idle spell; the window extent is never touched here.
    /// </summary>
    private bool EnsureSurface(int width, int height)
    {
        if (_surface != 0 && width <= _surfaceWidth && height <= _surfaceHeight)
        {
            return true;
        }

        width = Math.Min(Math.Max(_surfaceWidth, (width + width / 4 + 255) & ~255), Math.Max(width, _screen.Width));
        height = Math.Min(Math.Max(_surfaceHeight, (height + height / 4 + 255) & ~255), Math.Max(height, _screen.Height));
        ReleaseSurface();
        if (_memoryDc == 0)
        {
            _memoryDc = CreateCompatibleDC(0);
        }

        var header = new BITMAPINFOHEADER
        {
            Size = (uint)sizeof(BITMAPINFOHEADER),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32
        };
        void* bits;
        _surface = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (_surface == 0 || bits == null)
        {
            _surface = 0;
            return false;
        }

        _previousBitmap = SelectObject(_memoryDc, _surface);
        _surfaceWidth = width;
        _surfaceHeight = height;
        _canvas.Attach((uint*)bits, width, height);
        return true;
    }

    private void ReleaseSurface()
    {
        if (_surface == 0)
        {
            return;
        }

        _canvas.Detach();
        SelectObject(_memoryDc, _previousBitmap);
        DeleteObject(_surface);
        _surface = 0;
        _surfaceWidth = _surfaceHeight = 0;
    }

    public void Dispose()
    {
        ReleaseSurface();
        if (_memoryDc != 0)
        {
            DeleteDC(_memoryDc);
            _memoryDc = 0;
        }

        DestroyWindow(_window);
    }
}

/// <summary>
/// GPU path: <see cref="GpuCanvas"/> renders each frame and DirectComposition shows it. The window
/// spans the whole virtual screen but has no redirection bitmap, and its content is a sparse virtual
/// surface in screen coordinates: only the tiles around the trail exist, the window never moves, and
/// each frame re-sends just the rectangle that changed. Nothing is uploaded from system memory.
/// </summary>
internal sealed unsafe class CompositionPresenter : IOverlayPresenter
{
    private readonly GpuCanvas _canvas;
    private IntPtr _window;
    private void* _device;
    private void* _target;
    private void* _visual;
    private void* _surface;
    private PixelBounds _screen;
    private PixelBounds _content;
    private PixelBounds _region;
    private PixelBounds _previous;
    private bool _hasPrevious;
    private bool _visible;
    private double _hiddenSince;
    private double _lastZOrder;

    private CompositionPresenter(GpuCanvas canvas) => _canvas = canvas;

    public string Name => "GPU";

    public bool IsLost { get; private set; }

    public int LastUploadArea { get; private set; }

    public string AdapterName => _canvas.AdapterName;

    public static CompositionPresenter? TryCreate(string windowClass, in PixelBounds screen, out string? error)
    {
        var canvas = GpuCanvas.TryCreate(out error);
        if (canvas is null)
        {
            return null;
        }

        var presenter = new CompositionPresenter(canvas);
        try
        {
            var hr = presenter.Initialize(windowClass, screen);
            if (hr >= 0)
            {
                return presenter;
            }

            error = $"DirectComposition 0x{hr:X8}";
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        presenter.Dispose();
        return null;
    }

    private int Initialize(string windowClass, in PixelBounds screen)
    {
        _screen = screen;
        _window = OverlayWindow.Create(windowClass, WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP, screen);
        // Layered (for click-through) and fully opaque as a layer; the content brings its own alpha.
        SetLayeredWindowAttributes(_window, 0, 255, LWA_ALPHA);

        void* dxgiDevice;
        var hr = QueryInterface(_canvas.Device, IID_IDXGIDevice, &dxgiDevice);
        if (hr < 0)
        {
            return hr;
        }

        void* device;
        var iid = IID_IDCompositionDevice;
        hr = DCompositionCreateDevice(dxgiDevice, &iid, &device);
        Release(ref dxgiDevice);
        if (hr < 0)
        {
            return hr;
        }

        _device = device;
        void* target;
        if ((hr = CreateTargetForHwnd(device, _window, true, &target)) < 0)
        {
            return hr;
        }

        _target = target;
        void* visual;
        if ((hr = CreateVisual(device, &visual)) < 0)
        {
            return hr;
        }

        _visual = visual;
        if ((hr = CreateSurface(screen)) < 0 || (hr = SetRoot(target, visual)) < 0)
        {
            return hr;
        }

        return Commit(device);
    }

    private int CreateSurface(in PixelBounds screen)
    {
        void* surface;
        var hr = CreateVirtualSurface(_device, (uint)screen.Width, (uint)screen.Height, DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE_PREMULTIPLIED, &surface);
        if (hr < 0)
        {
            return hr;
        }

        Release(ref _surface);
        _surface = surface;
        return SetContent(_visual, surface);
    }

    public IPrimitiveSink? BeginFrame(in PixelBounds bounds, out float originX, out float originY)
    {
        originX = originY = 0;
        var content = bounds.Intersect(_screen);
        if (content.Width <= 0 || content.Height <= 0 || IsLost)
        {
            return null;
        }

        // Redraw what the trail covers now plus what it covered last frame, which must be erased.
        _content = content;
        _region = _hasPrevious ? content.Union(_previous) : content;
        _canvas.BeginFrame(_region);
        return _canvas;
    }

    public bool EndFrame(double now)
    {
        var hr = _canvas.Render();
        if (hr >= 0)
        {
            hr = CopyToSurface();
        }

        if (hr >= 0)
        {
            // Drop surface tiles the trail has left, so the compositor only blends where it is.
            var keep = Local(_content);
            hr = Trim(_surface, &keep, 1);
        }

        if (hr >= 0)
        {
            hr = Commit(_device);
        }

        if (hr < 0)
        {
            Fail(hr);
            return false;
        }

        LastUploadArea = _region.Width * _region.Height;
        _previous = _content;
        _hasPrevious = true;
        if (!_visible)
        {
            OverlayWindow.Show(_window);
            _visible = true;
            _lastZOrder = now;
        }
        else if (now - _lastZOrder > OverlayWindow.ZOrderInterval)
        {
            OverlayWindow.RaiseToTop(_window);
            _lastZOrder = now;
        }

        return true;
    }

    private int CopyToSurface()
    {
        var update = Local(_region);
        void* texture;
        POINT offset;
        var hr = BeginDraw(_surface, &update, IID_ID3D11Texture2D, &texture, &offset);
        if (hr < 0)
        {
            return hr;
        }

        var box = new D3D11_BOX { Right = (uint)_region.Width, Bottom = (uint)_region.Height, Back = 1 };
        CopySubresourceRegion(_canvas.Context, texture, (uint)offset.X, (uint)offset.Y, _canvas.Target, &box);
        Release(ref texture);
        return EndDraw(_surface);
    }

    private RECT Local(in PixelBounds bounds) => new()
    {
        Left = bounds.Left - _screen.Left,
        Top = bounds.Top - _screen.Top,
        Right = bounds.Right - _screen.Left,
        Bottom = bounds.Bottom - _screen.Top
    };

    public void Hide(double now)
    {
        if (_visible)
        {
            OverlayWindow.Hide(_window);
            _visible = false;
            _hiddenSince = now;
        }

        if (_hasPrevious && !IsLost)
        {
            _hasPrevious = false;
            var hr = Trim(_surface, null, 0);
            if (hr >= 0)
            {
                hr = Commit(_device);
            }

            if (hr < 0)
            {
                Fail(hr);
            }
        }
    }

    public void Idle(double now)
    {
        if (!_visible && now - _hiddenSince > OverlayWindow.ReleaseDelay)
        {
            _canvas.ReleaseTarget();
        }
    }

    public void SetScreen(in PixelBounds screen)
    {
        if (screen == _screen || IsLost)
        {
            return;
        }

        _screen = screen;
        _hasPrevious = false;
        SetWindowPos(_window, 0, screen.Left, screen.Top, screen.Width, screen.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        var hr = Resize(_surface, (uint)screen.Width, (uint)screen.Height);
        if (hr >= 0)
        {
            hr = Trim(_surface, null, 0);
        }

        if (hr >= 0)
        {
            hr = Commit(_device);
        }

        if (hr < 0)
        {
            Fail(hr);
        }
    }

    private void Fail(int hr)
    {
        if (!IsLost)
        {
            IsLost = true;
            var reason = _canvas.Device != null ? GetDeviceRemovedReason(_canvas.Device) : 0;
            AppLog.Write($"GPU presentation failed (0x{hr:X8}, device state 0x{reason:X8}); switching renderer.");
        }
    }

    public void Dispose()
    {
        if (_visual != null)
        {
            SetContent(_visual, null);
        }

        Release(ref _surface);
        Release(ref _visual);
        Release(ref _target);
        Release(ref _device);
        if (_window != 0)
        {
            DestroyWindow(_window);
            _window = 0;
        }

        _canvas.Dispose();
    }
}
