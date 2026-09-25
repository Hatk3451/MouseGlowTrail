using System.Drawing;
using System.Runtime.InteropServices;

namespace MouseGlowTrail.Bench;

/// <summary>Diagnostics: records ribbon pieces and explains the pixels where two renderings disagree.</summary>
internal static unsafe class RibbonProbe
{
    private sealed class Recorder : IPrimitiveSink
    {
        public readonly List<Piece> Pieces = [];

        public void Capsule(float ax, float ay, float bx, float by, float radius, float fade, uint rgb, ProfileLut lut)
        {
        }

        public void Ribbon(float ax, float ay, float bx, float by, float radiusA, float radiusB, float fadeA,
            float fadeB, uint rgbA, uint rgbB, ProfileLut lut, bool capStart, bool capEnd, float nextX, float nextY) =>
            Pieces.Add(new Piece(ax, ay, bx, by, radiusA, radiusB, fadeA, fadeB, lut, capStart, capEnd, nextX, nextY));

        public void Ring(float cx, float cy, float radius, float thickness, float fade, uint rgb, float white)
        {
        }

        public void Shape(float cx, float cy, float size, float angle, float fade, uint rgb, float white,
        ParticleShape shape)
    {
    }

    public void Sparkle(float cx, float cy, float size, float fade, uint rgb, float white)
        {
        }
    }

    private readonly record struct Piece(float Ax, float Ay, float Bx, float By, float RadiusA, float RadiusB,
        float FadeA, float FadeB, ProfileLut Lut, bool CapStart, bool CapEnd, float NextX, float NextY);

    public static void Run()
    {
        const int w = 1400, h = 900;
        var scene = Scene.Run(Scene.MakeConfig(TrailStyleId.Neon), t => Gesture.At(t, 1.2, 700, 450), 1.4);
        var recorder = new Recorder();
        FrameComposer.Draw(recorder, scene.Model, new Particles(), new ClickEffects(), scene.Config, 1.4, 0, 0);
        var fresh = (uint*)NativeMemory.AllocZeroed(w * h, 4);
        var reference = (uint*)NativeMemory.AllocZeroed(w * h, 4);
        var canvas = new Canvas();
        canvas.Attach(fresh, w, h);
        canvas.BeginFrame(w, h);
        var old = new ReferenceCanvas();
        old.Attach(reference, w, h);
        old.BeginFrame(w, h);
        FrameComposer.Draw(canvas, scene.Model, new Particles(), new ClickEffects(), scene.Config, 1.4, 0, 0);
        FrameComposer.Draw(old, scene.Model, new Particles(), new ClickEffects(), scene.Config, 1.4, 0, 0);
        var worst = new List<(int X, int Y, int Diff)>();
        for (var i = 0; i < w * h; i++)
        {
            var diff = 0;
            for (var shift = 0; shift < 32; shift += 8)
            {
                diff = Math.Max(diff, Math.Abs((int)((fresh[i] >> shift) & 255) - (int)((reference[i] >> shift) & 255)));
            }

            if (diff > 1)
            {
                worst.Add((i % w, i / w, diff));
            }
        }

        Console.WriteLine($"{recorder.Pieces.Count} pieces, {worst.Count} pixels differ by more than one level in some channel");
        foreach (var (x, y, diff) in worst.OrderByDescending(p => p.Diff).Take(8))
        {
            Console.WriteLine($"pixel ({x},{y}): new {fresh[y * w + x]:X8}, reference {reference[y * w + x]:X8}");
            for (var k = 0; k < recorder.Pieces.Count; k++)
            {
                var piece = recorder.Pieces[k];
                var (alpha, t, distance) = Evaluate(piece, x, y);
                if (alpha < 1f)
                {
                    continue;
                }

                var limits = RibbonLimits.Compute(piece.Bx - piece.Ax, piece.By - piece.Ay, piece.CapStart, piece.CapEnd,
                    piece.NextX, piece.NextY);
                var px = x + 0.5f - piece.Ax;
                var py = y + 0.5f - piece.Ay;
                var ex = piece.Bx - piece.Ax;
                var ey = piece.By - piece.Ay;
                var owns = limits.Owns(t, px - ex, py - ey);
                Console.WriteLine($"   piece {k,3}: alpha {alpha,6:0.0}  t {t,6:0.00}  dist {distance,5:0.0}  len {MathF.Sqrt(ex * ex + ey * ey),5:0.0}  r {piece.RadiusA:0.0}->{piece.RadiusB:0.0}  fade {piece.FadeA:0.00}->{piece.FadeB:0.00}  caps {(piece.CapStart ? "S" : "-")}{(piece.CapEnd ? "E" : "-")}  {(owns ? "OWNS" : "")}");
            }
        }

        NativeMemory.Free(fresh);
        NativeMemory.Free(reference);
    }

    /// <summary>Alpha (before dithering) the old capsule rule gives this piece at the pixel, and its raw t.</summary>
    private static (float Alpha, float T, float Distance) Evaluate(in Piece piece, int x, int y)
    {
        var ex = piece.Bx - piece.Ax;
        var ey = piece.By - piece.Ay;
        var lengthSquared = ex * ex + ey * ey;
        var inverse = lengthSquared > 1e-6f ? 1f / lengthSquared : 0f;
        var px = x + 0.5f - piece.Ax;
        var py = y + 0.5f - piece.Ay;
        var tRaw = (px * ex + py * ey) * inverse;
        var t = Math.Clamp(tRaw, 0f, 1f);
        var dx = px - t * ex;
        var dy = py - t * ey;
        var distanceSquared = dx * dx + dy * dy;
        var radiusA = MathF.Max(piece.RadiusA, 0.05f);
        var radiusB = MathF.Max(piece.RadiusB, 0.05f);
        var reach = MathF.Max(piece.RadiusA, piece.RadiusB) * piece.Lut.ReachFor(MathF.Max(piece.FadeA, piece.FadeB));
        if (distanceSquared >= reach * reach)
        {
            return (0f, tRaw, MathF.Sqrt(distanceSquared));
        }

        var scaleA = piece.Lut.IndexScale / (radiusA * radiusA);
        var scaleB = piece.Lut.IndexScale / (radiusB * radiusB);
        var index = (int)(distanceSquared * (scaleA + (scaleB - scaleA) * t));
        if ((uint)index >= ProfileLut.Size)
        {
            return (0f, tRaw, MathF.Sqrt(distanceSquared));
        }

        return (piece.Lut.Alpha[index] * (piece.FadeA + (piece.FadeB - piece.FadeA) * t) * 255f, tRaw,
            MathF.Sqrt(distanceSquared));
    }
}
