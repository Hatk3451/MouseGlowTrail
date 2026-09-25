using System.Runtime.InteropServices;
using MouseGlowTrail.Ui;

namespace MouseGlowTrail;

/// <summary>What the preview acts out.</summary>
internal enum PreviewScenario
{
    /// <summary>A loose figure of eight with a pause, so the tail can be seen fading.</summary>
    Gesture,

    /// <summary>Visits three spots and clicks left, right and middle, then turns the wheel.</summary>
    Clicks,

    /// <summary>A slower loop, so the ribbon's origin relative to the pointer is easy to see.</summary>
    Origin
}

/// <summary>
/// The control panel's live preview: the real trail renderer (CPU), driven by a scripted pointer and
/// drawn with the user's actual pointer image on a dark or light stage.
/// </summary>
internal sealed unsafe class PreviewStage : IDisposable
{
    private readonly TrailModel _model = new();
    private readonly Particles _particles = new();
    private readonly ClickEffects _clicks = new();
    private readonly Canvas _canvas = new();
    private uint* _pixels;
    private int _width;
    private int _height;
    private void* _bitmap;
    private int _bitmapGeneration = -1;
    private PixelBounds _uploaded;
    private bool _uploadedAny;
    private double _time;
    private double _previousScript = -1;
    private PreviewScenario _scenario = PreviewScenario.Gesture;
    private float _scale = 1f;
    private float _pointerX;
    private float _pointerY;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;
    private string? _caption;
    private double _captionTime = -10;

    /// <summary>The pointer drawn on the stage (shared with the panel's other previews).</summary>
    public PointerImage? Pointer { get; set; }

    /// <summary>Light stage instead of the default dark one.</summary>
    public bool LightStage { get; set; }

    public PreviewScenario Scenario
    {
        get => _scenario;
        set
        {
            if (_scenario != value)
            {
                _scenario = value;
                Restart();
            }
        }
    }

    public void Restart()
    {
        _time = 0;
        _previousScript = -1;
        _model.Reset();
        _particles.Clear();
        _clicks.Clear();
        _caption = null;
    }

    /// <summary>Advances the scene by <paramref name="dt"/> seconds for a stage of the given size (DIPs).</summary>
    public void Update(double dt, RenderConfig config, float widthDips, float heightDips, float scale)
    {
        _scale = scale;
        EnsureBuffer((int)MathF.Ceiling(widthDips * scale), (int)MathF.Ceiling(heightDips * scale));
        // Sub-steps keep the motion as fine-grained as the real overlay at high refresh rates.
        var steps = Math.Clamp((int)Math.Ceiling(dt / (1.0 / 240)), 1, 12);
        for (var i = 1; i <= steps; i++)
        {
            Step(_time + dt * i / steps, config, widthDips, heightDips);
        }

        _time += dt;
        _particles.Update((float)Math.Clamp(dt, 0, 0.05));
        _clicks.Expire(_time);
        _model.Expire(_time, config.Lifetime);
    }

    private void Step(double time, RenderConfig config, float width, float height)
    {
        _model.CycleLength = config.Style.CycleLength;
        _model.SmoothingCutoff = config.SmoothingCutoff;
        _model.SpeedResponse = config.SpeedResponse;
        _particles.Options = config.Particles;
        _model.Spawner = config.Particles.Enabled ? _particles : null;

        var (x, y, left, right, middle, wheel) = Script(time, width, height);
        _pointerX = x;
        _pointerY = y;
        var (originX, originY) = config.Origin switch
        {
            TrailOrigin.Tip => (0f, 0f),
            TrailOrigin.Center => CursorGeometry.CenterOffset(PointerImage.Arrow),
            TrailOrigin.Custom => (config.OffsetX, config.OffsetY),
            _ => (8f, 14f)
        };
        var cursorScale = _scale * PointerImage.CursorBaseScale();
        _model.AddSample((x + originX * cursorScale / _scale) * _scale, (y + originY * cursorScale / _scale) * _scale, time,
            _scale);

        var effectScale = _scale * config.EffectSize;
        var px = x * _scale;
        var py = y * _scale;
        if (left && !_leftDown)
        {
            EffectSpawner.Click(_clicks, _particles, config.LeftClick, px, py, time, _model.Phase, effectScale, config.LeftClickColor);
            Say(config.LeftClick == ClickEffect.None ? Loc.T("左键单击 · 无效果", "Left click · no effect") : Loc.T("左键单击 · ", "Left click · ") + TrailOptions.Label(config.LeftClick), time);
        }

        if (right && !_rightDown)
        {
            EffectSpawner.Click(_clicks, _particles, config.RightClick, px, py, time, _model.Phase, effectScale, config.RightClickColor);
            Say(config.RightClick == ClickEffect.None ? Loc.T("右键单击 · 无效果", "Right click · no effect") : Loc.T("右键单击 · ", "Right click · ") + TrailOptions.Label(config.RightClick), time);
        }

        if (middle && !_middleDown)
        {
            EffectSpawner.Click(_clicks, _particles, config.MiddleClick, px, py, time, _model.Phase, effectScale,
                config.MiddleClickColor);
            Say(config.MiddleClick == ClickEffect.None ? Loc.T("中键单击 · 无效果", "Middle click · no effect") : Loc.T("中键单击 · ", "Middle click · ") + TrailOptions.Label(config.MiddleClick), time);
        }

        if (wheel != 0)
        {
            EffectSpawner.Wheel(_clicks, _particles, config.Wheel, px, py, 0f, -wheel, time, _model.Phase, effectScale,
                config.WheelColor);
            Say(config.Wheel == WheelEffect.None ? Loc.T("滚动滚轮 · 无效果", "Scroll · no effect") : Loc.T("滚动滚轮 · ", "Scroll · ") + TrailOptions.Label(config.Wheel), time);
        }

        _leftDown = left;
        _rightDown = right;
        _middleDown = middle;
    }

