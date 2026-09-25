using static MouseGlowTrail.Native;

namespace MouseGlowTrail.Ui;

/// <summary>The user's own arrow pointer as a Direct2D bitmap, for the previews.</summary>
internal sealed unsafe class PointerImage : IDisposable
{
    public static readonly IntPtr Arrow = LoadCursorW(0, IDC_ARROW);

    private static float s_cursorBase = 1f;
    private static long s_cursorBaseRead;

    private void* _bitmap;
    private int _generation = -1;
    private float _dpi;
    private float _base;

    /// <summary>Edge length in DIPs.</summary>
    public float Size { get; private set; }

    public float HotspotX { get; private set; }
    public float HotspotY { get; private set; }

    /// <summary>The pointer size setting (1 = standard), re-read at most every two seconds.</summary>
    public static float CursorBaseScale()
    {
        var now = Environment.TickCount64;
        if (s_cursorBaseRead != 0 && now - s_cursorBaseRead < 2000)
        {
            return s_cursorBase;
        }

        s_cursorBaseRead = now;
        s_cursorBase = 1f;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Cursors");
            if (key?.GetValue("CursorBaseSize") is int size && size is >= 32 and <= 256)
            {
                s_cursorBase = size / 32f;
            }
        }
        catch
        {
            // Standard size.
        }

        return s_cursorBase;
    }

    /// <summary>Draws the pointer with its hotspot at (x, y), optionally scaled down.</summary>
    public void Draw(Gfx gfx, float x, float y, float scale = 1f)
    {
        var cursorBase = CursorBaseScale();
        if (_bitmap == null || _generation != gfx.Generation || _dpi != gfx.Dpi || _base != cursorBase)
        {
            D2D.Release(ref _bitmap);
            _generation = gfx.Generation;
            _dpi = gfx.Dpi;
            _base = cursorBase;
            _bitmap = Render(gfx, cursorBase);
        }

        if (_bitmap != null)
        {
            gfx.DrawBitmap(_bitmap, new RectF(x - HotspotX * scale, y - HotspotY * scale, Size * scale, Size * scale));
        }
    }

    private void* Render(Gfx gfx, float cursorBase)
    {
        var size = (int)MathF.Round(32f * gfx.Scale * cursorBase);
        ICONINFO info;
        if (Arrow == 0 || size <= 0 || GetIconInfo(Arrow, &info) == 0)
        {
            return null;
        }

        BITMAP mask = default;
        if (info.Mask != 0)
        {
            GetObjectW(info.Mask, sizeof(BITMAP), &mask);
            DeleteObject(info.Mask);
        }

        if (info.Color != 0)
        {
            DeleteObject(info.Color);
        }

        var nativeWidth = Math.Max(1, mask.Width);
        HotspotX = info.HotspotX * (float)size / nativeWidth / gfx.Scale;
        HotspotY = info.HotspotY * (float)size / nativeWidth / gfx.Scale;
        Size = size / gfx.Scale;

        // Drawn on black and on white: the difference recovers the alpha channel for any cursor type.
        var onBlack = DrawCursor(size, 0x00000000u);
        var onWhite = DrawCursor(size, 0x00FFFFFFu);
        if (onBlack is null || onWhite is null)
        {
            return null;
        }

        var pixels = new uint[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            var alpha = 255 - Math.Clamp((int)((onWhite[i] >> 8) & 255) - (int)((onBlack[i] >> 8) & 255), 0, 255);
            pixels[i] = ((uint)alpha << 24) | (onBlack[i] & 0xFFFFFF);
        }

        var bitmap = gfx.CreateBitmap(size, size);
        if (bitmap != null)
        {
            var rect = new D2D_RECT_U { Right = (uint)size, Bottom = (uint)size };
            fixed (uint* data = pixels)
            {
                D2D.CopyFromMemory(bitmap, &rect, data, (uint)(size * 4));
            }
        }

        return bitmap;
    }

    private static uint[]? DrawCursor(int size, uint background)
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
        var dib = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (dib == 0 || bits == null)
        {
            return null;
        }

        var dc = CreateCompatibleDC(0);
        var previous = SelectObject(dc, dib);
        new Span<uint>(bits, size * size).Fill(background);
        DrawIconEx(dc, 0, 0, Arrow, size, size, 0, 0, DI_NORMAL);
        var pixels = new Span<uint>(bits, size * size).ToArray();
        SelectObject(dc, previous);
        DeleteDC(dc);
        DeleteObject(dib);
        return pixels;
    }

    public void ReleaseDeviceResources() => D2D.Release(ref _bitmap);

    public void Dispose() => ReleaseDeviceResources();
}
