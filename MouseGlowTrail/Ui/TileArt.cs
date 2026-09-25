using System.Runtime.InteropServices;

namespace MouseGlowTrail.Ui;

/// <summary>
/// Small still images drawn with the real trail renderer (style swatches, particle and origin
/// choices), cached as Direct2D bitmaps until the look, the scaling or the device changes.
/// </summary>
internal sealed unsafe class TileArt : IDisposable
{
    private readonly Dictionary<string, IntPtr> _bitmaps = [];
    private int _generation = -1;
    private float _dpi;

    /// <summary>A cached image for <paramref name="key"/>, rendered on first use.</summary>
    public void* Get(Gfx gfx, string key, float widthDips, float heightDips, Action<Scene> script, RenderConfig config)
    {
        if (_generation != gfx.Generation || _dpi != gfx.Dpi)
        {
            Clear();
            _generation = gfx.Generation;
            _dpi = gfx.Dpi;
        }

        if (_bitmaps.TryGetValue(key, out var existing))
        {
            return (void*)existing;
        }

        var width = Math.Max(1, (int)MathF.Ceiling(widthDips * gfx.Scale));
        var height = Math.Max(1, (int)MathF.Ceiling(heightDips * gfx.Scale));
        var scene = new Scene(config, gfx.Scale, widthDips, heightDips);
        script(scene);
        var pixels = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
        void* bitmap = null;
        try
        {
            var canvas = new Canvas();
            canvas.Attach(pixels, width, height);
            canvas.BeginFrame(width, height);
            FrameComposer.Draw(canvas, scene.Model, scene.Particles, scene.Clicks, config, scene.Time, 0, 0);
            canvas.Detach();
            bitmap = gfx.CreateBitmap(width, height);
            if (bitmap != null)
            {
                var rect = new D2D_RECT_U { Right = (uint)width, Bottom = (uint)height };
                D2D.CopyFromMemory(bitmap, &rect, pixels, (uint)(width * 4));
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }

        if (_bitmaps.Count > 64)
        {
            Clear();
        }

        _bitmaps[key] = (IntPtr)bitmap;
        return bitmap;
    }

    public void Clear()
    {
        foreach (var bitmap in _bitmaps.Values)
        {
            var handle = (void*)bitmap;
            D2D.Release(ref handle);
        }

        _bitmaps.Clear();
    }

    public void Dispose() => Clear();

    /// <summary>A scripted scene in DIPs; positions are scaled to pixels internally.</summary>
    internal sealed class Scene
    {
        private readonly float _scale;

        public Scene(RenderConfig config, float scale, float width, float height)
        {
            _scale = scale;
            Width = width;
            Height = height;
            Config = config;
            Model.CycleLength = config.Style.CycleLength;
            Model.SmoothingCutoff = config.SmoothingCutoff;
            Particles.Options = config.Particles;
            Model.Spawner = config.Particles.Enabled ? Particles : null;
        }

        public RenderConfig Config { get; }
        public TrailModel Model { get; } = new();
        public Particles Particles { get; } = new();
        public ClickEffects Clicks { get; } = new();
        public float Width { get; }
        public float Height { get; }
        public double Time { get; private set; }

        /// <summary>Moves the pointer along a path (t from 0 to 1) over <paramref name="duration"/> seconds.</summary>
        public void Stroke(Func<float, (float X, float Y)> path, double duration, float offsetX = 0f, float offsetY = 0f)
        {
            var steps = (int)Math.Ceiling(duration * 240);
            var previous = Time;
            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                var (x, y) = path(t);
                Time = previous + duration * t;
                Model.AddSample((x + offsetX) * _scale, (y + offsetY) * _scale, Time, _scale);
                Particles.Update(1f / 240);
            }
        }

        /// <summary>Lets time pass with the pointer at rest.</summary>
        public void Wait(double seconds)
        {
            var steps = (int)Math.Ceiling(seconds * 240);
            for (var i = 0; i < steps; i++)
            {
                Particles.Update(1f / 240);
            }

            Time += seconds;
            Model.Expire(Time, Config.Lifetime);
        }
    }
}
