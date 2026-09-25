using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace MouseGlowTrail;

/// <summary>
/// Software rasteriser over a premultiplied BGRA surface. Every primitive is an analytic distance
/// field, composited with "highest alpha wins": neighbouring pieces of one stroke therefore merge
/// into a single seamless ribbon instead of stacking up into darker beads at every joint.
/// Only 32 px tiles that were touched are cleared again, so the cost follows the drawn area.
/// </summary>
internal sealed unsafe class Canvas : IPrimitiveSink
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
    /// A ribbon piece whose radius, opacity and colour vary linearly from end A to end B; nearly straight
    /// runs of the trail are drawn as one piece. Pieces of one stroke do not re-shade each other: a
    /// piece covers its own slab (between the perpendiculars at A and B), plus the outer wedge that a
    /// bend at B leaves between its slab and the next one; only the ends of a stroke get round caps.
    /// Together that is exactly the round-joined ribbon, with each pixel shaded about once instead of
    /// three or four times. Neighbouring pieces overlap by half a pixel, so no seam can open. Rows are
    /// shaded eight pixels at a time with AVX2; the vector and scalar paths are bit-for-bit identical.
    /// </summary>
    /// <param name="capStart">True when no piece of this stroke is drawn before A (round cap at A).</param>
    /// <param name="capEnd">True when no piece follows B (round cap at B).</param>
    /// <param name="nextX">The chord of the next piece, from B (ignored with <paramref name="capEnd"/>).</param>
    /// <param name="nextY">See <paramref name="nextX"/>.</param>
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
        shader.Limits = RibbonLimits.Compute(shader.Ex, shader.Ey, capStart, capEnd, nextX, nextY);

        // For a slanted piece only a band of each row can be within reach of the centre line.
        var useBand = lengthSquared > 0.25f && MathF.Abs(shader.Ey) > 1e-3f;
        var inverseEy = useBand ? 1f / shader.Ey : 0f;
        var bandHalfWidth = reach * MathF.Sqrt(lengthSquared);
        var vectorize = Vectorize;
        var vectors = vectorize ? new RibbonVectors(shader) : default;
        var lastColumn = _width - 1;
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
            }

            if (shader.Limits.Clipped)
            {
                shader.Limits.NarrowRow(ax, shader.Ex, shader.Ey, shader.InverseLengthSquared, py, ref rowStart,
                    ref rowEnd);
            }

            if (rowStart > rowEnd)
            {
                continue;
            }

            var row = _pixels + (long)y * _width;
            var x = rowStart;
            if (vectorize)
            {
                x = shader.ShadeRowAvx2(vectors, row, x, rowEnd, lastColumn, py, y);
            }

            shader.ShadeRowScalar(row, x, rowEnd, py, Dither + ((y & 3) << 2));
        }
    }

    /// <summary>A ribbon piece's constants, broadcast once per piece rather than once per row.</summary>
    private readonly struct RibbonVectors
    {
        public readonly Vector256<float> Ax;
        public readonly Vector256<float> Ex;
        public readonly Vector256<float> Ey;
        public readonly Vector256<float> InverseLengthSquared;
        public readonly Vector256<float> LowT;
        public readonly Vector256<float> HighT;
        public readonly Vector256<float> NextX;
        public readonly Vector256<float> ReachSquared;
        public readonly Vector256<float> IndexScaleA;
        public readonly Vector256<float> IndexScaleDelta;
        public readonly Vector256<float> AlphaA;
        public readonly Vector256<float> AlphaDelta;
        public readonly Vector256<float> RedA;
        public readonly Vector256<float> GreenA;
        public readonly Vector256<float> BlueA;
        public readonly Vector256<float> RedDelta;
        public readonly Vector256<float> GreenDelta;
        public readonly Vector256<float> BlueDelta;

        public RibbonVectors(in RibbonShader shader)
        {
            Ax = Vector256.Create(shader.Ax);
            Ex = Vector256.Create(shader.Ex);
            Ey = Vector256.Create(shader.Ey);
            InverseLengthSquared = Vector256.Create(shader.InverseLengthSquared);
            LowT = Vector256.Create(shader.Limits.LowT);
            HighT = Vector256.Create(shader.Limits.HighT);
            NextX = Vector256.Create(shader.Limits.NextX);
            ReachSquared = Vector256.Create(shader.ReachSquared);
            IndexScaleA = Vector256.Create(shader.IndexScaleA);
            IndexScaleDelta = Vector256.Create(shader.IndexScaleDelta);
            AlphaA = Vector256.Create(shader.AlphaA);
            AlphaDelta = Vector256.Create(shader.AlphaDelta);
            RedA = Vector256.Create(shader.RedA);
            GreenA = Vector256.Create(shader.GreenA);
            BlueA = Vector256.Create(shader.BlueA);
            RedDelta = Vector256.Create(shader.RedDelta);
            GreenDelta = Vector256.Create(shader.GreenDelta);
            BlueDelta = Vector256.Create(shader.BlueDelta);
        }
    }

    /// <summary>Uses AVX2 for ribbons when the CPU supports it; switchable for verification.</summary>
    public static bool Vectorize { get; set; } = Avx2.IsSupported;

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
        public RibbonLimits Limits;

        public readonly void ShadeRowScalar(uint* row, int x, int rowEnd, float py, float* dither)
        {
            var pyEy = py * Ey;
            var pyMinusEy = py - Ey;
            for (; x <= rowEnd; x++)
            {
                var px = x + 0.5f - Ax;
                var tRaw = (px * Ex + pyEy) * InverseLengthSquared;
                if (!Limits.Owns(tRaw, px - Ex, pyMinusEy))
                {
                    continue;
                }

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

        /// <summary>
        /// Shades the row eight pixels at a time, masking off lanes past <paramref name="rowEnd"/>, as long
        /// as a whole group of eight stays inside the surface row; returns the first x left for the scalar
        /// path (only ever pixels at the surface's right edge).
        /// </summary>
        public readonly int ShadeRowAvx2(in RibbonVectors v, uint* row, int x, int rowEnd, int lastColumn, float py, int y)
        {
            var lanes = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
            var half = Vector256.Create(0.5f);
            var one = Vector256.Create(1f);
            var full = Vector256.Create(255f);
            var inverse255 = Vector256.Create(1f / 255f);
            var vy = Vector256.Create(py);
            var pyEy = Vector256.Create(py * Ey);
            var pyMinusEyNextY = Vector256.Create((py - Ey) * Limits.NextY);
            var tableSize = Vector256.Create(ProfileLut.Size);
            var minusOne = Vector256.Create(-1);
            var maxAlpha = Vector256.Create(255);
            // x advances in steps of 8, so the dither phase of each lane stays fixed along the row.
            var dither = DitherLanes[((y & 3) << 2) | (x & 3)];

            for (; x <= rowEnd && x + 7 <= lastColumn; x += 8)
            {
                var px = Avx.Subtract(Avx.Add(Avx.ConvertToVector256Single(Avx2.Add(Vector256.Create(x), lanes)), half), v.Ax);
                var tRaw = Avx.Multiply(Avx.Add(Avx.Multiply(px, v.Ex), pyEy), v.InverseLengthSquared);
                // This piece's share of the stroke: its own slab, or the outer wedge at the next joint.
                var towardsNext = Avx.Add(Avx.Multiply(Avx.Subtract(px, v.Ex), v.NextX), pyMinusEyNextY);
                var owned = Avx.And(Avx.CompareGreaterThanOrEqual(tRaw, v.LowT),
                    Avx.Or(Avx.CompareLessThanOrEqual(tRaw, v.HighT), Avx.CompareLessThan(towardsNext, one)));
                var t = Avx.Min(Avx.Max(tRaw, Vector256<float>.Zero), one);
                var dx = Avx.Subtract(px, Avx.Multiply(t, v.Ex));
                var dy = Avx.Subtract(vy, Avx.Multiply(t, v.Ey));
                var distanceSquared = Avx.Add(Avx.Multiply(dx, dx), Avx.Multiply(dy, dy));
                var inside = Avx.And(Avx.CompareLessThan(distanceSquared, v.ReachSquared), owned).AsInt32();
                if (x + 7 > rowEnd)
                {
                    inside = Avx2.And(inside, Avx2.CompareGreaterThan(Vector256.Create(rowEnd - x + 1), lanes));
                }

                if (Avx.MoveMask(inside.AsSingle()) == 0)
                {
                    continue;
                }

                var index = Avx.ConvertToVector256Int32WithTruncation(
                    Avx.Multiply(distanceSquared, Avx.Add(v.IndexScaleA, Avx.Multiply(v.IndexScaleDelta, t))));
                var valid = Avx2.And(inside,
                    Avx2.And(Avx2.CompareGreaterThan(index, minusOne), Avx2.CompareGreaterThan(tableSize, index)));
                if (Avx.MoveMask(valid.AsSingle()) == 0)
                {
                    continue;
                }

                var safeIndex = Avx2.And(index, valid);
                var profile = Avx2.GatherVector256(AlphaTable, safeIndex, 4);
                var alpha = Avx.ConvertToVector256Int32WithTruncation(
                    Avx.Add(Avx.Multiply(profile, Avx.Add(v.AlphaA, Avx.Multiply(v.AlphaDelta, t))), dither));
                var existing = Avx.LoadVector256(row + x).AsInt32();
                var write = Avx2.And(valid, Avx2.CompareGreaterThan(alpha, Avx2.ShiftRightLogical(existing, 24)));
                if (Avx.MoveMask(write.AsSingle()) == 0)
                {
                    continue;
                }

                alpha = Avx2.Min(alpha, maxAlpha);
                var white = Avx2.GatherVector256(WhiteTable, safeIndex, 4);
                var scale = Avx.Multiply(Avx.ConvertToVector256Single(alpha), inverse255);
                var red = Avx.Add(v.RedA, Avx.Multiply(v.RedDelta, t));
                var green = Avx.Add(v.GreenA, Avx.Multiply(v.GreenDelta, t));
                var blue = Avx.Add(v.BlueA, Avx.Multiply(v.BlueDelta, t));
                var r = Channel(red, white, scale, full, half);
                var g = Channel(green, white, scale, full, half);
                var b = Channel(blue, white, scale, full, half);
                var packed = Avx2.Or(Avx2.Or(Avx2.ShiftLeftLogical(alpha, 24), Avx2.ShiftLeftLogical(r, 16)),
                    Avx2.Or(Avx2.ShiftLeftLogical(g, 8), b));
                // Unwritten lanes (including any past the row end) store back what was there.
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

    /// <summary>
    /// A four-point glint: soft core plus two thin flares. Every term is a product of a function of x
    /// and a function of y (exp(a + b) = exp(a) exp(b)), so the exponentials are taken once per column
    /// and once per row instead of three times per pixel.
    /// </summary>
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
        var columns = x1 - x0 + 1;
        var factors = stackalloc float[columns * 3];
        for (var i = 0; i < columns; i++)
        {
            var ax = MathF.Abs(x0 + i + 0.5f - cx) * inverseSize;
            SparkleFactors(ax, out factors[i * 3], out factors[i * 3 + 1], out factors[i * 3 + 2]);
        }

        for (var y = y0; y <= y1; y++)
        {
            var ay = MathF.Abs(y + 0.5f - cy) * inverseSize;
            SparkleFactors(ay, out var coreY, out var linearY, out var squareY);
            var row = _pixels + (long)y * _width;
            var dither = Dither + ((y & 3) << 2);
            var column = factors;
            for (var x = x0; x <= x1; x++, column += 3)
            {
                var core = column[0] * coreY;
                var flare = column[1] * squareY + linearY * column[2];
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

    /// <summary>A small glowing shape: an antialiased fill inside a soft halo (see <see cref="ShapeField"/>).</summary>
    public void Shape(float cx, float cy, float size, float angle, float fade, uint rgb, float white, ParticleShape shape)
    {
        if (_pixels == null || fade <= 0.002f || size <= 0.05f)
        {
            return;
        }

        var reach = size * FrameComposer.ShapeReach;
        if (!Clip(cx - reach, cy - reach, cx + reach, cy + reach, out var x0, out var y0, out var x1, out var y1))
        {
            return;
        }

        var inverseSize = 1f / size;
        var cos = MathF.Cos(angle) * inverseSize;
        var sin = MathF.Sin(angle) * inverseSize;
        var alphaScale = fade * 255f;
        float red = (rgb >> 16) & 255, green = (rgb >> 8) & 255, blue = rgb & 255;
        for (var y = y0; y <= y1; y++)
        {
            var dy = y + 0.5f - cy;
            var row = _pixels + (long)y * _width;
            var dither = Dither + ((y & 3) << 2);
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5f - cx;
                // Into the shape's own frame: turned back by the angle, y pointing up.
                var distance = ShapeField.Distance(shape, dx * cos + dy * sin, dx * sin - dy * cos);
                var intensity = ShapeIntensity(distance, size, out var coverage);
                var alpha = (int)(intensity * alphaScale + dither[x & 3]);
                if (alpha <= (int)(row[x] >> 24))
                {
                    continue;
                }

                var mix = white * coverage * 0.4f;
                Put(row, x, alpha, red + (255f - red) * mix, green + (255f - green) * mix, blue + (255f - blue) * mix);
            }
        }
    }

    /// <summary>Shape shading from its distance field: a crisp one-pixel edge plus a halo reaching about half a size out.</summary>
    public static float ShapeIntensity(float distance, float size, out float coverage)
    {
        coverage = Math.Clamp(0.5f - distance * size, 0f, 1f);
        var outside = MathF.Max(distance, 0f) * (1f / 0.45f);
        var glow = 0.42f * MathF.Exp(-outside * outside);
        return 1f - (1f - 0.95f * coverage) * (1f - glow);
    }

    /// <summary>The per-axis factors of a glint: exp(-2.2 a^2) for the core, exp(-1.7 a) and exp(-10 a^2) for the flares.</summary>
    public static void SparkleFactors(float a, out float core, out float linear, out float square)
    {
        core = MathF.Exp(-2.2f * a * a);
        linear = MathF.Exp(-1.7f * a);
        square = MathF.Exp(-10f * a * a);
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
