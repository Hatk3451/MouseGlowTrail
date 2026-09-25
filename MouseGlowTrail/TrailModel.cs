namespace MouseGlowTrail;

internal struct TrailVertex
{
    public float X;
    public float Y;
    public double Time;

    /// <summary>Palette position in [0, 1).</summary>
    public float Phase;

    /// <summary>Width multiplier: monitor DPI times the speed response.</summary>
    public float Scale;

    /// <summary>Starts a new stroke; no segment joins it to the previous vertex.</summary>
    public bool Break;
}

/// <summary>
/// Turns raw pointer samples into a flattened, smoothed polyline. Curves run through the midpoints
/// of consecutive samples with the sample itself as the quadratic control point, which is causal:
/// geometry already on screen never gets revised when the next sample arrives.
/// </summary>
internal sealed class TrailModel
{
    private const int Capacity = 8192;
    private const int Mask = Capacity - 1;
    private const float MinimumStep = 1.5f;
    private const double StopDelay = 0.024;
    private const float JumpDistance = 520f;
    private const float FlatnessTolerance = 0.2f;
    private const double RestartLead = 1.0 / 60;

    private readonly TrailVertex[] _vertices = new TrailVertex[Capacity];
    private int _head;
    private int _count;
    private bool _inStroke;
    private bool _finalized;
    private int _known;
    private Sample _previous;
    private Sample _latest;
    private double _phase;
    private double _lastTime;
    private float _speed;
    private float _rawX;
    private float _rawY;
    private float _filteredX;
    private float _filteredY;
    private float _velocityX;
    private float _velocityY;
    private double _filterTime;

    public float CycleLength { get; set; } = 800f;

    /// <summary>One Euro filter minimum cutoff (Hz): lower smooths slow moves more.</summary>
    public float SmoothingCutoff { get; set; } = 4f;

    /// <summary>Width follows speed: finer when careful, slightly bolder when flicked.</summary>
    public bool SpeedResponse { get; set; } = true;

    /// <summary>Receives every new segment, e.g. to emit sparkles along it. Null disables.</summary>
    public Particles? Spawner { get; set; }

    public int Count => _count;

    /// <summary>Time of the last sample that actually moved the trail (drives the frame rate).</summary>
    public double LastMovement { get; private set; } = double.NegativeInfinity;

    public bool HasVisibleSegments => _count >= 2;

    public float Phase => (float)(_phase - Math.Floor(_phase));

    public ref readonly TrailVertex this[int index] => ref _vertices[(_head + index) & Mask];

    public void AddSample(float rawX, float rawY, double time, float dpiScale)
    {
        if (!_inStroke)
        {
            ResetFilter(rawX, rawY, time);
            BeginStroke(rawX, rawY, time, dpiScale);
            _lastTime = time;
            return;
        }

        var jumpX = rawX - _rawX;
        var jumpY = rawY - _rawY;
        if (jumpX * jumpX + jumpY * jumpY > JumpDistance * JumpDistance * dpiScale * dpiScale)
        {
            // A teleport (monitor jump, programmatic move) must not draw a streak across the screen.
            EndStroke();
            ResetFilter(rawX, rawY, time);
            BeginStroke(rawX, rawY, time, dpiScale);
            _lastTime = time;
            return;
        }

        Smooth(rawX, rawY, time, dpiScale, out var x, out var y);
        var dx = x - _latest.X;
        var dy = y - _latest.Y;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < MinimumStep * dpiScale)
        {
            // Resting: once the pointer has settled, close the curve exactly at its position.
            if (!_finalized && time - _latest.Time >= StopDelay)
            {
                CloseCurve();
            }

            _lastTime = time;
            return;
        }

        if (_finalized)
        {
            // The pointer rested here; restart from "just now" so the new piece is not born pre-faded.
            // After a settled spell the previous sample can be up to a slow poll old, so cap the gap.
            _latest = _latest with { Time = Math.Max(_lastTime, time - RestartLead) };
            Push(_latest, isBreak: true);
            _finalized = false;
        }

        LastMovement = time;
        var normalisedDistance = distance / dpiScale;
        var speed = (float)(normalisedDistance / Math.Max(time - _latest.Time, 1e-3));
        _speed += (speed - _speed) * 0.35f;
        _phase += normalisedDistance / CycleLength;
        var next = new Sample(x, y, time, _phase, dpiScale * SpeedScale(_speed));
        if (_known == 1)
        {
            EmitLine(_latest, Sample.Mid(_latest, next));
        }
        else
        {
            EmitQuadratic(Sample.Mid(_previous, _latest), _latest, Sample.Mid(_latest, next));
        }

