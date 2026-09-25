using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseGlowTrail.Bench;

/// <summary>Splits the CPU frame cost into its phases (model, bounds, clearing, ribbon, particles).</summary>
internal static unsafe class Profile
{
    public static void Run()
    {
        foreach (var (name, config, scale) in new[]
                 {
                     ("极光 标准 60% loop", Scene.MakeConfig(TrailStyleId.Aurora, opacity: 60), 1.0),
                     ("幻彩 短 纤细 星尘 45% loop", Scene.MakeConfig(TrailStyleId.Prism, sparkles: true, width: TrailWidth.Thin, length: TrailLength.Short, opacity: 45), 1.0),
                     ("极光 标准 100% big", Scene.MakeConfig(TrailStyleId.Aurora), 2.2),
                 })
        {
            for (var pass = 0; pass < 2; pass++)
            {
                Measure(name, config, scale, pass == 1);
            }
        }
    }

    private static void Measure(string name, RenderConfig config, double scale, bool print)
    {
        const int fps = 180, w = 2560, h = 1440;
        var pixels = (uint*)NativeMemory.AllocZeroed(w * h, sizeof(uint));
        var empty = new TrailModel();
        var noParticles = new Particles();
        var noClicks = new ClickEffects();
        var phases = new double[6];
        double moteTicks = 0, starTicks = 0;
        long moteCount = 0, starCount = 0;
        int windowWidth = 0, windowHeight = 0;
        try
        {
            var scene = new Scene(config);
            var canvas = new Canvas();
            canvas.Attach(pixels, w, h);
            var frames = 4 * fps;
            for (var frame = 0; frame < frames; frame++)
            {
                var now = frame / (double)fps;
                var t0 = Stopwatch.GetTimestamp();
                scene.Step(Gesture.At(now, scale, 1280, 720), now);
                var t1 = Stopwatch.GetTimestamp();
                if (!FrameComposer.TryGetBounds(scene.Model, scene.Particles, scene.Clicks, config, out var b))
                {
                    continue;
                }

                var t2 = Stopwatch.GetTimestamp();
                var left = Math.Max(b.Left, 0);
                var top = Math.Max(b.Top, 0);
                var needWidth = Math.Min(b.Right, w) - left;
                var needHeight = Math.Min(b.Bottom, h) - top;
                windowWidth = TrailEngineMath.PickExtent(windowWidth, needWidth, w);
                windowHeight = TrailEngineMath.PickExtent(windowHeight, needHeight, h);
                var x = Math.Clamp(left - (windowWidth - needWidth) / 2, 0, w - windowWidth);
                var y = Math.Clamp(top - (windowHeight - needHeight) / 2, 0, h - windowHeight);
                canvas.BeginFrame(windowWidth, windowHeight);
                var t3 = Stopwatch.GetTimestamp();
                FrameComposer.Draw(canvas, scene.Model, noParticles, noClicks, config, now, x, y);
                var t4 = Stopwatch.GetTimestamp();
                FrameComposer.Draw(canvas, empty, scene.Particles, scene.Clicks, config, now, x, y);
                var t5 = Stopwatch.GetTimestamp();
                // Particle split (same maths as FrameComposer.DrawParticles), for the report only.
                var t6 = Stopwatch.GetTimestamp();
                var motes = 0;
                for (var i = 0; i < scene.Particles.Count; i++)
                {
                    ref readonly var particle = ref scene.Particles[i];
                    if (particle.Shape != ParticleShape.Glint)
                    {
                        motes++;
                        canvas.Capsule(particle.X - x, particle.Y - y, particle.X - x, particle.Y - y, particle.Size, 0.5f,
                            0xFFFFFF, Profiles.Particle);
                    }
                }

                var t7 = Stopwatch.GetTimestamp();
                for (var i = 0; i < scene.Particles.Count; i++)
                {
                    ref readonly var particle = ref scene.Particles[i];
                    if (particle.Shape == ParticleShape.Glint)
                    {
                        canvas.Sparkle(particle.X - x, particle.Y - y, particle.Size, 0.5f, 0xFFFFFF, 0.7f);
                    }
                }

                var t8 = Stopwatch.GetTimestamp();
                moteTicks += t7 - t6;
                starTicks += t8 - t7;
                moteCount += motes;
                starCount += scene.Particles.Count - motes;
                phases[0] += t1 - t0;
                phases[1] += t2 - t1;
                phases[2] += t3 - t2;
                phases[3] += t4 - t3;
                phases[4] += t5 - t4;
                phases[5] += t5 - t0;
            }

            if (print)
            {
                double Ms(double ticks) => ticks * 1000.0 / Stopwatch.Frequency / frames;
                Console.WriteLine($"{name,-28} model {Ms(phases[0]):0.000}  bounds {Ms(phases[1]):0.000}  clear {Ms(phases[2]):0.000}  ribbon {Ms(phases[3]):0.000}  particles+clicks {Ms(phases[4]):0.000}  total {Ms(phases[5]):0.000} ms/frame");
                if (moteCount + starCount > 0)
                {
                    Console.WriteLine($"{"",-28} motes {moteCount / (double)frames:0} per frame, {Ms(moteTicks):0.000} ms;  stars {starCount / (double)frames:0} per frame, {Ms(starTicks):0.000} ms");
                }
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }
}
