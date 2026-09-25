using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MouseGlowTrail.Bench;

internal static class Program
{
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "all";
        var output = Path.GetFullPath(args.Length > 1 && mode != "live" ? args[1] : "out");
        Directory.CreateDirectory(output);
        if (mode is "all" or "icon")
        {
            IconOutput.Write(output);
        }

        if (mode is "all" or "preview")
        {
            Preview.Write(output);
        }

        var ok = true;
        if (mode is "all" or "verify")
        {
            ok = Verify.Run();
        }

        if (mode is "all" or "bench")
        {
            Bench.Run();
        }

        if (mode is "profile")
        {
            Profile.Run();
        }

        if (mode is "demo")
        {
            Demo.Write(output);
        }

        if (mode is "gpumem")
        {
            GpuMemory.Run();
        }

        if (mode is "probe")
        {
            RibbonProbe.Run();
        }

        // Not part of "all": it draws on the real desktop for about half a minute.
        if (mode is "live")
        {
            ok &= Live.Run(args.Length > 1 ? args[1] : "both");
        }

        return ok ? 0 : 1;
    }
}

internal static class Gesture
{
    /// <summary>A looping, handwriting-like path at roughly 900-1900 px/s (scale 1).</summary>
    public static Point At(double t, double scale = 1.0, double cx = 450, double cy = 282) => new(
        (int)Math.Round(cx + scale * (290 * Math.Sin(2 * Math.PI * 0.55 * t) + 70 * Math.Sin(2 * Math.PI * 1.7 * t + 0.4))),
        (int)Math.Round(cy + scale * (165 * Math.Sin(2 * Math.PI * 0.9 * t + 0.8) + 25 * Math.Sin(2 * Math.PI * 2.3 * t))));

    /// <summary>A fast sweep that decelerates and stops at t = 0.42 s.</summary>
    public static Point Flick(double t)
    {
        var p = Math.Clamp(t / 0.42, 0, 1);
        var e = 1 - Math.Pow(1 - p, 3);
        return new Point((int)Math.Round(120 + 640 * e), (int)Math.Round(420 - 300 * e + 90 * Math.Sin(Math.PI * e)));
    }
}

internal sealed class Scene
{
    private double _last;

    public Scene(RenderConfig config)
    {
        Config = config;
        Model.CycleLength = config.Style.CycleLength;
        Model.SmoothingCutoff = config.SmoothingCutoff;
        Model.SpeedResponse = config.SpeedResponse;
        Particles.Options = config.Particles;
        Model.Spawner = config.Particles.Enabled ? Particles : null;
    }

    private Scene(RenderConfig config, Scene source)
        : this(config)
    {
        Model = source.Model;
        Particles = source.Particles;
        Clicks = source.Clicks;
        _last = source._last;
        Model.CycleLength = config.Style.CycleLength;
        Particles.Options = config.Particles;
        Model.Spawner = config.Particles.Enabled ? Particles : null;
    }

    /// <summary>The same trail, particles and effects carried on with another configuration.</summary>
    public Scene WithConfig(RenderConfig config) => new(config, this);

    public TrailModel Model { get; } = new();
    public Particles Particles { get; } = new();
    public ClickEffects Clicks { get; } = new();
    public RenderConfig Config { get; }

    public static RenderConfig MakeConfig(TrailStyleId id, bool sparkles = false,
        TrailWidth width = TrailWidth.Standard, TrailLength length = TrailLength.Standard, int opacity = 100,
        ParticleKind particles = ParticleKind.Stardust) => new()
    {
        Style = TrailStyles.Get(id),
        Lifetime = TrailOptions.Lifetime(length),
        Width = TrailOptions.WidthScale(width),
        Opacity = opacity / 100f,
        Particles = new ParticleOptions(sparkles, particles, 1f, 1f, 1f, EffectColor.FollowTrail)
    };

    public void Step(Point cursor, double now)
    {
        Model.AddSample(cursor.X + 8, cursor.Y + 14, now, 1f);
        Model.Expire(now, Config.Lifetime);
        Particles.Update((float)Math.Clamp(now - _last, 0, 0.05));
        Clicks.Expire(now);
        _last = now;
    }

    public void Click(Point cursor, double now) => Clicks.Add(cursor.X, cursor.Y, now, Model.Phase, 1f);

    /// <summary>Like <see cref="Run"/>, firing every click and wheel effect along the way (two colour modes each).</summary>
    public static Scene RunWithEffects(RenderConfig config, Func<double, Point> path, double end)
    {
        var scene = new Scene(config);
        var clicks = Enum.GetValues<ClickEffect>().Skip(1).ToArray();
        var wheels = Enum.GetValues<WheelEffect>().Skip(1).ToArray();
        var fired = 0;
        for (var frame = 0; ; frame++)
        {
            var t = Math.Min(end, frame / 180.0);
            var point = path(t);
            scene.Step(point, t);
            if (t >= end - 0.5 && fired < clicks.Length + wheels.Length && frame % 9 == 0)
            {
                var color = fired % 2 == 0 ? EffectColor.FollowTrail : 0xFF66FFCC;
                if (fired < clicks.Length)
                {
                    EffectSpawner.Click(scene.Clicks, scene.Particles, clicks[fired], point.X, point.Y, t,
                        scene.Model.Phase, 1.25f, color);
                }
                else
                {
                    var direction = fired % 2 == 0 ? -1f : 1f;
                    EffectSpawner.Wheel(scene.Clicks, scene.Particles, wheels[fired - clicks.Length], point.X, point.Y,
                        0f, direction, t, scene.Model.Phase, 1.25f, color);
                }

                fired++;
            }

            if (t >= end)
            {
                return scene;
            }
        }
    }