        _previous = _latest;
        _latest = next;
        _known = 2;
        _lastTime = time;
    }

    public void EndStroke()
    {
        if (!_inStroke)
        {
            return;
        }

        if (!_finalized)
        {
            CloseCurve();
        }

        _inStroke = false;
    }

    public void Expire(double now, double lifetime)
    {
        while (_count > 0)
        {
            if (_count == 1)
            {
                if (now - _vertices[_head].Time >= lifetime)
                {
                    Pop();
                }

                break;
            }

            ref readonly var next = ref _vertices[(_head + 1) & Mask];
            if (!next.Break && now - next.Time < lifetime)
            {
                break;
            }

            Pop();
        }
    }

    public void Reset()
    {
        _head = 0;
        _count = 0;
        _inStroke = false;
        _finalized = false;
        _known = 0;
    }

    public void GetBounds(ref float minX, ref float minY, ref float maxX, ref float maxY, out float maxScale)
    {
        maxScale = 0f;
        for (var i = 0; i < _count; i++)
        {
            ref readonly var vertex = ref _vertices[(_head + i) & Mask];
            minX = MathF.Min(minX, vertex.X);
            minY = MathF.Min(minY, vertex.Y);
            maxX = MathF.Max(maxX, vertex.X);
            maxY = MathF.Max(maxY, vertex.Y);
            maxScale = MathF.Max(maxScale, vertex.Scale);
        }
    }

    private void ResetFilter(float x, float y, double time)
    {
        _rawX = _filteredX = x;
        _rawY = _filteredY = y;
        _velocityX = _velocityY = 0f;
        _filterTime = time;
    }

    /// <summary>
    /// One Euro filter: strong smoothing when slow (removes the integer-pixel staircase of careful
    /// moves), nearly none when fast (so the ribbon keeps up with a flick).
    /// </summary>
    private void Smooth(float rawX, float rawY, double time, float dpiScale, out float x, out float y)
    {
        var minimumCutoff = SmoothingCutoff;
        const float SpeedCoefficient = 0.025f;
        const float VelocityCutoff = 1.2f;
        var dt = (float)(time - _filterTime);
        if (dt > 1e-4f)
        {
            _velocityX += Blend(VelocityCutoff, dt) * ((rawX - _rawX) / dt - _velocityX);
            _velocityY += Blend(VelocityCutoff, dt) * ((rawY - _rawY) / dt - _velocityY);
            var speed = MathF.Sqrt(_velocityX * _velocityX + _velocityY * _velocityY) / dpiScale;
            var blend = Blend(minimumCutoff + SpeedCoefficient * speed, dt);
            _filteredX += blend * (rawX - _filteredX);
            _filteredY += blend * (rawY - _filteredY);
            _rawX = rawX;
            _rawY = rawY;
            _filterTime = time;
        }

        x = _filteredX;
        y = _filteredY;
    }

    private static float Blend(float cutoff, float dt) => 1f / (1f + 1f / (2f * MathF.PI * cutoff * dt));

    private float SpeedScale(float speed)
    {
        if (!SpeedResponse)
        {
            return 1f;
        }

        // Slow, careful moves draw a finer line; quick flicks swell slightly.
        var t = Math.Clamp((speed - 120f) / 2400f, 0f, 1f);
        return 0.84f + 0.3f * t * t * (3f - 2f * t);
    }

    private void BeginStroke(float x, float y, double time, float dpiScale)
    {
        _inStroke = true;
        _finalized = false;
        _known = 1;
        _speed = 0f;
        _latest = new Sample(x, y, time, _phase, dpiScale * SpeedScale(0f));
        Push(_latest, isBreak: true);
    }

    private void CloseCurve()
    {
        if (_known == 2)
        {
            EmitQuadratic(Sample.Mid(_previous, _latest), _latest, _latest);
        }

        _known = 1;
        _finalized = true;
    }

    private void EmitLine(in Sample start, in Sample end)
    {
        EnsureAnchor(start);
        Push(end, isBreak: false);
    }

    private void EmitQuadratic(in Sample start, in Sample control, in Sample end)
    {
        EnsureAnchor(start);
        var ddx = start.X - 2f * control.X + end.X;
        var ddy = start.Y - 2f * control.Y + end.Y;
        // Flattening error of n chords is |a - 2c + b| / (4 n^2).
        var deviation = 0.25f * MathF.Sqrt(ddx * ddx + ddy * ddy);
        var pieces = deviation <= FlatnessTolerance
            ? 1
            : Math.Min(16, (int)MathF.Ceiling(MathF.Sqrt(deviation / FlatnessTolerance)));
        for (var i = 1; i <= pieces; i++)
        {
            var u = (float)i / pieces;
            var v = 1f - u;
            Push(v * v * start.X + 2f * v * u * control.X + u * u * end.X,
                v * v * start.Y + 2f * v * u * control.Y + u * u * end.Y,
                start.Time + (end.Time - start.Time) * u,
                start.Phase + (end.Phase - start.Phase) * u,
                start.Scale + (end.Scale - start.Scale) * u,
                isBreak: false);
        }
    }

    private void EnsureAnchor(in Sample start)
    {
        if (_count == 0)
        {
            Push(start, isBreak: true);
        }
    }

    private void Push(in Sample sample, bool isBreak) =>
        Push(sample.X, sample.Y, sample.Time, sample.Phase, sample.Scale, isBreak);

    private void Push(float x, float y, double time, double phase, float scale, bool isBreak)
    {
        if (_count == Capacity)
        {
            Pop();
        }

        ref var vertex = ref _vertices[(_head + _count) & Mask];
        vertex.X = x;
        vertex.Y = y;
        vertex.Time = time;
        vertex.Phase = (float)(phase - Math.Floor(phase));
        vertex.Scale = scale;
        vertex.Break = isBreak;
        _count++;
        if (!isBreak && _count >= 2 && Spawner is { } spawner)
        {
            spawner.OnSegment(_vertices[(_head + _count - 2) & Mask], vertex);
        }
    }

    private void Pop()
    {
        _head = (_head + 1) & Mask;
        _count--;
    }

    private readonly record struct Sample(float X, float Y, double Time, double Phase, float Scale)
    {
        public static Sample Mid(in Sample a, in Sample b) =>
            new((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f, (a.Time + b.Time) * 0.5, (a.Phase + b.Phase) * 0.5,
                (a.Scale + b.Scale) * 0.5f);
    }
}
