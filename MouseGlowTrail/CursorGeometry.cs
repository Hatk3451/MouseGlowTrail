using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

/// <summary>
/// Where the middle of a pointer image lies relative to its hotspot, found once per cursor handle
/// from the bounding box of its visible pixels.
/// </summary>
internal static unsafe class CursorGeometry
{
    private const int CacheLimit = 64;

    /// <summary>Used when a cursor cannot be read: roughly the middle of a standard arrow.</summary>
    private static readonly (float X, float Y) s_fallback = (6f, 10f);

    private static readonly Dictionary<IntPtr, (float X, float Y)> s_centers = [];

    /// <summary>
    /// Offset from the hotspot to the middle of the visible image, scaled to a standard 32 px pointer
    /// (the unit the other origin offsets use).
    /// </summary>
    public static (float X, float Y) CenterOffset(IntPtr cursor)
    {
        if (cursor == 0)
        {
            return s_fallback;
        }

        // Used by the render thread and the control panel's preview.
        lock (s_centers)
        {
            if (s_centers.TryGetValue(cursor, out var cached))
            {
                return cached;
            }

            var center = Measure(cursor) ?? s_fallback;
            if (s_centers.Count >= CacheLimit)
            {
                s_centers.Clear();
            }

            s_centers[cursor] = center;
            return center;
        }
    }

    /// <summary>Forget measured cursors (the pointer scheme or size changed).</summary>
    public static void Reset()
    {
        lock (s_centers)
        {
            s_centers.Clear();
        }
    }

    private static (float X, float Y)? Measure(IntPtr cursor)
    {
        ICONINFO info;
        if (GetIconInfo(cursor, &info) == 0)
        {
            return null;
        }

        try
        {
            BITMAP mask;
            if (info.Mask == 0 || GetObjectW(info.Mask, sizeof(BITMAP), &mask) == 0 || mask.Width <= 0)
            {
                return null;
            }

            // A monochrome cursor stacks its AND and XOR masks in one bitmap of double height.
            var width = mask.Width;
            var height = info.Color != 0 ? mask.Height : mask.Height / 2;
            if (height <= 0 || width > 512 || height > 512)
            {
                return null;
            }

            var andMask = Read(info.Mask, width, mask.Height);
            var colors = info.Color != 0 ? Read(info.Color, width, height) : null;
            if (andMask is null)
            {
                return null;
            }

            var hasAlpha = colors is not null && colors.Any(pixel => pixel >> 24 != 0);
            int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    bool visible;
                    if (hasAlpha)
                    {
                        visible = colors![index] >> 24 != 0;
                    }
                    else if (colors is not null)
                    {
                        visible = (andMask[index] & 0xFFFFFF) == 0 || (colors[index] & 0xFFFFFF) != 0;
                    }
                    else
                    {
                        // AND mask clear (drawn) or XOR mask set (inverted) both show something.
                        visible = (andMask[index] & 0xFFFFFF) == 0 || (andMask[index + width * height] & 0xFFFFFF) != 0;
                    }

                    if (visible)
                    {
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            if (right < 0)
            {
                return null;
            }

            var unit = 32f / width;
            return (((left + right + 1) * 0.5f - info.HotspotX) * unit, ((top + bottom + 1) * 0.5f - info.HotspotY) * unit);
        }
        finally
        {
            if (info.Mask != 0)
            {
                DeleteObject(info.Mask);
            }

            if (info.Color != 0)
            {
                DeleteObject(info.Color);
            }
        }
    }

    private static uint[]? Read(IntPtr bitmap, int width, int height)
    {
        var pixels = new uint[width * height];
        var header = new BITMAPINFOHEADER
        {
            Size = (uint)sizeof(BITMAPINFOHEADER),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32
        };
        var dc = GetDC(0);
        try
        {
            // Room for the two palette entries GetDIBits may write after the header for a 1 bpp source.
            var buffer = stackalloc byte[sizeof(BITMAPINFOHEADER) + 16];
            *(BITMAPINFOHEADER*)buffer = header;
            fixed (uint* bits = pixels)
            {
                return GetDIBits(dc, bitmap, 0, (uint)height, bits, (BITMAPINFOHEADER*)buffer, DIB_RGB_COLORS) == height
                    ? pixels
                    : null;
            }
        }
        finally
        {
            ReleaseDC(0, dc);
        }
    }
}