    private void Say(string text, double time)
    {
        _caption = text;
        _captionTime = time;
    }

    /// <summary>Pointer position (DIPs) and button / wheel state at a moment of the script.</summary>
    private (float X, float Y, bool Left, bool Right, bool Middle, int Wheel) Script(double time, float width, float height)
    {
        switch (_scenario)
        {
            case PreviewScenario.Clicks:
            {
                const double Period = 5.6;
                var t = time % Period;
                (float X, float Y)[] spots =
                [
                    (0.24f, 0.56f), (0.5f, 0.42f), (0.76f, 0.56f), (0.5f, 0.58f)
                ];
                // Legs: move 0.55 s, rest 0.85 s at each spot.
                var leg = (int)(t / 1.4);
                var within = t - leg * 1.4;
                var from = spots[(leg + spots.Length - 1) % spots.Length];
                var to = spots[leg % spots.Length];
                var u = (float)Math.Clamp(within / 0.55, 0, 1);
                u = u * u * (3 - 2 * u);
                var arc = MathF.Sin(u * MathF.PI) * 0.08f;
                var x = (from.X + (to.X - from.X) * u) * width;
                var y = (from.Y + (to.Y - from.Y) * u - arc) * height;
                var pressing = within is > 0.7 and < 0.82;
                var wheel = 0;
                var cycleStart = Math.Floor(time / Period) * Period;
                foreach (var (at, direction) in WheelEvents)
                {
                    // Three notches down, then two up, while resting at the last spot.
                    var eventTime = cycleStart + 3 * 1.4 + at;
                    if (_previousScript < eventTime && time >= eventTime)
                    {
                        wheel = direction;
                    }
                }

                _previousScript = time;
                return (x, y, pressing && leg == 0, pressing && leg == 1, pressing && leg == 2, wheel);
            }

            case PreviewScenario.Origin:
            {
                const double Period = 4.4;
                var t = time % Period;
                var moving = Math.Min(t / 3.4, 1.0);
                var angle = 2 * Math.PI * (moving - 0.08 * Math.Sin(2 * Math.PI * moving));
                return ((float)(width * 0.5 + width * 0.26 * Math.Cos(angle)),
                    (float)(height * 0.5 + height * 0.27 * Math.Sin(angle)), false, false, false, 0);
            }

            default:
            {
                const double Period = 4.0;
                var t = time % Period;
                var moving = Math.Min(t / 3.1, 1.0);
                // Ease in and out, with a little extra speed through the middle of the loop.
                var u = moving * moving * (3 - 2 * moving);
                var angle = 2 * Math.PI * u;
                return ((float)(width * 0.5 + width * 0.34 * Math.Sin(angle)),
                    (float)(height * 0.52 + height * 0.3 * Math.Sin(2 * angle)), false, false, false, 0);
            }
        }
    }

    private void EnsureBuffer(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _width && height == _height && _pixels != null)
        {
            return;
        }

