using static MouseGlowTrail.D2D;

namespace MouseGlowTrail.Ui;

internal enum TextStyle
{
    Caption,
    Body,
    BodyStrong,
    Subtitle,
    Title,
    IconSmall,
    Icon,
    IconLarge
}

internal readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public float CenterX => X + W * 0.5f;
    public float CenterY => Y + H * 0.5f;

    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;

    public RectF Inflate(float amount) => new(X - amount, Y - amount, W + 2 * amount, H + 2 * amount);

    public RectF Inflate(float dx, float dy) => new(X - dx, Y - dy, W + 2 * dx, H + 2 * dy);

    public RectF Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);

    public RectF Intersect(RectF other)
    {
        var left = MathF.Max(X, other.X);
        var top = MathF.Max(Y, other.Y);
        var right = MathF.Min(Right, other.Right);
        var bottom = MathF.Min(Bottom, other.Bottom);
        return new RectF(left, top, MathF.Max(0, right - left), MathF.Max(0, bottom - top));
    }
}

/// <summary>
/// Direct2D drawing for the control panel, in device-independent pixels. Edges are snapped to whole
/// physical pixels so hairlines stay crisp at any scaling. While <see cref="Rendering"/> is false every
/// drawing call is skipped, which lets the same layout code run as an input-only pass.
/// </summary>
internal sealed unsafe class Gfx : IDisposable
{
    private const string TextFamily = "Segoe UI Variable Text";
    private const string DisplayFamily = "Segoe UI Variable Display";

    private readonly Dictionary<(TextStyle Style, bool Wrap), IntPtr> _formats = [];
    private readonly Dictionary<LayoutKey, LayoutEntry> _layouts = [];
    private readonly Dictionary<TextStyle, IntPtr> _signs = [];
    private void* _factory;
    private void* _write;
    private void* _window;
    private void* _target;
    private void* _brush;
    private void* _roundStroke;
    private void* _ellipsis;
    private IntPtr _hwnd;
    private int _frame;
    private string _iconFamily = "Segoe Fluent Icons";

    public float Dpi { get; private set; } = 96f;

    public float Scale => Dpi / 96f;

    /// <summary>Multiplies the alpha of everything drawn (fading flyouts in and out).</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>False during input-only passes: layout runs, nothing is drawn.</summary>
    public bool Rendering { get; set; }

    /// <summary>Bumped whenever device resources were lost, so owners recreate bitmaps and layers.</summary>
    public int Generation { get; private set; }

    public void* Factory => _factory;

    public void* WindowTarget => _window;

    public bool Initialize(IntPtr hwnd, float dpi)
    {
        _hwnd = hwnd;
        Dpi = dpi;
        void* factory;
        var iid = IID_ID2D1Factory;
        if (D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &iid, null, &factory) < 0)
        {
            return false;
        }

