using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MouseGlowTrail.Bench;

/// <summary>
/// Frame sequences for the README animations, drawn with the real renderer and the system arrow
/// pointer on a dark backdrop. make-gifs.ps1 turns them into GIFs with ffmpeg.
/// </summary>
internal static unsafe class Demo
{
    private const int Fps = 30;

    public static void Write(string output)
    {
        Hero(Path.Combine(output, "hero"));
        ParticleGrid(Path.Combine(output, "particles"));
        EffectGrid(Path.Combine(output, "effects"));
        Console.WriteLine($"demo frames -> {output}");
    }

    /// <summary>A handwriting-like gesture that cycles through the five styles, with stardust and a few clicks.</summary>
    private static void Hero(string folder)
    {
        const int width = 960, height = 420;
        const double duration = 10.0;
        Directory.CreateDirectory(folder);
        var styles = TrailStyles.All;
        var configs = styles.Select(style => Scene.MakeConfig(style.Id, sparkles: true, opacity: 90) with
        {
            Particles = new ParticleOptions(true, ParticleKind.Stardust, 1.3f, 1.1f, 1f, EffectColor.FollowTrail)
        }).ToArray();
        var scene = new Scene(configs[0]);
        var frame = 0;
        var clicks = new[] { 1.4, 3.7, 6.1, 8.4 };
        var clicked = 0;
        for (var tick = 0; ; tick++)
        {
            var t = tick / 180.0;
            var index = Math.Min(styles.Length - 1, (int)(t / (duration / styles.Length)));
            if (scene.Config != configs[index])
            {
                // Same trail, new look: carry the geometry over into a scene with the next style.
                scene = scene.WithConfig(configs[index]);
            }

            var point = Gesture.At(t * 0.9, 0.92, width / 2.0, height / 2.0 + 6);
            scene.Step(point, t);
            if (clicked < clicks.Length && t >= clicks[clicked])
            {
                EffectSpawner.Click(scene.Clicks, scene.Particles, clicked % 2 == 0 ? ClickEffect.Burst : ClickEffect.DoubleRipple,
                    point.X, point.Y, t, scene.Model.Phase, 1.2f, 0);
                clicked++;
            }

            if (tick % (180 / Fps) == 0)
            {
                using var bitmap = Compose(scene, t, width, height, [(point, Bilingual(() => styles[index].Name))]);
                bitmap.Save(Path.Combine(folder, $"f{frame++:D4}.png"), ImageFormat.Png);
            }

            if (t >= duration)
            {
                break;
            }
        }
    }

    /// <summary>The eight particle kinds side by side, each on its own looping gesture.</summary>
    private static void ParticleGrid(string folder)
    {
        var kinds = Enum.GetValues<ParticleKind>();
        Grid(folder, kinds.Length, (i, cellWidth, cellHeight) =>
        {
            var config = Scene.MakeConfig(TrailStyleId.Aurora, sparkles: true, opacity: 95) with
            {
                Particles = new ParticleOptions(true, kinds[i], 1.6f, 1.1f, 1f, EffectColor.FollowTrail),
                Width = 0.9f
            };
            return (config, t => Loop(t, cellWidth, cellHeight), null, Label(kinds[i]));
        });
    }

    /// <summary>The six click effects and two wheel effects, each played at a resting pointer.</summary>
    private static void EffectGrid(string folder)
    {
        var clicks = Enum.GetValues<ClickEffect>().Skip(1).ToArray();
        var wheels = new[] { WheelEffect.Chevrons, WheelEffect.Stream };
        Grid(folder, clicks.Length + wheels.Length, (i, cellWidth, cellHeight) =>
        {
            var config = Scene.MakeConfig(TrailStyleId.Neon, opacity: 95);
            Func<double, Point> path = t =>
            {
                // Drift to the middle, rest while the effect plays, drift on.
                var phase = t % 2.0;
                var u = Math.Clamp((phase - 0.1) / 0.5, 0, 1);
                u = u * u * (3 - 2 * u);
                return new Point((int)(cellWidth * (0.28 + 0.26 * u)), (int)(cellHeight * (0.66 - 0.16 * u)));
            };
            Action<Scene, double, double, Point> fire = i < clicks.Length
                ? (scene, previous, t, point) =>
                {
                    if (previous % 2.0 < 0.75 && t % 2.0 >= 0.75)
                    {
                        EffectSpawner.Click(scene.Clicks, scene.Particles, clicks[i], point.X, point.Y, t,
                            scene.Model.Phase, 1.3f, 0);
                    }
                }
                : (scene, previous, t, point) =>
                {
                    foreach (var at in new[] { 0.7, 0.85, 1.0 })
                    {
                        if (previous % 2.0 < at && t % 2.0 >= at)
                        {
                            EffectSpawner.Wheel(scene.Clicks, scene.Particles, wheels[i - clicks.Length], point.X, point.Y, 0f,
                                -1f, t, scene.Model.Phase, 1.3f, 0);
                        }
                    }
                };
            var label = i < clicks.Length ? Label(clicks[i]) : Label(wheels[i - clicks.Length]);
            return (config, path, fire, label);
        });
    }

    private static Point Loop(double t, int width, int height)
    {
        var angle = 2 * Math.PI * (t / 2.0);
        return new Point((int)(width * 0.5 + width * 0.3 * Math.Sin(angle)),
            (int)(height * 0.54 + height * 0.26 * Math.Sin(2 * angle)));
    }

