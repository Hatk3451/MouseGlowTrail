namespace MouseGlowTrail;

/// <summary>
/// The share of the plane one ribbon piece shades (see <see cref="Canvas.Ribbon"/>), in terms of its
/// parameter t along A to B: t &lt; 0 belongs to the previous piece unless the stroke starts here,
/// t &gt; 1 belongs to the next piece except inside the outer wedge of the joint, which the next slab
/// does not reach. Borders overlap by half a pixel. Shared by the CPU rasteriser and the GPU shader.
/// </summary>
internal struct RibbonLimits
{
    public const float Unbounded = 1e30f;
    private const float Overlap = 0.5f;

    /// <summary>Pixels with a smaller t belong to the previous piece.</summary>
    public float LowT;

    /// <summary>Pixels with a larger t belong to the next piece, unless (p - B)·Next &lt; 1 (outer wedge).</summary>
    public float HighT;

    public float NextX;
    public float NextY;

    /// <summary>False for a piece with round caps at both ends (nothing to clip).</summary>
    public bool Clipped;

    /// <summary>Verification switch: skip the per-row narrowing and rely on the per-pixel test alone.</summary>
    public static bool NarrowRows { get; set; } = true;

    /// <param name="ex">This piece's chord, B - A.</param>
    /// <param name="nextX">The next piece's chord from B (unused when <paramref name="capEnd"/>).</param>
    public static RibbonLimits Compute(float ex, float ey, bool capStart, bool capEnd, float nextX, float nextY)
    {
        var limits = new RibbonLimits { LowT = -Unbounded, HighT = Unbounded };
        var length = MathF.Sqrt(ex * ex + ey * ey);
        if (length < 1e-3f)
        {
            return limits; // a dot is round all over
        }

        if (!capStart)
        {
            limits.LowT = -Overlap / length;
        }

        var nextLength = MathF.Sqrt(nextX * nextX + nextY * nextY);
        if (!capEnd && nextLength >= 1e-3f)
        {
            limits.HighT = 1f + Overlap / length;
            // (p - B)·next / |next| is the distance past the next piece's start line; this piece keeps
            // what lies less than half a pixel beyond it.
            var scale = 1f / (nextLength * Overlap);
            limits.NextX = nextX * scale;
            limits.NextY = nextY * scale;
        }

        limits.Clipped = NarrowRows && (limits.LowT > -Unbounded || limits.HighT < Unbounded);
        return limits;
    }

    /// <param name="tRaw">Unclamped t of the pixel centre.</param>
    /// <param name="fromBx">Pixel centre minus B.</param>
    public readonly bool Owns(float tRaw, float fromBx, float fromBy) =>
        tRaw >= LowT && (tRaw <= HighT || fromBx * NextX + fromBy * NextY < 1f);

    /// <summary>
    /// Narrows a row's column range to what this piece can own, padded by a pixel; <see cref="Owns"/>
    /// still decides each pixel. Leaves start &gt; end when nothing is left.
    /// </summary>
    public readonly void NarrowRow(float ax, float ex, float ey, float inverseLengthSquared, float py, ref int start,
        ref int end)
    {
        float lo = start, hi = end;
        // t(x) = slope * x + offset for pixel column x (centre at x + 0.5).
        var slope = ex * inverseLengthSquared;
        var offset = ((0.5f - ax) * ex + py * ey) * inverseLengthSquared;
        if (LowT > -Unbounded && !Keep(slope, offset - LowT, ref lo, ref hi))
        {
            end = start - 1;
            return;
        }

        if (HighT < Unbounded)
        {
            float slabLo = lo, slabHi = hi, wedgeLo = lo, wedgeHi = hi;
            var slab = Keep(-slope, HighT - offset, ref slabLo, ref slabHi);
            var wedge = Keep(slope, offset - HighT, ref wedgeLo, ref wedgeHi) &&
                        Keep(-NextX, 1f - (0.5f - ax - ex) * NextX - (py - ey) * NextY, ref wedgeLo, ref wedgeHi);
            if (!slab && !wedge)
            {
                end = start - 1;
                return;
            }

            lo = MathF.Min(slab ? slabLo : float.MaxValue, wedge ? wedgeLo : float.MaxValue);
            hi = MathF.Max(slab ? slabHi : float.MinValue, wedge ? wedgeHi : float.MinValue);
        }

        start = Math.Max(start, (int)MathF.Floor(lo) - 1);
        end = Math.Min(end, (int)MathF.Ceiling(hi) + 1);
    }

    /// <summary>Keeps the x in [lo, hi] where c * x + d &gt;= 0; false when none remain.</summary>
    private static bool Keep(float c, float d, ref float lo, ref float hi)
    {
        if (c > 1e-9f)
        {
            lo = MathF.Max(lo, -d / c);
        }
        else if (c < -1e-9f)
        {
            hi = MathF.Min(hi, -d / c);
        }
        else if (d < 0f)
        {
            return false;
        }

        return lo <= hi;
    }
}
