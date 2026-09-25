using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace MouseGlowTrail.Bench;

// Frozen copy of the v2.0 rasteriser (ribbons shaded with full round caps at every joint), kept as the
// reference that the overdraw-free ribbon is compared against. Not used by the product.

/// <summary>
/// Software rasteriser over a premultiplied BGRA surface. Every primitive is an analytic distance
/// field, composited with "highest alpha wins": neighbouring pieces of one stroke therefore merge
/// into a single seamless ribbon instead of stacking up into darker beads at every joint.
/// Only 32 px tiles that were touched are cleared again, so the cost follows the drawn area.
/// </summary>
internal sealed unsafe class ReferenceCanvas : IPrimitiveSink
{
    private const int TileShift = 5;
    private const int TileSize = 1 << TileShift;

    // 4x4 ordered dither added before truncating alpha; hides banding in the long, faint glow falloff.
    private static readonly float* Dither = CreateDither();

    private uint* _pixels;
    private int _width;
    private int _height;
    private int _clipWidth;
    private int _clipHeight;
    private int _tilesX;
    private int _dirtyCount;
    private byte[] _tileDirty = [];
    private int[] _dirtyTiles = [];

    public void Attach(uint* pixels, int width, int height)
    {
        _pixels = pixels;
        _width = width;
        _height = height;
        _clipWidth = width;
        _clipHeight = height;
        _tilesX = (width + TileSize - 1) >> TileShift;
        var tiles = _tilesX * ((height + TileSize - 1) >> TileShift);
        _tileDirty = new byte[tiles];
        _dirtyTiles = new int[tiles];
        _dirtyCount = 0;
    }

    public void Detach()
    {
        _pixels = null;
        _width = _height = _clipWidth = _clipHeight = 0;
        _dirtyCount = 0;
    }

    /// <summary>Erases the previous frame and restricts drawing to the top-left clip rectangle.</summary>
    public void BeginFrame(int clipWidth, int clipHeight)
    {
        ClearDirty();
        _clipWidth = Math.Min(clipWidth, _width);
        _clipHeight = Math.Min(clipHeight, _height);
    }

    public void ClearDirty()
    {
        for (var i = 0; i < _dirtyCount; i++)
        {
            var tile = _dirtyTiles[i];
            _tileDirty[tile] = 0;
            var x0 = (tile % _tilesX) << TileShift;
            var y0 = (tile / _tilesX) << TileShift;
            var width = Math.Min(TileSize, _width - x0);
            var y1 = Math.Min(y0 + TileSize, _height);
            for (var y = y0; y < y1; y++)
            {
                new Span<uint>(_pixels + (long)y * _width + x0, width).Clear();
            }
        }

        _dirtyCount = 0;
    }