    private delegate (RenderConfig Config, Func<double, Point> Path, Action<Scene, double, double, Point>? Fire, string Label)
        CellFactory(int index, int width, int height);

    /// <summary>A 4 x 2 grid of independent scenes, looped over two seconds after a warm-up.</summary>
    private static void Grid(string folder, int count, CellFactory factory)
    {
        const int columns = 4, cellWidth = 240, cellHeight = 170;
        var rows = (count + columns - 1) / columns;
        Directory.CreateDirectory(folder);
        var cells = Enumerable.Range(0, count).Select(i => factory(i, cellWidth, cellHeight)).ToArray();
        var scenes = cells.Select(cell => new Scene(cell.Config)).ToArray();
        var frame = 0;
        const double warmUp = 2.0, loop = 2.0;
        double previous = -1;
        for (var tick = 0; ; tick++)
        {
            var t = tick / 180.0;
            var points = new Point[count];
            for (var i = 0; i < count; i++)
            {
                points[i] = cells[i].Path(t);
                scenes[i].Step(points[i], t);
                cells[i].Fire?.Invoke(scenes[i], previous, t, points[i]);
            }

            previous = t;
            if (t >= warmUp && tick % (180 / Fps) == 0)
            {
                using var sheet = new Bitmap(cellWidth * columns, cellHeight * rows, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(sheet))
                {
                    for (var i = 0; i < count; i++)
                    {
                        using var cell = Compose(scenes[i], t, cellWidth, cellHeight, [(points[i], cells[i].Label)]);
                        g.DrawImage(cell, i % columns * cellWidth, i / columns * cellHeight);
                    }

                    using var pen = new Pen(Color.FromArgb(24, 255, 255, 255));
                    for (var c = 1; c < columns; c++)
                    {
                        g.DrawLine(pen, c * cellWidth, 0, c * cellWidth, sheet.Height);
                    }

                    for (var r = 1; r < rows; r++)
                    {
                        g.DrawLine(pen, 0, r * cellHeight, sheet.Width, r * cellHeight);
                    }
                }

                sheet.Save(Path.Combine(folder, $"f{frame++:D4}.png"), ImageFormat.Png);
            }

            if (t >= warmUp + loop - 1.0 / Fps)
            {
                break;
            }
        }
    }

    /// <summary>Backdrop, trail layer, pointer and a caption.</summary>
    private static Bitmap Compose(Scene scene, double now, int width, int height, (Point Pointer, string Label)[] overlays)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        var area = new Rectangle(0, 0, width, height);
        using (var background = new LinearGradientBrush(area, Color.FromArgb(27, 28, 40), Color.FromArgb(12, 13, 19), 90f))
        {
            g.FillRectangle(background, area);
        }

        using (var layer = Raster.Render(scene, now, width, height))
        {
            g.DrawImage(layer, 0, 0);
        }

        foreach (var (pointer, label) in overlays)
        {
            DrawArrow(g, pointer.X, pointer.Y);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI Variable Text", width < 400 ? 9.5f : 12f);
            using var brush = new SolidBrush(Color.FromArgb(185, 255, 255, 255));
            g.DrawString(label, font, brush, 12, height - (width < 400 ? 26 : 34));
        }

        return bitmap;
    }

    private static string Label(ParticleKind kind) => Bilingual(() => TrailOptions.Label(kind));

    private static string Label(ClickEffect effect) => Bilingual(() => TrailOptions.Label(effect));

    private static string Label(WheelEffect effect) =>
        Bilingual(() => Loc.T("滚轮 · ", "Wheel · ") + TrailOptions.Label(effect));

    private static string Bilingual(Func<string> text)
    {
        Loc.Apply(UiLanguage.Chinese);
        var chinese = text();
        Loc.Apply(UiLanguage.English);
        var english = text();
        return $"{chinese}  {english}";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int width, int height, uint step,
        IntPtr brush, uint flags);

    private static readonly IntPtr Arrow = LoadCursorW(0, 32512);

    private static Bitmap? s_arrow;

    /// <summary>The system arrow with its hotspot (top-left for the standard arrow) at (x, y).</summary>
    private static void DrawArrow(Graphics g, int x, int y)
    {
        s_arrow ??= ArrowBitmap();
        g.DrawImage(s_arrow, x, y);
    }

    /// <summary>GDI drops alpha, so the pointer is drawn on black and on white and the alpha recovered from the difference.</summary>
    private static Bitmap ArrowBitmap()
    {
        const int size = 32;
        uint[] Draw(Color background)
        {
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(background);
                var hdc = g.GetHdc();
                DrawIconEx(hdc, 0, 0, Arrow, size, size, 0, 0, 3);
                g.ReleaseHdc(hdc);
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            var pixels = new uint[size * size];
            Marshal.Copy(data.Scan0, (int[])(object)pixels, 0, pixels.Length);
            bitmap.UnlockBits(data);
            return pixels;
        }

        var onBlack = Draw(Color.Black);
        var onWhite = Draw(Color.White);
        var result = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        var target = result.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        var output = (uint*)target.Scan0;
        for (var i = 0; i < size * size; i++)
        {
            var alpha = 255 - Math.Clamp((int)((onWhite[i] >> 8) & 255) - (int)((onBlack[i] >> 8) & 255), 0, 255);
            output[i] = ((uint)alpha << 24) | (onBlack[i] & 0xFFFFFF);
        }

        result.UnlockBits(target);
        return result;
    }
}