        _factory = factory;
        void* write;
        iid = IID_IDWriteFactory;
        if (DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, &iid, &write) < 0)
        {
            return false;
        }

        _write = write;
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!File.Exists(Path.Combine(fonts, "SegoeIcons.ttf")))
        {
            _iconFamily = "Segoe MDL2 Assets";
        }

        var strokeProperties = new D2D_STROKE_STYLE_PROPERTIES
        {
            StartCap = D2D1_CAP_STYLE_ROUND,
            EndCap = D2D1_CAP_STYLE_ROUND,
            DashCap = D2D1_CAP_STYLE_ROUND,
            LineJoin = D2D1_LINE_JOIN_ROUND,
            MiterLimit = 10f
        };
        void* stroke;
        if (CreateStrokeStyle(_factory, &strokeProperties, &stroke) >= 0)
        {
            _roundStroke = stroke;
        }

        return EnsureTarget();
    }

    /// <summary>Creates the window render target (again, after a device loss).</summary>
    public bool EnsureTarget()
    {
        if (_window != null)
        {
            return true;
        }

        RECT client;
        Native.GetClientRect(_hwnd, &client);
        var properties = new D2D_RENDER_TARGET_PROPERTIES
        {
            Type = D2D1_RENDER_TARGET_TYPE_DEFAULT,
            PixelFormat = new D2D_PIXEL_FORMAT { Format = DXGI_FORMAT_B8G8R8A8_UNORM, AlphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED },
            DpiX = Dpi,
            DpiY = Dpi
        };
        var hwndProperties = new D2D_HWND_RENDER_TARGET_PROPERTIES
        {
            Hwnd = _hwnd,
            PixelSize = new D2D_SIZE_U
            {
                Width = (uint)Math.Max(1, client.Right - client.Left),
                Height = (uint)Math.Max(1, client.Bottom - client.Top)
            },
            PresentOptions = D2D1_PRESENT_OPTIONS_NONE
        };
        void* target;
        if (CreateHwndRenderTarget(_factory, &properties, &hwndProperties, &target) < 0)
        {
            return false;
        }

        _window = target;
        var white = new D2D_COLOR(1, 1, 1, 1);
        void* brush;
        CreateSolidColorBrush(target, &white, &brush);
        _brush = brush;
        SetTextAntialiasMode(target, D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        _target = target;
        Generation++;
        return true;
    }

    public void Resize(int width, int height)
    {
        if (_window == null)
        {
            return;
        }

        var size = new D2D_SIZE_U { Width = (uint)Math.Max(1, width), Height = (uint)Math.Max(1, height) };
        D2D.Resize(_window, &size);
    }

    public void SetDpi(float dpi)
    {
        Dpi = dpi;
        if (_window != null)
        {
            D2D.SetDpi(_window, dpi, dpi);
        }
    }

    /// <summary>Starts drawing into the window.</summary>
    public void BeginWindow()
    {
        _target = _window;
        _frame++;
        BeginDraw(_window);
        var identity = D2D_MATRIX.Identity;
        SetTransform(_window, &identity);
    }

    /// <summary>Presents; false when the device was lost (everything device-bound is recreated next frame).</summary>
    public bool EndWindow()
    {
        var hr = EndDraw(_window);
        if (hr == D2DERR_RECREATE_TARGET)
        {
            DiscardDevice();
            return false;
        }

        TrimLayouts();
        return hr >= 0;
    }

    /// <summary>
    /// An offscreen layer of the given pixel size (for caching the static part of the UI). The size is
    /// explicit: the window target may not have caught up with a resize yet.
    /// </summary>
    public void* CreateLayer(int width, int height)
    {
        var dips = stackalloc float[2] { width / Scale, height / Scale };
        var pixels = new D2D_SIZE_U { Width = (uint)Math.Max(1, width), Height = (uint)Math.Max(1, height) };
        void* layer;
        return CreateCompatibleRenderTarget(_window, dips, &pixels, &layer) >= 0 ? layer : null;
    }

    public void BeginLayer(void* layer)
    {
        _target = layer;
        BeginDraw(layer);
        var identity = D2D_MATRIX.Identity;
        SetTransform(layer, &identity);
        SetTextAntialiasMode(layer, D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        var clear = default(D2D_COLOR);
        D2D.Clear(layer, &clear);
    }

    public void EndLayer(void* layer)
    {
        EndDraw(layer);
        _target = _window;
    }

    public void DrawLayer(void* layer, float width, float height)
    {
        void* bitmap;
        if (GetBitmap(layer, &bitmap) >= 0)
        {
            var rect = new D2D_RECT(0, 0, width, height);
            D2D.DrawBitmap(_target, bitmap, &rect, 1f, &rect);
            Release(ref bitmap);
        }
    }

    public void Clear(uint argb)
    {
        var color = D2D_COLOR.FromArgb(argb);
        // Premultiply for the target.
        color.R *= color.A;
        color.G *= color.A;
        color.B *= color.A;
        D2D.Clear(_target, &color);
    }

    public float Snap(float value) => MathF.Round(value * Scale) / Scale;

    private RectF SnapRect(RectF r)
    {
        var left = Snap(r.X);
        var top = Snap(r.Y);
        return new RectF(left, top, Snap(r.Right) - left, Snap(r.Bottom) - top);
    }

    public void Fill(RectF rect, uint argb, float radius = 0f)
    {
        if (!Rendering || argb >> 24 == 0 || rect.W <= 0 || rect.H <= 0)
        {
            return;
        }

        SetBrush(argb);
        rect = SnapRect(rect);
        var r = new D2D_RECT(rect.X, rect.Y, rect.Right, rect.Bottom);
        if (radius <= 0f)
        {
            FillRectangle(_target, &r, _brush);
        }
        else
        {
            var rounded = new D2D_ROUNDED_RECT { Rect = r, RadiusX = radius, RadiusY = radius };
            FillRoundedRectangle(_target, &rounded, _brush);
        }
    }

    /// <summary>A border drawn inside the rectangle, snapped to whole pixels.</summary>
    public void Stroke(RectF rect, uint argb, float width = 1f, float radius = 0f)
    {
        if (!Rendering || argb >> 24 == 0)
        {
            return;
        }

        SetBrush(argb);
        rect = SnapRect(rect);
        var pixels = MathF.Max(1f, MathF.Round(width * Scale));
        var inset = pixels * 0.5f / Scale;
        var r = new D2D_ROUNDED_RECT
        {
            Rect = new D2D_RECT(rect.X + inset, rect.Y + inset, rect.Right - inset, rect.Bottom - inset),
            RadiusX = MathF.Max(0, radius - inset),
            RadiusY = MathF.Max(0, radius - inset)
        };
        DrawRoundedRectangle(_target, &r, _brush, pixels / Scale);
    }

    public void FillEllipse(float cx, float cy, float rx, float ry, uint argb)
    {
        if (!Rendering || argb >> 24 == 0)
        {
            return;
        }

        SetBrush(argb);
        var ellipse = new D2D_ELLIPSE { Center = new D2D_POINT(cx, cy), RadiusX = rx, RadiusY = ry };
        D2D.FillEllipse(_target, &ellipse, _brush);
    }

    public void StrokeEllipse(float cx, float cy, float rx, float ry, uint argb, float width)
    {
        if (!Rendering || argb >> 24 == 0)
        {
            return;
        }

        SetBrush(argb);
        var ellipse = new D2D_ELLIPSE { Center = new D2D_POINT(cx, cy), RadiusX = rx, RadiusY = ry };
        D2D.DrawEllipse(_target, &ellipse, _brush, width);
    }

    public void Line(float x1, float y1, float x2, float y2, uint argb, float width, bool round = true)
    {
        if (!Rendering || argb >> 24 == 0)
        {
            return;
        }

        SetBrush(argb);
        D2D.DrawLine(_target, new D2D_POINT(x1, y1), new D2D_POINT(x2, y2), _brush, width, round ? _roundStroke : null);
    }

    /// <summary>A filled or stroked polygon / polyline.</summary>
    public void Polygon(ReadOnlySpan<D2D_POINT> points, uint fill, uint stroke = 0, float strokeWidth = 1f, bool closed = true)
    {
        if (!Rendering || points.Length < 2)
        {
            return;
        }

        void* path;
        if (CreatePathGeometry(_factory, &path) < 0)
        {
            return;
        }

        void* sink;
        if (Open(path, &sink) >= 0)
        {
            BeginFigure(sink, points[0], fill >> 24 != 0 ? D2D1_FIGURE_BEGIN_FILLED : D2D1_FIGURE_BEGIN_HOLLOW);
            for (var i = 1; i < points.Length; i++)
            {
                AddLine(sink, points[i]);
            }

            EndFigure(sink, closed ? D2D1_FIGURE_END_CLOSED : D2D1_FIGURE_END_OPEN);
            CloseSink(sink);
            Release(ref sink);
            if (fill >> 24 != 0)
            {
                SetBrush(fill);
                FillGeometry(_target, path, _brush);
            }

            if (stroke >> 24 != 0)
            {
                SetBrush(stroke);
                DrawGeometry(_target, path, _brush, strokeWidth, _roundStroke);
            }
        }

        Release(ref path);
    }

    /// <summary>A rectangle whose top-left corner alone is rounded (the Settings-style content layer).</summary>
    public void FillTopLeftRounded(RectF rect, float radius, uint fill, uint stroke)
    {
        if (!Rendering)
        {
            return;
        }

        rect = SnapRect(rect);
        void* path;
        if (CreatePathGeometry(_factory, &path) < 0)
        {
            return;
        }

        void* sink;
        if (Open(path, &sink) >= 0)
        {
            var inset = 0.5f / Scale;
            float left = rect.X + inset, top = rect.Y + inset, right = rect.Right + 4f, bottom = rect.Bottom + 4f;
            BeginFigure(sink, new D2D_POINT(left, bottom), D2D1_FIGURE_BEGIN_FILLED);
            AddLine(sink, new D2D_POINT(left, top + radius));
            var arc = new D2D_ARC_SEGMENT
            {
                Point = new D2D_POINT(left + radius, top),
                Width = radius,
                Height = radius,
                SweepDirection = D2D1_SWEEP_DIRECTION_CLOCKWISE,
                ArcSize = D2D1_ARC_SIZE_SMALL
            };
            AddArc(sink, &arc);
            AddLine(sink, new D2D_POINT(right, top));
            EndFigure(sink, D2D1_FIGURE_END_OPEN);
            CloseSink(sink);
            Release(ref sink);
            SetBrush(fill);
            FillGeometry(_target, path, _brush);
            SetBrush(stroke);
            DrawGeometry(_target, path, _brush, 1f / Scale, null);
        }

        Release(ref path);
    }

    public void FillGradient(RectF rect, float radius, ReadOnlySpan<(float Position, uint Color)> stops, bool vertical)
    {
        if (!Rendering || stops.Length == 0)
        {
            return;
        }

        rect = SnapRect(rect);
        var gradient = stackalloc D2D_GRADIENT_STOP[stops.Length];
        for (var i = 0; i < stops.Length; i++)
        {
            gradient[i] = new D2D_GRADIENT_STOP { Position = stops[i].Position, Color = D2D_COLOR.FromArgb(stops[i].Color) };
            gradient[i].Color.A *= Opacity;
        }

        void* collection;
        if (CreateGradientStopCollection(_target, gradient, (uint)stops.Length, &collection) < 0)
        {
            return;
        }

        var properties = new D2D_LINEAR_GRADIENT_BRUSH_PROPERTIES
        {
            Start = new D2D_POINT(rect.X, rect.Y),
            End = vertical ? new D2D_POINT(rect.X, rect.Bottom) : new D2D_POINT(rect.Right, rect.Y)
        };
        void* brush;
        if (CreateLinearGradientBrush(_target, &properties, collection, &brush) >= 0)
        {
            FillWith(rect, radius, brush);
            Release(ref brush);
        }

        Release(ref collection);
    }

    public void FillRadial(RectF rect, float radius, float cx, float cy, float rx, float ry, uint inner, uint outer)
    {
        if (!Rendering)
        {
            return;
        }

        rect = SnapRect(rect);
        var gradient = stackalloc D2D_GRADIENT_STOP[2];
        gradient[0] = new D2D_GRADIENT_STOP { Position = 0f, Color = D2D_COLOR.FromArgb(inner) };
        gradient[1] = new D2D_GRADIENT_STOP { Position = 1f, Color = D2D_COLOR.FromArgb(outer) };
        void* collection;
        if (CreateGradientStopCollection(_target, gradient, 2, &collection) < 0)
        {
            return;
        }

        var properties = new D2D_RADIAL_GRADIENT_BRUSH_PROPERTIES
        {
            Center = new D2D_POINT(cx, cy),
            RadiusX = rx,
            RadiusY = ry
        };
        void* brush;
        if (CreateRadialGradientBrush(_target, &properties, collection, &brush) >= 0)
        {
            FillWith(rect, radius, brush);
            Release(ref brush);
        }

        Release(ref collection);
    }

    private void FillWith(RectF rect, float radius, void* brush)
    {
        var r = new D2D_RECT(rect.X, rect.Y, rect.Right, rect.Bottom);
        if (radius <= 0f)
        {
            FillRectangle(_target, &r, brush);
        }
        else
        {
            var rounded = new D2D_ROUNDED_RECT { Rect = r, RadiusX = radius, RadiusY = radius };
            FillRoundedRectangle(_target, &rounded, brush);
        }
    }

    /// <summary>A premultiplied BGRA bitmap on the window's device, sized in physical pixels.</summary>
    public void* CreateBitmap(int width, int height)
    {
        var properties = new D2D_BITMAP_PROPERTIES
        {
            PixelFormat = new D2D_PIXEL_FORMAT { Format = DXGI_FORMAT_B8G8R8A8_UNORM, AlphaMode = D2D1_ALPHA_MODE_PREMULTIPLIED },
            DpiX = Dpi,
            DpiY = Dpi
        };
        void* bitmap;
        return D2D.CreateBitmap(_window, new D2D_SIZE_U { Width = (uint)width, Height = (uint)height }, null, 0,
            &properties, &bitmap) >= 0
            ? bitmap
            : null;
    }

    /// <summary>Fills a rounded rectangle with a bitmap whose top-left pixel sits at (x, y).</summary>
    public void FillBitmap(RectF rect, float radius, void* bitmap, float x, float y)
    {
        if (!Rendering || bitmap == null)
        {
            return;
        }

        var properties = new D2D_BITMAP_BRUSH_PROPERTIES { InterpolationMode = D2D1_BITMAP_INTERPOLATION_MODE_LINEAR };
        void* brush;
        if (CreateBitmapBrush(_target, bitmap, &properties, &brush) < 0)
        {
            return;
        }

        var transform = D2D_MATRIX.Translation(Snap(x), Snap(y));
        SetBrushTransform(brush, &transform);
        SetOpacity(brush, Opacity);
        FillWith(SnapRect(rect), radius, brush);
        Release(ref brush);
    }

    public void DrawBitmap(void* bitmap, RectF destination, float opacity = 1f)
    {
        if (!Rendering || bitmap == null)
        {
            return;
        }

        var rect = new D2D_RECT(Snap(destination.X), Snap(destination.Y), Snap(destination.Right), Snap(destination.Bottom));
        D2D.DrawBitmap(_target, bitmap, &rect, opacity * Opacity, null);
    }

    public void PushClip(RectF rect)
    {
        if (!Rendering)
        {
            return;
        }

        rect = SnapRect(rect);
        var r = new D2D_RECT(rect.X, rect.Y, rect.Right, rect.Bottom);
        PushAxisAlignedClip(_target, &r);
    }

    public void PopClip()
    {
        if (Rendering)
        {
            PopAxisAlignedClip(_target);
        }
    }

    // ---- Text ----

    /// <summary>Size of a text in DIPs (single line unless a wrapping width is given).</summary>
    public (float Width, float Height) Measure(string text, TextStyle style, float wrapWidth = 0f)
    {
        var entry = Layout(text, style, wrapWidth);
        return (entry.Width, entry.Height);
    }

    /// <summary>Draws text with its layout box's top-left at (x, y); returns the size.</summary>
    public (float Width, float Height) Text(string text, TextStyle style, float x, float y, uint argb,
        float wrapWidth = 0f, float clipWidth = 0f)
    {
        var entry = clipWidth > 0f ? Trimmed(text, style, clipWidth) : Layout(text, style, wrapWidth);
        if (Rendering && argb >> 24 != 0 && entry.Handle != null)
        {
            SetBrush(argb);
            DrawTextLayout(_target, new D2D_POINT(Snap(x), Snap(y)), entry.Handle, _brush, D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
        }

        return (entry.Width, entry.Height);
    }

    /// <summary>Single-line text vertically centred in a box, aligned left, centre or right.</summary>
    public void TextIn(string text, TextStyle style, RectF box, uint argb, float align = 0f)
    {
        var (width, height) = Measure(text, style);
        Text(text, style, box.X + (box.W - width) * align, box.Y + (box.H - height) * 0.5f, argb);
    }

    /// <summary>An icon-font glyph centred in a box.</summary>
    public void Icon(char glyph, RectF box, uint argb, TextStyle style = TextStyle.Icon)
    {
        var text = glyph.ToString();
        var (width, height) = Measure(text, style);
        Text(text, style, box.CenterX - width * 0.5f, box.CenterY - height * 0.5f, argb);
    }

    /// <summary>Caret position (x offset) before character <paramref name="index"/> of a single-line text.</summary>
    public float CaretX(string text, TextStyle style, int index)
    {
        var entry = Layout(text.Length == 0 ? " " : text, style, 0f);
        if (entry.Handle == null || text.Length == 0)
        {
            return 0f;
        }

        float x, y;
        DWRITE_HIT_TEST_METRICS metrics;
        HitTestTextPosition(entry.Handle, (uint)Math.Clamp(index, 0, text.Length), 0, &x, &y, &metrics);
        return x;
    }

    /// <summary>Character index nearest to an x offset in a single-line text.</summary>
    public int HitTest(string text, TextStyle style, float x)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var entry = Layout(text, style, 0f);
        if (entry.Handle == null)
        {
            return text.Length;
        }

        int trailing, inside;
        DWRITE_HIT_TEST_METRICS metrics;
        HitTestPoint(entry.Handle, x, 1f, &trailing, &inside, &metrics);
        return Math.Clamp((int)metrics.TextPosition + (trailing != 0 ? 1 : 0), 0, text.Length);
    }

    /// <summary>A single line cut to <paramref name="width"/> with an ellipsis when it does not fit.</summary>
    private LayoutEntry Trimmed(string text, TextStyle style, float width)
    {
        var full = Layout(text, style, 0f);
        if (full.Width <= width)
        {
            return full;
        }

        var key = new LayoutKey(text, style, -(int)MathF.Round(width * 4f));
        if (_layouts.TryGetValue(key, out var entry))
        {
            entry.LastFrame = _frame;
            return entry;
        }

        entry = new LayoutEntry { LastFrame = _frame };
        var format = Format(style, false);
        if (format != IntPtr.Zero)
        {
            if (!_signs.TryGetValue(style, out var sign))
            {
                void* created;
                sign = CreateEllipsisTrimmingSign(_write, (void*)format, &created) >= 0 ? (IntPtr)created : IntPtr.Zero;
                _signs[style] = sign;
            }

            void* layout;
            fixed (char* chars = text)
            {
                if (CreateTextLayout(_write, chars, (uint)text.Length, (void*)format, width, 100000f, &layout) >= 0)
                {
                    var trimming = new DWRITE_TRIMMING { Granularity = DWRITE_TRIMMING_GRANULARITY_CHARACTER };
                    SetTrimming(layout, &trimming, (void*)sign);
                    DWRITE_TEXT_METRICS metrics;
                    GetMetrics(layout, &metrics);
                    entry.Handle = layout;
                    entry.Width = MathF.Min(metrics.WidthIncludingTrailingWhitespace, width);
                    entry.Height = metrics.Height;
                }
            }
        }

        _layouts[key] = entry;
        return entry;
    }

    private LayoutEntry Layout(string text, TextStyle style, float wrapWidth)
    {
        var key = new LayoutKey(text, style, (int)MathF.Round(wrapWidth * 4f));
        if (_layouts.TryGetValue(key, out var entry))
        {
            entry.LastFrame = _frame;
            return entry;
        }

        entry = new LayoutEntry { LastFrame = _frame };
        var format = Format(style, wrapWidth > 0f);
        if (format != IntPtr.Zero && _write != null)
        {
            void* layout;
            fixed (char* chars = text)
            {
                if (CreateTextLayout(_write, chars, (uint)text.Length, (void*)format, wrapWidth > 0f ? wrapWidth : 100000f,
                        100000f, &layout) >= 0)
                {
                    DWRITE_TEXT_METRICS metrics;
                    GetMetrics(layout, &metrics);
                    entry.Handle = layout;
                    entry.Width = metrics.WidthIncludingTrailingWhitespace;
                    entry.Height = metrics.Height;
                }
            }
        }

        _layouts[key] = entry;
        return entry;
    }

    private IntPtr Format(TextStyle style, bool wrap)
    {
        if (_formats.TryGetValue((style, wrap), out var existing))
        {
            return existing;
        }

        var (family, weight, size) = style switch
        {
            TextStyle.Caption => (TextFamily, DWRITE_FONT_WEIGHT_NORMAL, 12f),
            TextStyle.Body => (TextFamily, DWRITE_FONT_WEIGHT_NORMAL, 14f),
            TextStyle.BodyStrong => (TextFamily, DWRITE_FONT_WEIGHT_SEMI_BOLD, 14f),
            TextStyle.Subtitle => (DisplayFamily, DWRITE_FONT_WEIGHT_SEMI_BOLD, 20f),
            TextStyle.Title => (DisplayFamily, DWRITE_FONT_WEIGHT_SEMI_BOLD, 28f),
            TextStyle.IconSmall => (_iconFamily, DWRITE_FONT_WEIGHT_NORMAL, 12f),
            TextStyle.IconLarge => (_iconFamily, DWRITE_FONT_WEIGHT_NORMAL, 20f),
            _ => (_iconFamily, DWRITE_FONT_WEIGHT_NORMAL, 16f)
        };
        void* format = null;
        fixed (char* familyName = family)
        fixed (char* locale = "zh-cn")
        {
            if (CreateTextFormat(_write, familyName, weight, size, locale, &format) >= 0)
            {
                SetWordWrapping(format, wrap ? DWRITE_WORD_WRAPPING_WRAP : DWRITE_WORD_WRAPPING_NO_WRAP);
            }
        }

        _formats[(style, wrap)] = (IntPtr)format;
        return (IntPtr)format;
    }

    private void TrimLayouts()
    {
        if (_layouts.Count < 600)
        {
            return;
        }

        foreach (var (key, entry) in _layouts.ToArray())
        {
            if (_frame - entry.LastFrame > 30)
            {
                var handle = entry.Handle;
                Release(ref handle);
                _layouts.Remove(key);
            }
        }
    }

    private void SetBrush(uint argb)
    {
        var color = D2D_COLOR.FromArgb(argb);
        color.A *= Opacity;
        SetColor(_brush, &color);
    }

    public void SetOffset(float x, float y)
    {
        if (!Rendering)
        {
            return;
        }

        var matrix = D2D_MATRIX.Translation(x, y);
        SetTransform(_target, &matrix);
    }

    /// <summary>Drops the window target and everything created on its device.</summary>
    public void DiscardDevice()
    {
        Release(ref _brush);
        Release(ref _window);
        _target = null;
    }

    public void Dispose()
    {
        foreach (var entry in _layouts.Values)
        {
            var handle = entry.Handle;
            Release(ref handle);
        }

        _layouts.Clear();
        foreach (var format in _formats.Values)
        {
            var handle = (void*)format;
            Release(ref handle);
        }

        _formats.Clear();
        foreach (var sign in _signs.Values)
        {
            var handle = (void*)sign;
            Release(ref handle);
        }

        _signs.Clear();
        DiscardDevice();
        Release(ref _ellipsis);
        Release(ref _roundStroke);
        Release(ref _write);
        Release(ref _factory);
    }

    private readonly record struct LayoutKey(string Text, TextStyle Style, int Wrap);

    private sealed class LayoutEntry
    {
        public void* Handle;
        public float Width;
        public float Height;
        public int LastFrame;
    }
}