    /// <summary>
    /// Draws a round-capped segment whose cross-section follows <paramref name="lut"/>, scaled so that
    /// normalised distance 1 equals <paramref name="radius"/> pixels. a == b draws a radial dot.
    /// </summary>
    public void Capsule(float ax, float ay, float bx, float by, float radius, float fade, uint rgb, ProfileLut lut)
    {
        if (_pixels == null || fade <= 0.002f || radius <= 0.01f)
        {
            return;
        }

        var reach = radius * lut.Extent;
        if (!Clip(MathF.Min(ax, bx) - reach, MathF.Min(ay, by) - reach, MathF.Max(ax, bx) + reach,
                MathF.Max(ay, by) + reach, out var x0, out var y0, out var x1, out var y1))
        {
            return;
        }

        var ex = bx - ax;
        var ey = by - ay;
        var lengthSquared = ex * ex + ey * ey;
        var inverseLengthSquared = lengthSquared > 1e-6f ? 1f / lengthSquared : 0f;
        var length = MathF.Sqrt(lengthSquared);
        var reachSquared = reach * reach;
        var indexScale = lut.IndexScale / (radius * radius);
        var alphaScale = fade * 255f;
        float red = (rgb >> 16) & 255, green = (rgb >> 8) & 255, blue = rgb & 255;
        float whiteRed = 255f - red, whiteGreen = 255f - green, whiteBlue = 255f - blue;

        // For a slanted segment only a band of each row can be within reach of the centre line.
        var useBand = lengthSquared > 0.25f && MathF.Abs(ey) > 1e-3f;
        var inverseEy = useBand ? 1f / ey : 0f;
        var bandHalfWidth = reach * length;
        var tStep = ex * inverseLengthSquared;
        var alphaTable = lut.Alpha;
        var whiteTable = lut.White;

        for (var y = y0; y <= y1; y++)
        {
            var py = y + 0.5f - ay;
            var rowStart = x0;
            var rowEnd = x1;
            if (useBand)
            {
                var cross = ex * py;
                var first = (cross - bandHalfWidth) * inverseEy + ax - 0.5f;
                var second = (cross + bandHalfWidth) * inverseEy + ax - 0.5f;
                if (first > second)
                {
                    (first, second) = (second, first);
                }

                rowStart = Math.Max(rowStart, (int)MathF.Floor(Math.Clamp(first, x0 - 1, x1 + 1)));
                rowEnd = Math.Min(rowEnd, (int)MathF.Ceiling(Math.Clamp(second, x0 - 1, x1 + 1)));
                if (rowStart > rowEnd)
                {
                    continue;
                }
            }

            var row = _pixels + (long)y * _width;
            var dither = Dither + ((y & 3) << 2);
            var px = rowStart + 0.5f - ax;
            var tRaw = (px * ex + py * ey) * inverseLengthSquared;
            for (var x = rowStart; x <= rowEnd; x++, px += 1f, tRaw += tStep)
            {
                var t = tRaw < 0f ? 0f : tRaw > 1f ? 1f : tRaw;
                var dx = px - t * ex;
                var dy = py - t * ey;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= reachSquared)
                {
                    continue;
                }

                var index = (int)(distanceSquared * indexScale);
                var alpha = (int)(alphaTable[index] * alphaScale + dither[x & 3]);
                if (alpha <= (int)(row[x] >> 24))
                {
                    continue;
                }

                if (alpha > 255)
                {
                    alpha = 255;
                }

                var white = whiteTable[index];
                var scale = alpha * (1f / 255f);
                row[x] = ((uint)alpha << 24) |
                         ((uint)((red + whiteRed * white) * scale + 0.5f) << 16) |
                         ((uint)((green + whiteGreen * white) * scale + 0.5f) << 8) |
                         (uint)((blue + whiteBlue * white) * scale + 0.5f);
            }
        }
    }

    /// <summary>
    /// A ribbon piece whose radius, opacity and colour vary linearly from end A to end B. Long,
    /// nearly straight runs of the trail are drawn as one piece, which avoids re-shading the same
    /// pixels for every short segment while keeping the gradients perfectly smooth. Rows are shaded
    /// eight pixels at a time with AVX2; the vector and scalar paths are bit-for-bit identical.
    /// </summary>
    public void Ribbon(float ax, float ay, float bx, float by, float radiusA, float radiusB, float fadeA, float fadeB,
        uint rgbA, uint rgbB, ProfileLut lut, bool capStart, bool capEnd, float nextX, float nextY)
    {
        var maxFade = MathF.Max(fadeA, fadeB);
        var maxRadius = MathF.Max(radiusA, radiusB);
        if (_pixels == null || maxFade <= 0.002f || maxRadius <= 0.01f)
        {
            return;
        }

        radiusA = MathF.Max(radiusA, 0.05f);
        radiusB = MathF.Max(radiusB, 0.05f);
        var reach = maxRadius * lut.ReachFor(maxFade);
        if (!Clip(MathF.Min(ax, bx) - reach, MathF.Min(ay, by) - reach, MathF.Max(ax, bx) + reach,
                MathF.Max(ay, by) + reach, out var x0, out var y0, out var x1, out var y1))
        {
            return;
        }

        var shader = new RibbonShader
        {
            Ax = ax,
            Ex = bx - ax,
            Ey = by - ay,
            ReachSquared = reach * reach,
            IndexScaleA = lut.IndexScale / (radiusA * radiusA),
            AlphaA = fadeA * 255f,
            AlphaDelta = (fadeB - fadeA) * 255f,
            RedA = (rgbA >> 16) & 255,
            GreenA = (rgbA >> 8) & 255,
            BlueA = rgbA & 255,
            AlphaTable = lut.Alpha,
            WhiteTable = lut.White
        };
        shader.IndexScaleDelta = lut.IndexScale / (radiusB * radiusB) - shader.IndexScaleA;
        shader.RedDelta = ((rgbB >> 16) & 255) - shader.RedA;
        shader.GreenDelta = ((rgbB >> 8) & 255) - shader.GreenA;
        shader.BlueDelta = (rgbB & 255) - shader.BlueA;
        var lengthSquared = shader.Ex * shader.Ex + shader.Ey * shader.Ey;
        shader.InverseLengthSquared = lengthSquared > 1e-6f ? 1f / lengthSquared : 0f;

        // For a slanted piece only a band of each row can be within reach of the centre line.
        var useBand = lengthSquared > 0.25f && MathF.Abs(shader.Ey) > 1e-3f;
        var inverseEy = useBand ? 1f / shader.Ey : 0f;
        var bandHalfWidth = reach * MathF.Sqrt(lengthSquared);
        var vectorize = Vectorize;
        for (var y = y0; y <= y1; y++)
        {
            var py = y + 0.5f - ay;
            var rowStart = x0;
            var rowEnd = x1;
            if (useBand)
            {
                var cross = shader.Ex * py;
                var first = (cross - bandHalfWidth) * inverseEy + ax - 0.5f;
                var second = (cross + bandHalfWidth) * inverseEy + ax - 0.5f;
                if (first > second)
                {
                    (first, second) = (second, first);
                }

                rowStart = Math.Max(rowStart, (int)MathF.Floor(Math.Clamp(first, x0 - 1, x1 + 1)));
                rowEnd = Math.Min(rowEnd, (int)MathF.Ceiling(Math.Clamp(second, x0 - 1, x1 + 1)));
                if (rowStart > rowEnd)
                {
                    continue;
                }
            }

            var row = _pixels + (long)y * _width;
            var x = rowStart;
            if (vectorize && rowEnd - rowStart >= 7)
            {
                x = shader.ShadeRowAvx2(row, x, rowEnd, py, y);
            }

            shader.ShadeRowScalar(row, x, rowEnd, py, Dither + ((y & 3) << 2));
        }
    }

    /// <summary>Uses AVX2 for ribbons when the CPU supports it; switchable for verification.</summary>
    public static bool Vectorize => Canvas.Vectorize;

    private struct RibbonShader
    {
        public float Ax;
        public float Ex;
        public float Ey;
        public float InverseLengthSquared;
        public float ReachSquared;
        public float IndexScaleA;
        public float IndexScaleDelta;
        public float AlphaA;
        public float AlphaDelta;
        public float RedA;
        public float GreenA;
        public float BlueA;
        public float RedDelta;
        public float GreenDelta;
        public float BlueDelta;
        public float* AlphaTable;
        public float* WhiteTable;

        public readonly void ShadeRowScalar(uint* row, int x, int rowEnd, float py, float* dither)
        {
            var pyEy = py * Ey;
            for (; x <= rowEnd; x++)
            {
                var px = x + 0.5f - Ax;
                var tRaw = (px * Ex + pyEy) * InverseLengthSquared;
                var t = tRaw < 0f ? 0f : tRaw > 1f ? 1f : tRaw;
                var dx = px - t * Ex;
                var dy = py - t * Ey;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= ReachSquared)
                {
                    continue;
                }

                var index = (int)(distanceSquared * (IndexScaleA + IndexScaleDelta * t));
                if ((uint)index >= ProfileLut.Size)
                {
                    continue;
                }

                var alpha = (int)(AlphaTable[index] * (AlphaA + AlphaDelta * t) + dither[x & 3]);
                if (alpha <= (int)(row[x] >> 24))
                {
                    continue;
                }

                if (alpha > 255)
                {
                    alpha = 255;
                }

                var white = WhiteTable[index];
                var red = RedA + RedDelta * t;
                var green = GreenA + GreenDelta * t;
                var blue = BlueA + BlueDelta * t;
                var scale = alpha * (1f / 255f);
                row[x] = ((uint)alpha << 24) |
                         ((uint)((red + (255f - red) * white) * scale + 0.5f) << 16) |
                         ((uint)((green + (255f - green) * white) * scale + 0.5f) << 8) |
                         (uint)((blue + (255f - blue) * white) * scale + 0.5f);
            }
        }

        /// <summary>Shades whole groups of eight pixels and returns the first x left for the scalar tail.</summary>
        public readonly int ShadeRowAvx2(uint* row, int x, int rowEnd, float py, int y)
        {
            var lanes = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
            var half = Vector256.Create(0.5f);
            var zero = Vector256<float>.Zero;
            var one = Vector256.Create(1f);
            var full = Vector256.Create(255f);
            var inverse255 = Vector256.Create(1f / 255f);
            var ax = Vector256.Create(Ax);
            var ex = Vector256.Create(Ex);
            var ey = Vector256.Create(Ey);
            var vy = Vector256.Create(py);
            var pyEy = Vector256.Create(py * Ey);
            var inverseLengthSquared = Vector256.Create(InverseLengthSquared);
            var reachSquared = Vector256.Create(ReachSquared);
            var indexScaleA = Vector256.Create(IndexScaleA);
            var indexScaleDelta = Vector256.Create(IndexScaleDelta);
            var alphaA = Vector256.Create(AlphaA);
            var alphaDelta = Vector256.Create(AlphaDelta);
            var redA = Vector256.Create(RedA);
            var greenA = Vector256.Create(GreenA);
            var blueA = Vector256.Create(BlueA);
            var redDelta = Vector256.Create(RedDelta);
            var greenDelta = Vector256.Create(GreenDelta);
            var blueDelta = Vector256.Create(BlueDelta);
            var tableSize = Vector256.Create(ProfileLut.Size);
            var minusOne = Vector256.Create(-1);
            var maxAlpha = Vector256.Create(255);
            // x advances in steps of 8, so the dither phase of each lane stays fixed along the row.
            var dither = DitherLanes[((y & 3) << 2) | (x & 3)];

            for (; x + 7 <= rowEnd; x += 8)
            {
                var px = Avx.Subtract(Avx.Add(Avx.ConvertToVector256Single(Avx2.Add(Vector256.Create(x), lanes)), half), ax);
                var tRaw = Avx.Multiply(Avx.Add(Avx.Multiply(px, ex), pyEy), inverseLengthSquared);
                var t = Avx.Min(Avx.Max(tRaw, zero), one);
                var dx = Avx.Subtract(px, Avx.Multiply(t, ex));
                var dy = Avx.Subtract(vy, Avx.Multiply(t, ey));
                var distanceSquared = Avx.Add(Avx.Multiply(dx, dx), Avx.Multiply(dy, dy));
                var inside = Avx.CompareLessThan(distanceSquared, reachSquared).AsInt32();
                if (Avx.MoveMask(inside.AsSingle()) == 0)
                {
                    continue;
                }

                var index = Avx.ConvertToVector256Int32WithTruncation(
                    Avx.Multiply(distanceSquared, Avx.Add(indexScaleA, Avx.Multiply(indexScaleDelta, t))));
                var valid = Avx2.And(inside,
                    Avx2.And(Avx2.CompareGreaterThan(index, minusOne), Avx2.CompareGreaterThan(tableSize, index)));
                if (Avx.MoveMask(valid.AsSingle()) == 0)
                {
                    continue;
                }

                var safeIndex = Avx2.And(index, valid);
                var profile = Avx2.GatherVector256(AlphaTable, safeIndex, 4);
                var alpha = Avx.ConvertToVector256Int32WithTruncation(
                    Avx.Add(Avx.Multiply(profile, Avx.Add(alphaA, Avx.Multiply(alphaDelta, t))), dither));
                var existing = Avx.LoadVector256(row + x).AsInt32();
                var write = Avx2.And(valid, Avx2.CompareGreaterThan(alpha, Avx2.ShiftRightLogical(existing, 24)));
                if (Avx.MoveMask(write.AsSingle()) == 0)
                {
                    continue;
                }

                alpha = Avx2.Min(alpha, maxAlpha);
                var white = Avx2.GatherVector256(WhiteTable, safeIndex, 4);
                var scale = Avx.Multiply(Avx.ConvertToVector256Single(alpha), inverse255);
                var red = Avx.Add(redA, Avx.Multiply(redDelta, t));
                var green = Avx.Add(greenA, Avx.Multiply(greenDelta, t));
                var blue = Avx.Add(blueA, Avx.Multiply(blueDelta, t));
                var r = Channel(red, white, scale, full, half);
                var g = Channel(green, white, scale, full, half);
                var b = Channel(blue, white, scale, full, half);
                var packed = Avx2.Or(Avx2.Or(Avx2.ShiftLeftLogical(alpha, 24), Avx2.ShiftLeftLogical(r, 16)),
                    Avx2.Or(Avx2.ShiftLeftLogical(g, 8), b));
                Avx.Store(row + x, Avx2.BlendVariable(existing.AsByte(), packed.AsByte(), write.AsByte()).AsUInt32());
            }

            return x;
        }

        private static Vector256<int> Channel(Vector256<float> color, Vector256<float> white, Vector256<float> scale,
            Vector256<float> full, Vector256<float> half) =>
            Avx.ConvertToVector256Int32WithTruncation(Avx.Add(
                Avx.Multiply(Avx.Add(color, Avx.Multiply(Avx.Subtract(full, color), white)), scale), half));
    }

    // Dither values for eight consecutive pixels, indexed by (row phase << 2) | column phase.
    private static readonly Vector256<float>[] DitherLanes = CreateDitherLanes();

    private static Vector256<float>[] CreateDitherLanes()
    {
        var lanes = new Vector256<float>[16];
        for (var rowPhase = 0; rowPhase < 4; rowPhase++)
        {
            for (var columnPhase = 0; columnPhase < 4; columnPhase++)
            {
                var values = new float[8];
                for (var i = 0; i < 8; i++)
                {
                    values[i] = Dither[(rowPhase << 2) | ((columnPhase + i) & 3)];
                }

                lanes[(rowPhase << 2) | columnPhase] = Vector256.Create(values);
            }
        }

        return lanes;
    }

    /// <summary>A thin glowing ring with a Gaussian cross-section, used by the click ripple.</summary>
    public void Ring(float cx, float cy, float radius, float thickness, float fade, uint rgb, float white)
    {
        if (_pixels == null || fade <= 0.002f || thickness <= 0.05f)
        {
            return;
        }

        var reach = radius + thickness * 2.6f;
        if (!Clip(cx - reach, cy - reach, cx + reach, cy + reach, out var x0, out var y0, out var x1, out var y1))
        {
            return;
        }

        var inner = MathF.Max(0f, radius - thickness * 2.6f);
        var innerSquared = inner * inner;
        var reachSquared = reach * reach;
        var inverseThickness = 1f / thickness;
        var alphaScale = fade * 255f;
        var red = ((rgb >> 16) & 255) + (255f - ((rgb >> 16) & 255)) * white;
        var green = ((rgb >> 8) & 255) + (255f - ((rgb >> 8) & 255)) * white;
        var blue = (rgb & 255) + (255f - (rgb & 255)) * white;
        for (var y = y0; y <= y1; y++)
        {
            var dy = y + 0.5f - cy;
            var row = _pixels + (long)y * _width;
            var dither = Dither + ((y & 3) << 2);
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5f - cx;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared >= reachSquared || distanceSquared <= innerSquared)
                {
                    continue;
                }

                var v = (MathF.Sqrt(distanceSquared) - radius) * inverseThickness;
                Put(row, x, (int)(MathF.Exp(-1.4f * v * v) * alphaScale + dither[x & 3]), red, green, blue);
            }
        }
    }

    /// <summary>A four-point glint: soft core plus two thin flares.</summary>
    public void Shape(float cx, float cy, float size, float angle, float fade, uint rgb, float white,
        ParticleShape shape)
    {
    }

    public void Sparkle(float cx, float cy, float size, float fade, uint rgb, float white)
    {
        if (_pixels == null || fade <= 0.002f || size <= 0.05f)
        {
            return;
        }

        var reach = size * 3.2f;
        if (!Clip(cx - reach, cy - reach, cx + reach, cy + reach, out var x0, out var y0, out var x1, out var y1))
        {
            return;
        }

        var inverseSize = 1f / size;
        var alphaScale = fade * 255f;
        float red = (rgb >> 16) & 255, green = (rgb >> 8) & 255, blue = rgb & 255;
        for (var y = y0; y <= y1; y++)
        {
            var ay = MathF.Abs(y + 0.5f - cy) * inverseSize;
            var row = _pixels + (long)y * _width;
            var dither = Dither + ((y & 3) << 2);
            for (var x = x0; x <= x1; x++)
            {
                var ax = MathF.Abs(x + 0.5f - cx) * inverseSize;
                var core = MathF.Exp(-2.2f * (ax * ax + ay * ay));
                var flare = MathF.Exp(-1.7f * ax - 10f * ay * ay) + MathF.Exp(-1.7f * ay - 10f * ax * ax);
                var intensity = MathF.Min(1f, core + 0.5f * flare);
                var alpha = (int)(intensity * alphaScale + dither[x & 3]);
                if (alpha <= (int)(row[x] >> 24))
                {
                    continue;
                }

                var mix = white * MathF.Min(1f, core * 1.3f + flare * 0.25f);
                Put(row, x, alpha, red + (255f - red) * mix, green + (255f - green) * mix, blue + (255f - blue) * mix);
            }
        }
    }

    private static void Put(uint* row, int x, int alpha, float red, float green, float blue)
    {
        if (alpha <= (int)(row[x] >> 24))
        {
            return;
        }

        if (alpha > 255)
        {
            alpha = 255;
        }

        var scale = alpha * (1f / 255f);
        row[x] = ((uint)alpha << 24) |
                 ((uint)(red * scale + 0.5f) << 16) |
                 ((uint)(green * scale + 0.5f) << 8) |
                 (uint)(blue * scale + 0.5f);
    }

    private bool Clip(float left, float top, float right, float bottom, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = Math.Max(0, (int)MathF.Floor(Math.Max(left, -1f)));
        y0 = Math.Max(0, (int)MathF.Floor(Math.Max(top, -1f)));
        x1 = Math.Min(_clipWidth - 1, (int)MathF.Ceiling(Math.Min(right, _clipWidth)));
        y1 = Math.Min(_clipHeight - 1, (int)MathF.Ceiling(Math.Min(bottom, _clipHeight)));
        if (x0 > x1 || y0 > y1)
        {
            return false;
        }

        var tileX1 = x1 >> TileShift;
        for (var ty = y0 >> TileShift; ty <= y1 >> TileShift; ty++)
        {
            for (var tx = x0 >> TileShift; tx <= tileX1; tx++)
            {
                var tile = ty * _tilesX + tx;
                if (_tileDirty[tile] == 0)
                {
                    _tileDirty[tile] = 1;
                    _dirtyTiles[_dirtyCount++] = tile;
                }
            }
        }

        return true;
    }

    private static float* CreateDither()
    {
        ReadOnlySpan<byte> bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        var table = (float*)NativeMemory.Alloc(16, sizeof(float));
        for (var i = 0; i < 16; i++)
        {
            table[i] = (bayer[i] + 0.5f) / 16f;
        }

        return table;
    }
}
