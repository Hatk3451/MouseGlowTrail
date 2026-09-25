using System.Runtime.InteropServices;

namespace MouseGlowTrail;

// Direct2D / DirectWrite interop in the style of Direct3D.cs: interface pointers are void*, methods
// are called through their vtable slots (from the Windows SDK 10.0.26100 d2d1.h and dwrite.h).

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_COLOR
{
    public float R;
    public float G;
    public float B;
    public float A;

    public D2D_COLOR(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>From 0xAARRGGBB (straight alpha).</summary>
    public static D2D_COLOR FromArgb(uint argb) => new(((argb >> 16) & 255) / 255f, ((argb >> 8) & 255) / 255f,
        (argb & 255) / 255f, (argb >> 24) / 255f);
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_POINT
{
    public float X;
    public float Y;

    public D2D_POINT(float x, float y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_RECT
{
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

    public D2D_RECT(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_ROUNDED_RECT
{
    public D2D_RECT Rect;
    public float RadiusX;
    public float RadiusY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_ELLIPSE
{
    public D2D_POINT Center;
    public float RadiusX;
    public float RadiusY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_MATRIX
{
    public float M11;
    public float M12;
    public float M21;
    public float M22;
    public float Dx;
    public float Dy;

    public static D2D_MATRIX Translation(float x, float y) => new() { M11 = 1, M22 = 1, Dx = x, Dy = y };

    public static readonly D2D_MATRIX Identity = new() { M11 = 1, M22 = 1 };
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_SIZE_U
{
    public uint Width;
    public uint Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_RECT_U
{
    public uint Left;
    public uint Top;
    public uint Right;
    public uint Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_PIXEL_FORMAT
{
    public uint Format;
    public uint AlphaMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_RENDER_TARGET_PROPERTIES
{
    public uint Type;
    public D2D_PIXEL_FORMAT PixelFormat;
    public float DpiX;
    public float DpiY;
    public uint Usage;
    public uint MinLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_HWND_RENDER_TARGET_PROPERTIES
{
    public IntPtr Hwnd;
    public D2D_SIZE_U PixelSize;
    public uint PresentOptions;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_BITMAP_PROPERTIES
{
    public D2D_PIXEL_FORMAT PixelFormat;
    public float DpiX;
    public float DpiY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_GRADIENT_STOP
{
    public float Position;
    public D2D_COLOR Color;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_LINEAR_GRADIENT_BRUSH_PROPERTIES
{
    public D2D_POINT Start;
    public D2D_POINT End;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_RADIAL_GRADIENT_BRUSH_PROPERTIES
{
    public D2D_POINT Center;
    public D2D_POINT OriginOffset;
    public float RadiusX;
    public float RadiusY;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_BITMAP_BRUSH_PROPERTIES
{
    public uint ExtendModeX;
    public uint ExtendModeY;
    public uint InterpolationMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_BRUSH_PROPERTIES
{
    public float Opacity;
    public D2D_MATRIX Transform;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_STROKE_STYLE_PROPERTIES
{
    public uint StartCap;
    public uint EndCap;
    public uint DashCap;
    public uint LineJoin;
    public float MiterLimit;
    public uint DashStyle;
    public float DashOffset;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_BEZIER_SEGMENT
{
    public D2D_POINT Point1;
    public D2D_POINT Point2;
    public D2D_POINT Point3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D2D_ARC_SEGMENT
{
    public D2D_POINT Point;
    public float Width;
    public float Height;
    public float RotationAngle;
    public uint SweepDirection;
    public uint ArcSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWRITE_TEXT_METRICS
{
    public float Left;
    public float Top;
    public float Width;
    public float WidthIncludingTrailingWhitespace;
    public float Height;
    public float LayoutWidth;
    public float LayoutHeight;
    public uint MaxBidiReorderingDepth;
    public uint LineCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWRITE_TRIMMING
{
    public uint Granularity;
    public uint Delimiter;
    public uint DelimiterCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DWRITE_HIT_TEST_METRICS
{
    public uint TextPosition;
    public uint Length;
    public float Left;
    public float Top;
    public float Width;
    public float Height;
    public uint BidiLevel;
    public int IsText;
    public int IsTrimmed;
}

internal static unsafe class D2D
{
    public const uint D2D1_FACTORY_TYPE_SINGLE_THREADED = 0;
    public const uint D2D1_RENDER_TARGET_TYPE_DEFAULT = 0;
    public const uint D2D1_ALPHA_MODE_PREMULTIPLIED = 1;
    public const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const uint D2D1_PRESENT_OPTIONS_NONE = 0;
    public const uint D2D1_ANTIALIAS_MODE_PER_PRIMITIVE = 0;
    public const uint D2D1_ANTIALIAS_MODE_ALIASED = 1;
    public const uint D2D1_TEXT_ANTIALIAS_MODE_DEFAULT = 0;
    public const uint D2D1_TEXT_ANTIALIAS_MODE_CLEARTYPE = 1;
    public const uint D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE = 2;
    public const uint D2D1_DRAW_TEXT_OPTIONS_CLIP = 2;
    public const uint D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT = 4;
    public const uint D2D1_BITMAP_INTERPOLATION_MODE_LINEAR = 1;
    public const uint D2D1_EXTEND_MODE_CLAMP = 0;
    public const uint D2D1_GAMMA_2_2 = 0;
    public const uint D2D1_CAP_STYLE_ROUND = 2;
    public const uint D2D1_LINE_JOIN_ROUND = 2;
    public const uint D2D1_FIGURE_BEGIN_FILLED = 0;
    public const uint D2D1_FIGURE_BEGIN_HOLLOW = 1;
    public const uint D2D1_FIGURE_END_OPEN = 0;
    public const uint D2D1_FIGURE_END_CLOSED = 1;
    public const uint D2D1_SWEEP_DIRECTION_CLOCKWISE = 1;
    public const uint D2D1_ARC_SIZE_SMALL = 0;
    public const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);

    public const uint DWRITE_FACTORY_TYPE_SHARED = 0;
    public const uint DWRITE_FONT_WEIGHT_NORMAL = 400;
    public const uint DWRITE_FONT_WEIGHT_SEMI_BOLD = 600;
    public const uint DWRITE_FONT_STYLE_NORMAL = 0;
    public const uint DWRITE_FONT_STRETCH_NORMAL = 5;
    public const uint DWRITE_TEXT_ALIGNMENT_LEADING = 0;
    public const uint DWRITE_TEXT_ALIGNMENT_TRAILING = 1;
    public const uint DWRITE_TEXT_ALIGNMENT_CENTER = 2;
    public const uint DWRITE_PARAGRAPH_ALIGNMENT_NEAR = 0;
    public const uint DWRITE_PARAGRAPH_ALIGNMENT_CENTER = 2;
    public const uint DWRITE_WORD_WRAPPING_WRAP = 0;
    public const uint DWRITE_WORD_WRAPPING_NO_WRAP = 1;
    public const uint DWRITE_TRIMMING_GRANULARITY_CHARACTER = 1;

    public static readonly Guid IID_ID2D1Factory = new(0x06152247, 0x6f50, 0x465a, 0x92, 0x45, 0x11, 0x8b, 0xfd, 0x3b, 0x60, 0x07);
    public static readonly Guid IID_IDWriteFactory = new(0xb859ee5a, 0xd838, 0x4b5b, 0xa2, 0xe8, 0x1a, 0xdc, 0x7d, 0x93, 0xdb, 0x48);

    [DllImport("d2d1.dll", ExactSpelling = true)]
    public static extern int D2D1CreateFactory(uint type, Guid* iid, void* options, void** factory);

    [DllImport("dwrite.dll", ExactSpelling = true)]
    public static extern int DWriteCreateFactory(uint type, Guid* iid, void** factory);

    private static void* Slot(void* self, int index) => (*(void***)self)[index];

    public static void Release(ref void* self) => D3D.Release(ref self);

    // ID2D1Factory (IUnknown 0-2)
    public static int CreatePathGeometry(void* factory, void** geometry) =>
        ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(factory, 10))(factory, geometry);

    public static int CreateStrokeStyle(void* factory, D2D_STROKE_STYLE_PROPERTIES* properties, void** style) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_STROKE_STYLE_PROPERTIES*, float*, uint, void**, int>)Slot(factory, 11))(
            factory, properties, null, 0, style);

    public static int CreateHwndRenderTarget(void* factory, D2D_RENDER_TARGET_PROPERTIES* properties,
        D2D_HWND_RENDER_TARGET_PROPERTIES* hwndProperties, void** target) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_RENDER_TARGET_PROPERTIES*, D2D_HWND_RENDER_TARGET_PROPERTIES*, void**, int>)
            Slot(factory, 14))(factory, properties, hwndProperties, target);

    // ID2D1RenderTarget (ID2D1Resource: 3; own methods from 4)
    public static int CreateBitmap(void* target, D2D_SIZE_U size, void* data, uint pitch,
        D2D_BITMAP_PROPERTIES* properties, void** bitmap) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_SIZE_U, void*, uint, D2D_BITMAP_PROPERTIES*, void**, int>)Slot(target, 4))(
            target, size, data, pitch, properties, bitmap);

    public static int CreateBitmapBrush(void* target, void* bitmap, D2D_BITMAP_BRUSH_PROPERTIES* properties, void** brush) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, D2D_BITMAP_BRUSH_PROPERTIES*, void*, void**, int>)Slot(target, 7))(
            target, bitmap, properties, null, brush);

    public static int CreateSolidColorBrush(void* target, D2D_COLOR* color, void** brush) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_COLOR*, void*, void**, int>)Slot(target, 8))(target, color, null, brush);

    public static int CreateGradientStopCollection(void* target, D2D_GRADIENT_STOP* stops, uint count, void** collection) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_GRADIENT_STOP*, uint, uint, uint, void**, int>)Slot(target, 9))(
            target, stops, count, D2D1_GAMMA_2_2, D2D1_EXTEND_MODE_CLAMP, collection);

    public static int CreateLinearGradientBrush(void* target, D2D_LINEAR_GRADIENT_BRUSH_PROPERTIES* properties,
        void* stops, void** brush) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_LINEAR_GRADIENT_BRUSH_PROPERTIES*, void*, void*, void**, int>)Slot(target, 10))(
            target, properties, null, stops, brush);

    public static int CreateRadialGradientBrush(void* target, D2D_RADIAL_GRADIENT_BRUSH_PROPERTIES* properties,
        void* stops, void** brush) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_RADIAL_GRADIENT_BRUSH_PROPERTIES*, void*, void*, void**, int>)Slot(target, 11))(
            target, properties, null, stops, brush);

    public static int CreateCompatibleRenderTarget(void* target, float* sizeDips, D2D_SIZE_U* sizePixels, void** compatible) =>
        ((delegate* unmanaged[Stdcall]<void*, float*, D2D_SIZE_U*, void*, uint, void**, int>)Slot(target, 12))(
            target, sizeDips, sizePixels, null, 0, compatible);

    public static void DrawLine(void* target, D2D_POINT a, D2D_POINT b, void* brush, float width, void* style) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_POINT, D2D_POINT, void*, float, void*, void>)Slot(target, 15))(
            target, a, b, brush, width, style);

    public static void FillRectangle(void* target, D2D_RECT* rect, void* brush) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_RECT*, void*, void>)Slot(target, 17))(target, rect, brush);

    public static void DrawRoundedRectangle(void* target, D2D_ROUNDED_RECT* rect, void* brush, float width) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_ROUNDED_RECT*, void*, float, void*, void>)Slot(target, 18))(
            target, rect, brush, width, null);

    public static void FillRoundedRectangle(void* target, D2D_ROUNDED_RECT* rect, void* brush) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_ROUNDED_RECT*, void*, void>)Slot(target, 19))(target, rect, brush);

    public static void DrawEllipse(void* target, D2D_ELLIPSE* ellipse, void* brush, float width) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_ELLIPSE*, void*, float, void*, void>)Slot(target, 20))(
            target, ellipse, brush, width, null);

    public static void FillEllipse(void* target, D2D_ELLIPSE* ellipse, void* brush) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_ELLIPSE*, void*, void>)Slot(target, 21))(target, ellipse, brush);

    public static void DrawGeometry(void* target, void* geometry, void* brush, float width, void* style) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, float, void*, void>)Slot(target, 22))(
            target, geometry, brush, width, style);

    public static void FillGeometry(void* target, void* geometry, void* brush) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, void>)Slot(target, 23))(target, geometry, brush, null);

    public static void DrawBitmap(void* target, void* bitmap, D2D_RECT* destination, float opacity, D2D_RECT* source) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, D2D_RECT*, float, uint, D2D_RECT*, void>)Slot(target, 26))(
            target, bitmap, destination, opacity, D2D1_BITMAP_INTERPOLATION_MODE_LINEAR, source);

    public static void DrawTextLayout(void* target, D2D_POINT origin, void* layout, void* brush, uint options) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_POINT, void*, void*, uint, void>)Slot(target, 28))(
            target, origin, layout, brush, options);

    public static void SetTransform(void* target, D2D_MATRIX* transform) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_MATRIX*, void>)Slot(target, 30))(target, transform);

    public static void SetAntialiasMode(void* target, uint mode) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void>)Slot(target, 32))(target, mode);

    public static void SetTextAntialiasMode(void* target, uint mode) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void>)Slot(target, 34))(target, mode);

    public static void PushAxisAlignedClip(void* target, D2D_RECT* rect) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_RECT*, uint, void>)Slot(target, 45))(
            target, rect, D2D1_ANTIALIAS_MODE_ALIASED);

    public static void PopAxisAlignedClip(void* target) =>
        ((delegate* unmanaged[Stdcall]<void*, void>)Slot(target, 46))(target);

    public static void Clear(void* target, D2D_COLOR* color) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_COLOR*, void>)Slot(target, 47))(target, color);

    public static void BeginDraw(void* target) =>
        ((delegate* unmanaged[Stdcall]<void*, void>)Slot(target, 48))(target);

    public static int EndDraw(void* target) =>
        ((delegate* unmanaged[Stdcall]<void*, ulong*, ulong*, int>)Slot(target, 49))(target, null, null);

    public static void SetDpi(void* target, float dpiX, float dpiY) =>
        ((delegate* unmanaged[Stdcall]<void*, float, float, void>)Slot(target, 51))(target, dpiX, dpiY);

    // ID2D1BitmapRenderTarget (after ID2D1RenderTarget's 57 slots)
    public static int GetBitmap(void* bitmapTarget, void** bitmap) =>
        ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(bitmapTarget, 57))(bitmapTarget, bitmap);

    // ID2D1HwndRenderTarget
    public static int Resize(void* target, D2D_SIZE_U* size) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_SIZE_U*, int>)Slot(target, 58))(target, size);

    // ID2D1Brush
    public static void SetOpacity(void* brush, float opacity) =>
        ((delegate* unmanaged[Stdcall]<void*, float, void>)Slot(brush, 4))(brush, opacity);

    public static void SetBrushTransform(void* brush, D2D_MATRIX* transform) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_MATRIX*, void>)Slot(brush, 5))(brush, transform);

    public static void SetColor(void* brush, D2D_COLOR* color) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_COLOR*, void>)Slot(brush, 8))(brush, color);

    // ID2D1Bitmap
    public static int CopyFromMemory(void* bitmap, D2D_RECT_U* destination, void* data, uint pitch) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_RECT_U*, void*, uint, int>)Slot(bitmap, 10))(bitmap, destination, data, pitch);

    // ID2D1PathGeometry / ID2D1GeometrySink
    public static int Open(void* path, void** sink) =>
        ((delegate* unmanaged[Stdcall]<void*, void**, int>)Slot(path, 17))(path, sink);

    public static void BeginFigure(void* sink, D2D_POINT start, uint begin) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_POINT, uint, void>)Slot(sink, 5))(sink, start, begin);

    public static void EndFigure(void* sink, uint end) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void>)Slot(sink, 8))(sink, end);

    public static int CloseSink(void* sink) =>
        ((delegate* unmanaged[Stdcall]<void*, int>)Slot(sink, 9))(sink);

    public static void AddLine(void* sink, D2D_POINT point) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_POINT, void>)Slot(sink, 10))(sink, point);

    public static void AddBezier(void* sink, D2D_BEZIER_SEGMENT* bezier) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_BEZIER_SEGMENT*, void>)Slot(sink, 11))(sink, bezier);

    public static void AddArc(void* sink, D2D_ARC_SEGMENT* arc) =>
        ((delegate* unmanaged[Stdcall]<void*, D2D_ARC_SEGMENT*, void>)Slot(sink, 14))(sink, arc);

    // IDWriteFactory
    public static int CreateTextFormat(void* factory, char* family, uint weight, float size, char* locale, void** format) =>
        ((delegate* unmanaged[Stdcall]<void*, char*, void*, uint, uint, uint, float, char*, void**, int>)Slot(factory, 15))(
            factory, family, null, weight, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, locale, format);

    public static int CreateTextLayout(void* factory, char* text, uint length, void* format, float maxWidth,
        float maxHeight, void** layout) =>
        ((delegate* unmanaged[Stdcall]<void*, char*, uint, void*, float, float, void**, int>)Slot(factory, 18))(
            factory, text, length, format, maxWidth, maxHeight, layout);

    public static int CreateEllipsisTrimmingSign(void* factory, void* format, void** sign) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)Slot(factory, 20))(factory, format, sign);

    // IDWriteTextFormat
    public static int SetTextAlignment(void* format, uint alignment) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, int>)Slot(format, 3))(format, alignment);

    public static int SetParagraphAlignment(void* format, uint alignment) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, int>)Slot(format, 4))(format, alignment);

    public static int SetWordWrapping(void* format, uint wrapping) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, int>)Slot(format, 5))(format, wrapping);

    public static int SetTrimming(void* format, DWRITE_TRIMMING* trimming, void* sign) =>
        ((delegate* unmanaged[Stdcall]<void*, DWRITE_TRIMMING*, void*, int>)Slot(format, 9))(format, trimming, sign);

    // IDWriteTextLayout (IDWriteTextFormat's 25 slots, then its own from 28)
    public static int GetMetrics(void* layout, DWRITE_TEXT_METRICS* metrics) =>
        ((delegate* unmanaged[Stdcall]<void*, DWRITE_TEXT_METRICS*, int>)Slot(layout, 60))(layout, metrics);

    public static int HitTestPoint(void* layout, float x, float y, int* isTrailing, int* isInside,
        DWRITE_HIT_TEST_METRICS* metrics) =>
        ((delegate* unmanaged[Stdcall]<void*, float, float, int*, int*, DWRITE_HIT_TEST_METRICS*, int>)Slot(layout, 64))(
            layout, x, y, isTrailing, isInside, metrics);

    public static int HitTestTextPosition(void* layout, uint position, int trailing, float* x, float* y,
        DWRITE_HIT_TEST_METRICS* metrics) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, int, float*, float*, DWRITE_HIT_TEST_METRICS*, int>)Slot(layout, 65))(
            layout, position, trailing, x, y, metrics);
}