    /// <summary>Runs a path at 180 Hz up to <paramref name="end"/>, optionally clicking at a time.</summary>
    public static Scene Run(RenderConfig config, Func<double, Point> path, double end, double clickAt = -1)
    {
        var scene = new Scene(config);
        var clicked = false;
        for (var frame = 0; ; frame++)
        {
            var t = Math.Min(end, frame / 180.0);
            scene.Step(path(t), t);
            if (!clicked && clickAt >= 0 && t >= clickAt)
            {
                scene.Click(path(t), t);
                clicked = true;
            }

            if (t >= end)
            {
                return scene;
            }
        }
    }
}

internal static unsafe class Raster
{
    /// <summary>Renders a scene at absolute coordinates into a fresh premultiplied bitmap.</summary>
    public static Bitmap Render(Scene scene, double now, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite,
            PixelFormat.Format32bppPArgb);
        try
        {
            var pixels = (uint*)data.Scan0;
            new Span<uint>(pixels, width * height).Clear();
            var canvas = new Canvas();
            canvas.Attach(pixels, width, height);
            canvas.BeginFrame(width, height);
            FrameComposer.Draw(canvas, scene.Model, scene.Particles, scene.Clicks, scene.Config, now, 0, 0);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}

internal static class Preview
{
    private const int Width = 900;
    private const int Height = 560;
    private const double End = 1.55;

    public static void Write(string output)
    {
        CompareOldAndNew(Path.Combine(output, "compare-v1-v2.png"));
        foreach (var dark in new[] { true, false })
        {
            StyleGallery(Path.Combine(output, dark ? "styles-dark.png" : "styles-light.png"), dark);
        }

        OpacitySheet(Path.Combine(output, "opacity.png"));
        ClickFilm(Path.Combine(output, "click-ripple.png"));
        FlickFilm(Path.Combine(output, "flick-stop.png"));
        ParticleGallery(Path.Combine(output, "particles.png"));
        EffectsFilm(Path.Combine(output, "effects.png"));
        Console.WriteLine($"previews -> {output}");
    }