        FreeBuffer();
        _width = width;
        _height = height;
        _pixels = (uint*)NativeMemory.AllocZeroed((nuint)(width * height), sizeof(uint));
        _canvas.Attach(_pixels, width, height);
        _uploadedAny = false;
    }

    /// <summary>Draws the stage into <paramref name="rect"/> (DIPs) with rounded corners.</summary>
    public void Draw(Gfx gfx, Theme theme, RectF rect, RenderConfig config)
    {
        if (_pixels == null)
        {
            return;
        }

        // Backdrop: a soft vertical gradient with a faint glow in the middle.
        if (LightStage)
        {
            gfx.FillGradient(rect, 8f, [(0f, 0xFFF7F8FBu), (1f, 0xFFE9ECF2u)], true);
        }
        else
        {
            gfx.FillGradient(rect, 8f, [(0f, theme.StageTop), (1f, theme.StageBottom)], true);
            gfx.FillRadial(rect, 8f, rect.CenterX, rect.Y + rect.H * 0.45f, rect.W * 0.55f, rect.H * 0.9f, 0x14A0B4FFu, 0x00A0B4FFu);
        }

        // Render the trail and upload only what changed since the last frame.
        _canvas.BeginFrame(_width, _height);
        var hasContent = FrameComposer.TryGetBounds(_model, _particles, _clicks, config, out var bounds);
        if (hasContent)
        {
            FrameComposer.Draw(_canvas, _model, _particles, _clicks, config, _time, 0, 0);
        }

        if (_bitmap == null || _bitmapGeneration != gfx.Generation)
        {
            D2D.Release(ref _bitmap);
            _bitmap = gfx.CreateBitmap(_width, _height);
            _bitmapGeneration = gfx.Generation;
            _uploadedAny = false;
        }

        if (_bitmap == null)
        {
            return;
        }

        var full = new PixelBounds(0, 0, _width, _height);
        var dirty = hasContent ? bounds.Intersect(full) : default;
        if (_uploadedAny)
        {
            dirty = dirty.Width > 0 && dirty.Height > 0 ? dirty.Union(_uploaded) : _uploaded;
        }
        else
        {
            dirty = full;
        }

        dirty = dirty.Intersect(full);
        if (dirty.Width > 0 && dirty.Height > 0)
        {
            var target = new D2D_RECT_U
            {
                Left = (uint)dirty.Left, Top = (uint)dirty.Top, Right = (uint)dirty.Right, Bottom = (uint)dirty.Bottom
            };
            D2D.CopyFromMemory(_bitmap, &target, _pixels + (long)dirty.Top * _width + dirty.Left, (uint)(_width * 4));
        }

        _uploaded = hasContent ? bounds.Intersect(full) : default;
        _uploadedAny = true;
        gfx.FillBitmap(rect, 8f, _bitmap, rect.X, rect.Y);

        Pointer?.Draw(gfx, rect.X + _pointerX, rect.Y + _pointerY);

        // What just happened, for the click scenario.
        var captionAge = _time - _captionTime;
        if (_caption is not null && captionAge < 1.3)
        {
            var alpha = (float)Math.Clamp(Math.Min(captionAge * 8, (1.3 - captionAge) * 4), 0, 1);
            var (w, h) = gfx.Measure(_caption, TextStyle.Caption);
            var chip = new RectF(rect.CenterX - w * 0.5f - 12f, rect.Bottom - h - 22f, w + 24f, h + 10f);
            gfx.Fill(chip, Theme.Fade(LightStage ? 0xD9FFFFFFu : 0xB3000000u, alpha), chip.H * 0.5f);
            gfx.Stroke(chip, Theme.Fade(LightStage ? 0x14000000u : 0x1FFFFFFFu, alpha), 1f, chip.H * 0.5f);
            gfx.Text(_caption, TextStyle.Caption, chip.X + 12f, chip.Y + 5f, Theme.Fade(LightStage ? 0xE4000000u : 0xFFFFFFFFu, alpha));
        }
    }

    private static readonly (double At, int Direction)[] WheelEvents =
        [(0.62, -1), (0.74, -1), (0.86, -1), (1.08, 1), (1.2, 1)];

    private void FreeBuffer()
    {
        _canvas.Detach();
        if (_pixels != null)
        {
            NativeMemory.Free(_pixels);
            _pixels = null;
        }

        _width = _height = 0;
    }

    public void ReleaseDeviceResources() => D2D.Release(ref _bitmap);

    public void Dispose()
    {
        ReleaseDeviceResources();
        FreeBuffer();
    }
}