    private static void ParticleGallery(string path)
    {
        var kinds = Enum.GetValues<ParticleKind>();
        const int columns = 4, cellWidth = 600, cellHeight = 380;
        using var sheet = new Bitmap(cellWidth * columns, cellHeight * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        for (var i = 0; i < kinds.Length; i++)
        {
            var config = Scene.MakeConfig(TrailStyleId.Aurora, sparkles: true, particles: kinds[i], opacity: 80);
            var scene = Scene.Run(config, t => Gesture.At(t, 0.62, 300, 190), End);
            using var layer = Raster.Render(scene, End, cellWidth, cellHeight);
            DrawPanel(g, new Rectangle(i % columns * cellWidth, i / columns * cellHeight, cellWidth, cellHeight), true,
                layer, TrailOptions.Label(kinds[i]));
        }

        sheet.Save(path, ImageFormat.Png);
    }

    /// <summary>Every click and wheel effect, frame by frame (rows), on a dark backdrop.</summary>
    private static void EffectsFilm(string path)
    {
        double[] ages = [0.02, 0.06, 0.12, 0.2, 0.3, 0.42, 0.56];
        var rows = Enum.GetValues<ClickEffect>().Skip(1).Select(e => (Label: TrailOptions.Label(e), Click: e, Wheel: WheelEffect.None))
            .Concat(Enum.GetValues<WheelEffect>().Skip(1).Select(e => (Label: "滚轮 " + TrailOptions.Label(e), Click: ClickEffect.None, Wheel: e)))
            .ToArray();
        const int cell = 150;
        using var sheet = new Bitmap(cell * ages.Length, cell * rows.Length, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        for (var row = 0; row < rows.Length; row++)
        {
            for (var i = 0; i < ages.Length; i++)
            {
                var scene = new Scene(Scene.MakeConfig(TrailStyleId.Aurora, opacity: 90));
                var center = new Point(cell / 2, cell / 2);
                scene.Step(center, 0);
                if (rows[row].Click != ClickEffect.None)
                {
                    EffectSpawner.Click(scene.Clicks, scene.Particles, rows[row].Click, center.X, center.Y, 0, 0.3f, 1f, 0);
                }
                else
                {
                    EffectSpawner.Wheel(scene.Clicks, scene.Particles, rows[row].Wheel, center.X, center.Y, 0f, -1f, 0,
                        0.3f, 1f, 0);
                }

                for (var frame = 1; frame / 180.0 <= ages[i]; frame++)
                {
                    scene.Step(center, frame / 180.0);
                }

                using var layer = Raster.Render(scene, ages[i], cell, cell);
                DrawPanel(g, new Rectangle(i * cell, row * cell, cell, cell), true, layer,
                    i == 0 ? rows[row].Label : $"{ages[i] * 1000:0} ms");
            }
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static void CompareOldAndNew(string path)
    {
        using var sheet = new Bitmap(Width * 2, Height * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        var old = new OldRenderer(2560, 1440);
        var oldNow = 0.0;
        for (var tick = 0; tick / 64.0 <= End; tick++)
        {
            oldNow = tick / 64.0;
            old.Sample(Gesture.At(oldNow), oldNow);
        }

        var scene = Scene.Run(Scene.MakeConfig(TrailStyleId.Aurora), t => Gesture.At(t), End);
        for (var row = 0; row < 2; row++)
        {
            var dark = row == 0;
            using var oldLayer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (var og = Graphics.FromImage(oldLayer))
            {
                og.Clear(Color.Transparent);
                old.RenderInto(og, oldNow);
            }

            using var newLayer = Raster.Render(scene, End, Width, Height);
            DrawPanel(g, new Rectangle(0, row * Height, Width, Height), dark, oldLayer, "v1  GDI+ · 64 Hz");
            DrawPanel(g, new Rectangle(Width, row * Height, Width, Height), dark, newLayer, "v2  极光 · 180 Hz");
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static void StyleGallery(string path, bool dark)
    {
        var panels = TrailStyles.All.Select(s => (Config: Scene.MakeConfig(s.Id), Label: $"{s.Name}  {s.Description}"))
            .Append((Config: Scene.MakeConfig(TrailStyleId.Aurora, sparkles: true), Label: "极光 + 星尘粒子"))
            .ToArray();
        const int columns = 3;
        using var sheet = new Bitmap(Width * columns, Height * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        for (var i = 0; i < panels.Length; i++)
        {
            var scene = Scene.Run(panels[i].Config, t => Gesture.At(t), End, clickAt: End - 0.09);
            using var layer = Raster.Render(scene, End, Width, Height);
            DrawPanel(g, new Rectangle(i % columns * Width, i / columns * Height, Width, Height), dark, layer,
                panels[i].Label);
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static void OpacitySheet(string path)
    {
        var levels = TrailOptions.Opacities;
        const int cellWidth = 540, cellHeight = 340;
        using var sheet = new Bitmap(cellWidth * levels.Length, cellHeight * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        for (var i = 0; i < levels.Length; i++)
        {
            var config = Scene.MakeConfig(TrailStyleId.Aurora, opacity: levels[i]);
            var scene = Scene.Run(config, t => Gesture.At(t, 0.6, 270, 175), End, clickAt: End - 0.09);
            using var layer = Raster.Render(scene, End, cellWidth, cellHeight);
            var label = TrailOptions.OpacityLabel(levels[i]).Replace('\t', ' ');
            DrawPanel(g, new Rectangle(i * cellWidth, 0, cellWidth, cellHeight), true, layer, label);
            DrawPanel(g, new Rectangle(i * cellWidth, cellHeight, cellWidth, cellHeight), false, layer, label);
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static void ClickFilm(string path)
    {
        double[] ages = [0.0, 0.04, 0.09, 0.15, 0.23, 0.33, 0.45];
        const int cell = 140;
        using var sheet = new Bitmap(cell * ages.Length, cell * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        for (var row = 0; row < 2; row++)
        {
            for (var i = 0; i < ages.Length; i++)
            {
                var scene = new Scene(Scene.MakeConfig(TrailStyleId.Aurora));
                scene.Click(new Point(cell / 2, cell / 2), 0);
                using var layer = Raster.Render(scene, ages[i], cell, cell);
                DrawPanel(g, new Rectangle(i * cell, row * cell, cell, cell), row == 0, layer, $"{ages[i] * 1000:0} ms");
            }
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static void FlickFilm(string path)
    {
        double[] times = [0.25, 0.45, 0.8, 1.2];
        using var sheet = new Bitmap(Width * 2, Height * 2, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        for (var i = 0; i < times.Length; i++)
        {
            var scene = Scene.Run(Scene.MakeConfig(TrailStyleId.Aurora), Gesture.Flick, times[i]);
            using var layer = Raster.Render(scene, times[i], Width, Height);
            DrawPanel(g, new Rectangle(i % 2 * Width, i / 2 * Height, Width, Height), true, layer,
                $"快速甩动后停下 · t = {times[i]:0.00} s");
        }

        sheet.Save(path, ImageFormat.Png);
    }

    public static void DrawPanel(Graphics g, Rectangle area, bool dark, Bitmap layer, string label)
    {
        using (var background = new LinearGradientBrush(area,
                   dark ? Color.FromArgb(24, 25, 31) : Color.FromArgb(246, 246, 248),
                   dark ? Color.FromArgb(38, 40, 52) : Color.FromArgb(232, 234, 240), 60f))
        {
            g.FillRectangle(background, area);
        }

        g.CompositingMode = CompositingMode.SourceOver;
        g.DrawImage(layer, area.Location);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Microsoft YaHei UI", area.Width < 200 ? 9f : 13f);
        using var brush = new SolidBrush(dark ? Color.FromArgb(170, 255, 255, 255) : Color.FromArgb(150, 0, 0, 0));
        g.DrawString(label, font, brush, area.X + 12, area.Y + 10);
    }
}

internal static unsafe class Verify
{
    public static bool Run()
    {
        var ok = true;
        ok &= Check("dirty-tile clearing leaves no residue", DirtyTilesMatchFreshRender());
        ok &= Check("palettes are seamless (no hard colour jump)", PalettesAreContinuous());
        ok &= Check("strokes never bridge across a pause in the trail cursor", StrokesDoNotBridge());
        ok &= Check("scene empties itself after the lifetime", SceneEmpties());
        ok &= Check("a stroke resumed after a settled spell starts unfaded", ResumedStrokeStartsFresh());
        ok &= Check("AVX2 and scalar shading are bit-identical", VectorMatchesScalar());
        ok &= Check("GPU renders the same pixels as the CPU (within 1 level)", GpuMatchesCpu());
        ok &= Check("ribbon without overdraw keeps the round-joined coverage (alpha within 1 level)", RibbonMatchesReference());
        ok &= Check("narrowing rows to a piece's share changes no pixel", NarrowingIsExact());
        ok &= Check("v2 settings files keep their look after the move to v3", SettingsMigrate());
        ok &= Check("default v3 settings draw exactly what v2.1 drew", DefaultsMatchV21());
        return ok;
    }

    /// <summary>The per-row span narrowing is only a shortcut: the per-pixel ownership test must agree with it.</summary>
    private static bool NarrowingIsExact()
    {
        foreach (var (config, path, end) in new (RenderConfig, Func<double, Point>, double)[]
                 {
                     (Scene.MakeConfig(TrailStyleId.Neon, width: TrailWidth.Bold), t => Gesture.At(t, 1.3), 1.4),
                     (Scene.MakeConfig(TrailStyleId.Prism, width: TrailWidth.Thin, length: TrailLength.Short), t => Gesture.At(t, 0.7), 1.1),
                     (Scene.MakeConfig(TrailStyleId.Aurora, length: TrailLength.Long), Gesture.Flick, 1.0),
                 })
        {
            var scene = Scene.Run(config, path, end);
            using var narrowed = Raster.Render(scene, end, 1200, 800);
            RibbonLimits.NarrowRows = false;
            using var exhaustive = Raster.Render(scene, end, 1200, 800);
            RibbonLimits.NarrowRows = true;
            if (!SamePixels(narrowed, exhaustive))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SamePixels(Bitmap a, Bitmap b)
    {
        var rect = new Rectangle(0, 0, a.Width, a.Height);
        var da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            return new ReadOnlySpan<byte>((void*)da.Scan0, da.Stride * a.Height)
                .SequenceEqual(new ReadOnlySpan<byte>((void*)db.Scan0, db.Stride * b.Height));
        }
        finally
        {
            a.UnlockBits(da);
            b.UnlockBits(db);
        }
    }

    /// <summary>
    /// The pieces of a stroke now shade only their own share of the plane (slab plus outer wedge)
    /// instead of full capsules. Coverage must not change: alpha may differ by one level (dither
    /// rounding), never more. Colour may differ on the centre line at joints, where the old full caps
    /// tied on the flat-topped body and let the older piece's cap, farther away and so less white,
    /// win; the nearest piece now decides, which evens out the white core. Those are counted only.
    /// </summary>
    private static bool RibbonMatchesReference()
    {
        const int w = 1400, h = 900;
        var fresh = (uint*)NativeMemory.AllocZeroed(w * h, sizeof(uint));
        var reference = (uint*)NativeMemory.AllocZeroed(w * h, sizeof(uint));
        var ok = true;
        try
        {
            var cases = new List<(string Name, RenderConfig Config, Func<double, Point> Path, double End)>();
            foreach (var style in TrailStyles.All)
            {
                cases.Add(($"{style.Name} 标准", Scene.MakeConfig(style.Id), t => Gesture.At(t, 1.2, 700, 450), 1.4));
            }

            cases.Add(("极光 醒目 长", Scene.MakeConfig(TrailStyleId.Aurora, width: TrailWidth.Bold, length: TrailLength.Long),
                t => Gesture.At(t, 1.4, 700, 450), 1.7));
            cases.Add(("幻彩 纤细 短 45%", Scene.MakeConfig(TrailStyleId.Prism, width: TrailWidth.Thin, length: TrailLength.Short,
                opacity: 45), t => Gesture.At(t, 1.2, 700, 450), 1.2));
            cases.Add(("甩动急停", Scene.MakeConfig(TrailStyleId.Neon), t => Gesture.Flick(t), 0.9));
            cases.Add(("慢速小圈", Scene.MakeConfig(TrailStyleId.Ember), t => new Point(
                (int)Math.Round(700 + 40 * Math.Cos(t * 5)), (int)Math.Round(450 + 40 * Math.Sin(t * 5))), 1.3));
            foreach (var (name, config, path, end) in cases)
            {
                var scene = Scene.Run(config, path, end);
                new Span<uint>(fresh, w * h).Clear();
                new Span<uint>(reference, w * h).Clear();
                var canvas = new Canvas();
                canvas.Attach(fresh, w, h);
                canvas.BeginFrame(w, h);
                var referenceCanvas = new ReferenceCanvas();
                referenceCanvas.Attach(reference, w, h);
                referenceCanvas.BeginFrame(w, h);
                var start = Stopwatch.GetTimestamp();
                FrameComposer.Draw(canvas, scene.Model, scene.Particles, scene.Clicks, scene.Config, end, 0, 0);
                var middle = Stopwatch.GetTimestamp();
                FrameComposer.Draw(referenceCanvas, scene.Model, scene.Particles, scene.Clicks, scene.Config, end, 0, 0);
                var stop = Stopwatch.GetTimestamp();

                long covered = 0, alphaDiffering = 0, colourOnly = 0;
                var worstAlpha = 0;
                for (var i = 0; i < w * h; i++)
                {
                    var a = fresh[i];
                    var b = reference[i];
                    if ((a | b) != 0)
                    {
                        covered++;
                    }

                    if (a == b)
                    {
                        continue;
                    }

                    var alphaDiff = Math.Abs((int)(a >> 24) - (int)(b >> 24));
                    worstAlpha = Math.Max(worstAlpha, alphaDiff);
                    if (alphaDiff > 0)
                    {
                        alphaDiffering++;
                    }
                    else
                    {
                        colourOnly++;
                    }
                }

                var passed = worstAlpha <= 1;
                var speedup = (double)(stop - middle) / Math.Max(1, middle - start);
                Console.WriteLine($"      {name,-14} {covered,7} px   alpha: {alphaDiffering,4} off by {worstAlpha}   core colour: {colourOnly,4}   {speedup:0.0}x faster{(passed ? "" : "   <-- FAIL")}");
                ok &= passed;
            }
        }
        finally
        {
            NativeMemory.Free(fresh);
            NativeMemory.Free(reference);
        }

        return ok;
    }

    /// <summary>
    /// Renders the same scenes with <see cref="Canvas"/> and <see cref="GpuCanvas"/> and compares every
    /// channel of every pixel. Float evaluation order differs slightly between the two, so a level of
    /// difference is allowed on a sliver of pixels, never more.
    /// </summary>
    private static bool GpuMatchesCpu()
    {
        var gpu = GpuCanvas.TryCreate(out var error);
        if (gpu is null)
        {
            Console.WriteLine($"      (no GPU: {error})");
            return true;
        }

        const int w = 1200, h = 800;
        var pixels = (uint*)NativeMemory.AllocZeroed(w * h, sizeof(uint));
        var ok = true;
        try
        {
            Console.WriteLine($"      adapter: {gpu.AdapterName}");
            var scenes = TrailStyles.All.Select(s => (s.Name, Config: Scene.MakeConfig(s.Id, width: TrailWidth.Bold)))
                .Append(("星尘 + 涟漪", Scene.MakeConfig(TrailStyleId.Aurora, sparkles: true, opacity: 45)))
                .Append(("纤细 短 30%", Scene.MakeConfig(TrailStyleId.Prism, sparkles: true, width: TrailWidth.Thin,
                    length: TrailLength.Short, opacity: 30)))
                .Concat(Enum.GetValues<ParticleKind>().Skip(1).Select(kind =>
                    ("粒子 " + TrailOptions.Label(kind), Scene.MakeConfig(TrailStyleId.Neon, sparkles: true, particles: kind))))
                .Append(("点击 + 滚轮效果", Scene.MakeConfig(TrailStyleId.Ember) with
                {
                    Style = TrailStyles.Custom([0xFF8A5B, 0x7BD3FF], ColorFlow.Loop).Adjust(1.5f, 1.6f, 0.5f)
                }));
            foreach (var (name, config) in scenes)
            {
                var scene = name.StartsWith("点击")
                    ? Scene.RunWithEffects(config, t => Gesture.At(t, 1.3), 1.4)
                    : Scene.Run(config, t => Gesture.At(t, 1.3), 1.4, clickAt: 1.3);
                using var cpu = Raster.Render(scene, 1.4, w, h);
                gpu.BeginFrame(new PixelBounds(0, 0, w, h));
                FrameComposer.Draw(gpu, scene.Model, scene.Particles, scene.Clicks, scene.Config, 1.4, 0, 0);
                if (gpu.Render() < 0 || gpu.ReadPixels(pixels) < 0)
                {
                    Console.WriteLine($"      {name}: GPU render failed");
                    return false;
                }

                var data = cpu.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                long differing = 0, covered = 0;
                var worst = 0;
                try
                {
                    var reference = (uint*)data.Scan0;
                    for (var i = 0; i < w * h; i++)
                    {
                        var a = reference[i];
                        var b = pixels[i];
                        if ((a | b) != 0)
                        {
                            covered++;
                        }

                        if (a == b)
                        {
                            continue;
                        }

                        differing++;
                        for (var shift = 0; shift < 32; shift += 8)
                        {
                            worst = Math.Max(worst, Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)));
                        }
                    }
                }
                finally
                {
                    cpu.UnlockBits(data);
                }

                var share = covered == 0 ? 0 : 100.0 * differing / covered;
                var passed = worst <= 1 && share < 1.0;
                Console.WriteLine($"      {name,-10} {gpu.InstanceCount,4} quads   {covered,7} px drawn   {differing,5} differ ({share:0.000}%)   max diff {worst}{(passed ? "" : "   <-- FAIL")}");
                ok &= passed;
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
            gpu.Dispose();
        }

        return ok;
    }

    /// <summary>A v2.1 settings file (the enums and switches of the tray menu) maps onto the v3 values.</summary>
    private static bool SettingsMigrate()
    {
        const string V2 = """
            {"Enabled":true,"Style":"Prism","Length":"Short","Width":"Thin","ClickEffects":false,"Sparkles":true,
             "HideInFullscreen":false,"Opacity":45,"GpuAcceleration":true,"WelcomeShown":true}
            """;
        var settings = System.Text.Json.JsonSerializer.Deserialize(V2, SettingsJsonContext.Default.Settings)!;
        settings.Normalize();
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);
        var config = settings.ToRenderConfig();
        var ok = settings.Version == 3 && settings.LengthMs == 600 && settings.WidthPercent == 72 &&
                 settings.LeftClick == ClickEffect.None && settings.RightClick == ClickEffect.None &&
                 settings.Style == TrailStyleId.Prism && settings.Sparkles && !settings.HideInFullscreen &&
                 settings.Opacity == 45 && settings.GpuAcceleration && settings.Length is null &&
                 !json.Contains("\"Length\"") && !json.Contains("\"ClickEffects\"") &&
                 Math.Abs(config.Lifetime - 0.6) < 1e-9 && Math.Abs(config.Width - 0.72f) < 1e-6 &&
                 config.Style == TrailStyles.Get(TrailStyleId.Prism) && config.Particles.Enabled;
        if (!ok)
        {
            Console.WriteLine(json);
        }

        return ok;
    }

    /// <summary>
    /// A fresh v3 configuration renders the same frame as the v2.1 defaults did: the new options all
    /// start at the old behaviour (colour speed, glow and core at 100 %, speed response, taper, 4 Hz filter).
    /// </summary>
    private static bool DefaultsMatchV21()
    {
        var fresh = new Settings();
        fresh.Normalize();
        var config = fresh.ToRenderConfig() with { Particles = ParticleOptions.Off with { Enabled = true } };
        var legacy = Scene.MakeConfig(TrailStyleId.Aurora, sparkles: true, opacity: 60);
        var sameStyle = config.Style == legacy.Style;
        using var a = Raster.Render(Scene.Run(config, t => Gesture.At(t), 1.4, clickAt: 1.3), 1.4, 900, 560);
        using var b = Raster.Render(Scene.Run(legacy, t => Gesture.At(t), 1.4, clickAt: 1.3), 1.4, 900, 560);
        return sameStyle && SamePixels(a, b);
    }

    private static bool Check(string name, bool passed)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}");
        return passed;
    }

    private static bool DirtyTilesMatchFreshRender()
    {
        const int w = 1600, h = 1000;
        var persistent = (uint*)NativeMemory.AllocZeroed(w * h, sizeof(uint));
        var fresh = (uint*)NativeMemory.AllocZeroed(w * h, sizeof(uint));
        try
        {
            var scene = new Scene(Scene.MakeConfig(TrailStyleId.Neon, sparkles: true));
            var canvas = new Canvas();
            canvas.Attach(persistent, w, h);
            float originX = 0, originY = 0;
            var end = 0.0;
            for (var frame = 0; frame < 400; frame++)
            {
                end = frame / 180.0;
                var cursor = Gesture.At(end, 1.4, 800, 500);
                scene.Step(cursor, end);
                if (frame % 37 == 0)
                {
                    scene.Click(cursor, end);
                }

                // Move the virtual window every frame, as the overlay does.
                originX = frame % 50;
                originY = frame % 31;
                canvas.BeginFrame(w - 60, h - 40);
                FrameComposer.Draw(canvas, scene.Model, scene.Particles, scene.Clicks, scene.Config, end, originX, originY);
            }

            var reference = new Canvas();
            reference.Attach(fresh, w, h);
            reference.BeginFrame(w - 60, h - 40);
            FrameComposer.Draw(reference, scene.Model, scene.Particles, scene.Clicks, scene.Config, end, originX, originY);
            return new ReadOnlySpan<uint>(persistent, w * h).SequenceEqual(new ReadOnlySpan<uint>(fresh, w * h));
        }
        finally
        {
            NativeMemory.Free(persistent);
            NativeMemory.Free(fresh);
        }
    }

    private static bool VectorMatchesScalar()
    {
        if (!Canvas.Vectorize)
        {
            Console.WriteLine("      (AVX2 not available, nothing to compare)");
            return true;
        }

        foreach (var style in TrailStyles.All)
        {
            var scene = Scene.Run(Scene.MakeConfig(style.Id, width: TrailWidth.Bold), t => Gesture.At(t, 1.3), 1.4);
            using var vector = Raster.Render(scene, 1.4, 1200, 800);
            Canvas.Vectorize = false;
            using var scalar = Raster.Render(scene, 1.4, 1200, 800);
            Canvas.Vectorize = true;
            for (var y = 0; y < 800; y++)
            {
                for (var x = 0; x < 1200; x++)
                {
                    if (vector.GetPixel(x, y) != scalar.GetPixel(x, y))
                    {
                        Console.WriteLine($"      {style.Name} differs at {x},{y}");
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool PalettesAreContinuous()
    {
        foreach (var style in TrailStyles.All)
        {
            for (var i = 0; i < 512; i++)
            {
                var a = style.ColorAt(i / 512f);
                var b = style.ColorAt((i + 1) % 512 / 512f);
                var delta = Math.Max(Math.Abs((int)((a >> 16) & 255) - (int)((b >> 16) & 255)),
                    Math.Max(Math.Abs((int)((a >> 8) & 255) - (int)((b >> 8) & 255)),
                        Math.Abs((int)(a & 255) - (int)(b & 255))));
                if (delta > 6)
                {
                    Console.WriteLine($"      {style.Name} jumps by {delta} at {i}");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool StrokesDoNotBridge()
    {
        var model = new TrailModel();
        for (var i = 0; i < 40; i++)
        {
            model.AddSample(100 + i * 6, 100, i / 180.0, 1f);
        }

        model.EndStroke(); // e.g. the pointer turned into an I-beam
        for (var i = 0; i < 40; i++)
        {
            model.AddSample(100 + i * 6, 400, (60 + i) / 180.0, 1f);
        }

        for (var i = 1; i < model.Count; i++)
        {
            ref readonly var a = ref model[i - 1];
            ref readonly var b = ref model[i];
            if (!b.Break && Math.Abs(a.Y - b.Y) > 50)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// While settled the engine samples only every 100 ms (Raw Input wakes it); the restart point of
    /// the next stroke must still be stamped "just now", not with that older sample's time.
    /// </summary>
    private static bool ResumedStrokeStartsFresh()
    {
        var model = new TrailModel();
        var t = 0.0;
        for (var i = 0; i < 40; i++, t += 1 / 180.0)
        {
            model.AddSample(100 + i * 6, 100, t, 1f);
        }

        for (var i = 0; i < 20; i++, t += 0.1)
        {
            model.AddSample(100 + 39 * 6, 100, t, 1f);
        }

        model.AddSample(100 + 39 * 6 + 12, 104, t, 1f);
        for (var i = model.Count - 1; i >= 0; i--)
        {
            if (model[i].Break)
            {
                return t - model[i].Time <= 1 / 60.0 + 1e-9;
            }
        }

        return false;
    }

    private static bool SceneEmpties()
    {
        var scene = Scene.Run(Scene.MakeConfig(TrailStyleId.Aurora, sparkles: true), t => Gesture.At(Math.Min(t, 0.8)),
            3.0, clickAt: 0.5);
        return !scene.Model.HasVisibleSegments && scene.Particles.Count == 0 && scene.Clicks.Count == 0 &&
               !FrameComposer.TryGetBounds(scene.Model, scene.Particles, scene.Clicks, scene.Config, out _);
    }
}

internal static unsafe class Bench
{
    public static void Run()
    {
        Console.WriteLine();
        Console.WriteLine("scenario                          fps   avg ms   p95 ms   max ms   CPU ms/s   upload MB/s");
        for (var pass = 0; pass < 2; pass++)
        {
            var print = pass == 1; // first pass warms up the JIT
            RunOld("v1 GDI+  (loop gesture)", 1.0, print);
            foreach (var reference in new[] { true, false })
            {
                var tag = reference ? "v2.0" : "v2.1";
                RunNew($"{tag} 极光  (loop gesture)", Scene.MakeConfig(TrailStyleId.Aurora), 1.0, print, reference);
                RunNew($"{tag} 幻彩 纤细 短 星尘 45%", Scene.MakeConfig(TrailStyleId.Prism, sparkles: true,
                    width: TrailWidth.Thin, length: TrailLength.Short, opacity: 45), 1.0, print, reference);
                RunNew($"{tag} 霓虹 + 星尘粒子", Scene.MakeConfig(TrailStyleId.Neon, sparkles: true), 1.0, print, reference);
                RunNew($"{tag} 极光  (big, fast)", Scene.MakeConfig(TrailStyleId.Aurora), 2.2, print, reference);
                RunNew($"{tag} 极光 粗+长 (big, fast)",
                    Scene.MakeConfig(TrailStyleId.Aurora, width: TrailWidth.Bold, length: TrailLength.Long), 2.2, print,
                    reference);
            }

            Canvas.Vectorize = false;
            RunNew("v2.1 极光  (loop, scalar only)", Scene.MakeConfig(TrailStyleId.Aurora), 1.0, print, false);
            Canvas.Vectorize = System.Runtime.Intrinsics.X86.Avx2.IsSupported;
            RunOld("v1 GDI+  (big, fast)", 2.2, print);
        }
    }

    private static void RunOld(string name, double scale, bool print)
    {
        const double fps = 64;
        using var old = new OldRenderer(2560, 1440);
        var times = new List<double>();
        double uploaded = 0;
        for (var tick = 0; tick < 4 * fps; tick++)
        {
            var now = tick / fps;
            var cursor = Gesture.At(now, scale, 1280, 720);
            var start = Stopwatch.GetTimestamp();
            old.Sample(cursor, now);
            old.RenderFrame(cursor, now);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            uploaded += old.CanvasSize.Width * old.CanvasSize.Height * 4.0;
        }

        if (print)
        {
            Report(name, fps, times, uploaded / 4);
        }
    }

    /// <param name="reference">Draw with the frozen v2.0 rasteriser (full round caps at every joint).</param>
    private static void RunNew(string name, RenderConfig config, double scale, bool print, bool reference)
    {
        const double fps = 180;
        const int screenWidth = 2560, screenHeight = 1440;
        var pixels = (uint*)NativeMemory.AllocZeroed(screenWidth * screenHeight, sizeof(uint));
        try
        {
            var scene = new Scene(config);
            var canvas = new Canvas();
            var oldCanvas = new ReferenceCanvas();
            canvas.Attach(pixels, screenWidth, screenHeight);
            oldCanvas.Attach(pixels, screenWidth, screenHeight);
            IPrimitiveSink sink = reference ? oldCanvas : canvas;
            int windowWidth = 0, windowHeight = 0;
            var times = new List<double>();
            double uploaded = 0;
            for (var frame = 0; frame < 4 * fps; frame++)
            {
                var now = frame / fps;
                var cursor = Gesture.At(now, scale, 1280, 720);
                var start = Stopwatch.GetTimestamp();
                scene.Step(cursor, now);
                if (FrameComposer.TryGetBounds(scene.Model, scene.Particles, scene.Clicks, config, out var b))
                {
                    var left = Math.Max(b.Left, 0);
                    var top = Math.Max(b.Top, 0);
                    var needWidth = Math.Min(b.Right, screenWidth) - left;
                    var needHeight = Math.Min(b.Bottom, screenHeight) - top;
                    windowWidth = TrailEngineMath.PickExtent(windowWidth, needWidth, screenWidth);
                    windowHeight = TrailEngineMath.PickExtent(windowHeight, needHeight, screenHeight);
                    var x = Math.Clamp(left - (windowWidth - needWidth) / 2, 0, screenWidth - windowWidth);
                    var y = Math.Clamp(top - (windowHeight - needHeight) / 2, 0, screenHeight - windowHeight);
                    if (reference)
                    {
                        oldCanvas.BeginFrame(windowWidth, windowHeight);
                    }
                    else
                    {
                        canvas.BeginFrame(windowWidth, windowHeight);
                    }

                    FrameComposer.Draw(sink, scene.Model, scene.Particles, scene.Clicks, config, now, x, y);
                    uploaded += windowWidth * windowHeight * 4.0;
                }

                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            if (print)
            {
                Report(name, fps, times, uploaded / 4);
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    private static void Report(string name, double fps, List<double> times, double bytesPerSecond)
    {
        var sorted = times.Order().ToArray();
        var average = times.Average();
        Console.WriteLine(
            $"{name,-32} {fps,4:0} {average,8:0.000} {sorted[(int)(sorted.Length * 0.95)],8:0.000} {sorted[^1],8:0.000} {average * fps,10:0.0} {bytesPerSecond / 1048576.0,12:0.0}");
    }
}

internal static class IconOutput
{
    private static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    public static void Write(string output)
    {
        var style = TrailStyles.Get(TrailStyleId.Aurora);
        var images = IcoSizes.Select(size => (size, png: EncodePng(IconPainter.RenderIcon(size, style, false), size)))
            .ToArray();
        using (var stream = File.Create(Path.Combine(output, "app.ico")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Length);
            var offset = 6 + 16 * images.Length;
            foreach (var (size, png) in images)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(png.Length);
                writer.Write(offset);
                offset += png.Length;
            }

            foreach (var (_, png) in images)
            {
                writer.Write(png);
            }
        }

        WriteSheet(Path.Combine(output, "icons.png"));
        Console.WriteLine($"icons -> {output}");
    }

    private static void WriteSheet(string path)
    {
        int[] sizes = [16, 20, 24, 32, 48, 256];
        const int rowHeight = 300;
        using var sheet = new Bitmap(1500, rowHeight * 2 + 260, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        for (var row = 0; row < 2; row++)
        {
            var dark = row == 0;
            var area = new Rectangle(0, row * rowHeight, sheet.Width, rowHeight);
            using (var brush = new SolidBrush(dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(238, 238, 238)))
            {
                g.FillRectangle(brush, area);
            }

            var x = 20;
            foreach (var size in sizes)
            {
                foreach (var muted in new[] { false, true })
                {
                    if (muted && size > 32)
                    {
                        continue;
                    }

                    using var icon = ToBitmap(IconPainter.RenderIcon(size, TrailStyles.Get(TrailStyleId.Aurora), muted), size);
                    var zoom = size <= 32 ? 4 : 1;
                    g.DrawImage(icon, new Rectangle(x, area.Y + 20, size * zoom, size * zoom));
                    g.DrawImage(icon, new Rectangle(x, area.Y + 20 + size * zoom + 10, size, size));
                    x += size * zoom + 24;
                }
            }
        }

        // Style swatches and per-style tray icons, as they appear in the menu and notification area.
        var y0 = rowHeight * 2 + 20;
        using (var menu = new SolidBrush(Color.FromArgb(249, 249, 249)))
        {
            g.FillRectangle(menu, 0, rowHeight * 2, sheet.Width / 2, 260);
        }

        using (var menuDark = new SolidBrush(Color.FromArgb(44, 44, 44)))
        {
            g.FillRectangle(menuDark, sheet.Width / 2, rowHeight * 2, sheet.Width / 2, 260);
        }

        for (var i = 0; i < TrailStyles.All.Length; i++)
        {
            var style = TrailStyles.All[i];
            for (var half = 0; half < 2; half++)
            {
                using var swatch = ToBitmap(IconPainter.RenderSwatch(16, style, half == 1), 16, premultiplied: true);
                using var tray = ToBitmap(IconPainter.RenderIcon(24, style, false), 24);
                var left = half * sheet.Width / 2 + 20 + i * 140;
                g.DrawImage(swatch, new Rectangle(left, y0, 64, 64));
                g.DrawImage(swatch, new Rectangle(left, y0 + 80, 16, 16));
                g.DrawImage(tray, new Rectangle(left + 30, y0 + 76, 24, 24));
                g.DrawImage(tray, new Rectangle(left, y0 + 120, 96, 96));
            }
        }

        sheet.Save(path, ImageFormat.Png);
    }

    private static byte[] EncodePng(byte[] bgra, int size)
    {
        using var bitmap = ToBitmap(bgra, size);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static Bitmap ToBitmap(byte[] bgra, int size, bool premultiplied = false)
    {
        var bitmap = new Bitmap(size, size, premultiplied ? PixelFormat.Format32bppPArgb : PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        bitmap.UnlockBits(data);
        return bitmap;
    }
}
