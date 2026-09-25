namespace MouseGlowTrail;

/// <summary>How a particle is drawn.</summary>
internal enum ParticleShape : byte
{
    Dot,
    Glint,
    Heart,
    Star,
    Snowflake,
    Bubble,
    Streak
}

internal struct Particle
{
    public float X;
    public float Y;
    public float VelocityX;
    public float VelocityY;
    public float Age;
    public float Life;
    public float Size;
    public float Phase;

    /// <summary>Twinkle phase (radians).</summary>
    public float Spin;

    public float Angle;
    public float AngularVelocity;
    public float Drag;
    public float Gravity;

    /// <summary>0 takes the trail colour at <see cref="Phase"/>, otherwise 0xFFRRGGBB.</summary>
    public uint Color;

    public ParticleShape Shape;

    /// <summary>How strongly the brightness flickers (0 = steady).</summary>
    public float Twinkle;
}

/// <summary>
/// Everything shed along the ribbon or thrown out by a click: drifting, twinkling motes, glints and
/// little shapes, each with its own drag and gravity.
/// </summary>
internal sealed class Particles
{
    private const int Capacity = 768;
    private const float Spacing = 12f;

    private readonly Particle[] _items = new Particle[Capacity];
    private int _count;
    private float _carry;
    private uint _random = 0x9E3779B9;

    public int Count => _count;

    public ref readonly Particle this[int index] => ref _items[index];

    /// <summary>What the trail sheds; set by the owner before samples are added.</summary>
    public ParticleOptions Options { get; set; } = ParticleOptions.Off with { Enabled = true };

    public void OnSegment(in TrailVertex from, in TrailVertex to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.01f)
        {
            return;
        }

        var options = Options;
        _carry += length * options.Density / (Spacing * SpacingFactor(options.Kind) * to.Scale);
        var inverseLength = 1f / length;
        var tangentX = dx * inverseLength;
        var tangentY = dy * inverseLength;
        var normalX = -tangentY;
        var normalY = tangentX;
        while (_carry >= 1f)
        {
            _carry -= 1f;
            if (_count == Capacity)
            {
                _carry = 0f;
                return;
            }

            var along = Next();
            var side = Next() * 2f - 1f;
            ref var particle = ref _items[_count++];
            particle.X = from.X + dx * along + normalX * side * 4.5f * to.Scale;
            particle.Y = from.Y + dy * along + normalY * side * 4.5f * to.Scale;
            particle.Age = 0f;
            var phase = to.Phase + (Next() - 0.5f) * 0.06f;
            particle.Phase = phase - MathF.Floor(phase);
            particle.Color = options.Color;
            Shed(ref particle, options, to.Scale, side >= 0f ? 1f : -1f, tangentX, tangentY, normalX, normalY);
        }
    }

    /// <summary>Particles thrown outwards from a point (click bursts, the wheel's stream).</summary>
    public void Burst(float x, float y, int count, float speed, float scale, ParticleShape shape, float phase,
        uint color, float directionX = 0f, float directionY = 0f, float spread = MathF.PI, float sizeScale = 1f)
    {
        var baseAngle = MathF.Atan2(directionY, directionX);
        var offset = Next() * MathF.Tau;
        for (var i = 0; i < count && _count < Capacity; i++)
        {
            ref var particle = ref _items[_count++];
            var angle = spread >= MathF.PI
                ? offset + MathF.Tau * (i + 0.3f * (Next() - 0.5f)) / count
                : baseAngle + spread * (Next() * 2f - 1f);
            var velocity = speed * (0.7f + 0.5f * Next()) * scale;
            particle.X = x;
            particle.Y = y;
            particle.VelocityX = MathF.Cos(angle) * velocity;
            particle.VelocityY = MathF.Sin(angle) * velocity;
            particle.Age = 0f;
            particle.Life = 0.45f + 0.3f * Next();
            particle.Phase = phase + (Next() - 0.5f) * 0.08f;
            particle.Phase -= MathF.Floor(particle.Phase);
            particle.Color = color;
            particle.Shape = shape;
            particle.Spin = Next() * MathF.Tau;
            particle.Angle = shape == ParticleShape.Heart ? (Next() - 0.5f) * 0.5f : Next() * MathF.Tau;
            particle.AngularVelocity = (Next() - 0.5f) * 3f;
            particle.Drag = 4.2f;
            particle.Gravity = shape == ParticleShape.Heart ? -30f : 40f;
            particle.Twinkle = shape is ParticleShape.Dot or ParticleShape.Glint ? 0.28f : 0f;
            particle.Size = scale * sizeScale * shape switch
            {
                ParticleShape.Dot => 1.1f + 0.7f * Next(),
                ParticleShape.Glint => 2.2f + 1.2f * Next(),
                ParticleShape.Heart => 6.5f + 2.5f * Next(),
                _ => 2.6f + 1.2f * Next()
            };
        }
    }

    public void Update(float deltaTime)
    {
        for (var i = 0; i < _count;)
        {
            ref var particle = ref _items[i];
            particle.Age += deltaTime;
            if (particle.Age >= particle.Life)
            {
                _items[i] = _items[--_count];
                continue;
            }

            var drag = MathF.Exp(-particle.Drag * deltaTime);
            particle.VelocityX *= drag;
            particle.VelocityY = particle.VelocityY * drag + particle.Gravity * deltaTime;
            particle.X += particle.VelocityX * deltaTime;
            particle.Y += particle.VelocityY * deltaTime;
            particle.Angle += particle.AngularVelocity * deltaTime;
            i++;
        }
    }

    public void Clear()
    {
        _count = 0;
        _carry = 0f;
    }

    /// <summary>Larger shapes are shed more sparsely, so the density slider means the same for all.</summary>
    private static float SpacingFactor(ParticleKind kind) => kind switch
    {
        ParticleKind.Glint => 1.7f,
        ParticleKind.Firefly => 1.9f,
        ParticleKind.Heart => 2.6f,
        ParticleKind.Star => 2.3f,
        ParticleKind.Snowflake => 2.5f,
        ParticleKind.Bubble => 2.2f,
        ParticleKind.Spark => 0.8f,
        _ => 1f
    };

    private void Shed(ref Particle particle, ParticleOptions options, float scale, float direction, float tangentX,
        float tangentY, float normalX, float normalY)
    {
        var size = options.Size;
        var drift = options.Drift;
        particle.Spin = Next() * MathF.Tau;
        particle.Angle = 0f;
        particle.AngularVelocity = 0f;
        particle.Twinkle = 0f;
        switch (options.Kind)
        {
            case ParticleKind.Firefly:
            {
                // Slow, floating motes that pulse gently and rise.
                var speed = (6f + 14f * Next()) * scale * drift;
                particle.VelocityX = normalX * direction * speed + (Next() - 0.5f) * 12f * drift;
                particle.VelocityY = normalY * direction * speed - (8f + 14f * Next()) * drift;
                particle.Life = 0.9f + 0.8f * Next();
                particle.Shape = ParticleShape.Dot;
                particle.Size = (1.4f + 0.9f * Next()) * scale * size;
                particle.Drag = 1.1f;
                particle.Gravity = -10f * drift;
                particle.Twinkle = 0.4f;
                break;
            }

            case ParticleKind.Spark:
            {
                // Hot sparks flung off to the sides that arc down under gravity.
                var speed = (60f + 110f * Next()) * scale * drift;
                particle.VelocityX = normalX * direction * speed - tangentX * speed * 0.4f;
                particle.VelocityY = normalY * direction * speed - tangentY * speed * 0.4f - 40f * Next() * drift;
                particle.Life = 0.28f + 0.3f * Next();
                particle.Shape = ParticleShape.Streak;
                particle.Size = (0.8f + 0.4f * Next()) * scale * size;
                particle.Drag = 1.6f;
                particle.Gravity = 320f * MathF.Max(drift, 0.25f);
                break;
            }

            case ParticleKind.Stardust or ParticleKind.Glint:
            {
                // Dust kicked up in the ribbon's wake: away from it and slightly backwards.
                var speed = (8f + 26f * Next()) * scale * drift;
                particle.VelocityX = normalX * direction * speed - tangentX * speed * 0.35f + (Next() - 0.5f) * 10f * drift;
                particle.VelocityY = normalY * direction * speed - tangentY * speed * 0.35f + (Next() - 0.5f) * 10f * drift;
                particle.Life = 0.45f + 0.55f * Next();
                var glint = options.Kind == ParticleKind.Glint || Next() < 0.14f;
                particle.Shape = glint ? ParticleShape.Glint : ParticleShape.Dot;
                particle.Size = (0.8f + 0.9f * Next()) * scale * size *
                                (options.Kind == ParticleKind.Glint ? 2.2f : glint ? 1.9f : 1f);
                particle.Drag = 2.4f;
                particle.Gravity = 16f * drift;
                particle.Twinkle = 0.28f;
                break;
            }

            default:
            {
                // Little shapes that drift off the ribbon and tumble slowly.
                var speed = (6f + 20f * Next()) * scale * drift;
                particle.VelocityX = normalX * direction * speed - tangentX * speed * 0.3f + (Next() - 0.5f) * 8f * drift;
                particle.VelocityY = normalY * direction * speed - tangentY * speed * 0.3f + (Next() - 0.5f) * 8f * drift;
                particle.Drag = 2f;
                particle.Life = 0.7f + 0.6f * Next();
                switch (options.Kind)
                {
                    case ParticleKind.Heart:
                        particle.Shape = ParticleShape.Heart;
                        particle.Size = (6.4f + 2.6f * Next()) * scale * size;
                        particle.Angle = (Next() - 0.5f) * 0.6f;
                        particle.AngularVelocity = (Next() - 0.5f) * 0.8f;
                        particle.Gravity = -18f * drift;
                        break;
                    case ParticleKind.Star:
                        particle.Shape = ParticleShape.Star;
                        particle.Size = (4.2f + 2.0f * Next()) * scale * size;
                        particle.Angle = Next() * MathF.Tau;
                        particle.AngularVelocity = (Next() - 0.5f) * 5f;
                        particle.Gravity = 22f * drift;
                        break;
                    case ParticleKind.Snowflake:
                        particle.Shape = ParticleShape.Snowflake;
                        particle.Size = (4.8f + 2.4f * Next()) * scale * size;
                        particle.Angle = Next() * MathF.Tau;
                        particle.AngularVelocity = (Next() - 0.5f) * 1.6f;
                        particle.Gravity = 14f * drift;
                        particle.Drag = 2.8f;
                        particle.Life = 0.9f + 0.7f * Next();
                        break;
                    default:
                        particle.Shape = ParticleShape.Bubble;
                        particle.Size = (2.8f + 2.2f * Next()) * scale * size;
                        particle.Gravity = -34f * drift;
                        particle.Drag = 2.4f;
                        break;
                }

                break;
            }
        }
    }

    private float Next()
    {
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return (_random >> 8) * (1f / 16777216f);
    }
}

/// <summary>Short-lived effects at a point: click ripples and flashes, wheel chevrons.</summary>
internal enum PulseKind : byte
{
    Ripple,
    DoubleRipple,
    Glint,
    Shockwave,
    Flash,
    Chevron,
    WheelRipple
}

/// <param name="Color">0 takes the trail colour at <paramref name="Phase"/>, otherwise 0xFFRRGGBB.</param>
/// <param name="DirectionX">Wheel effects: the direction of travel (unit vector).</param>
internal readonly record struct ClickPulse(float X, float Y, double Time, float Phase, float Scale, uint Color = 0,
    PulseKind Kind = PulseKind.Ripple, float DirectionX = 0f, float DirectionY = 0f)
{
    public double Lifetime => Kind switch
    {
        PulseKind.DoubleRipple => 0.7,
        PulseKind.Glint => 0.42,
        PulseKind.Shockwave => 0.55,
        PulseKind.Flash => FrameComposer.ClickDotLifetime,
        PulseKind.Chevron => 0.5,
        PulseKind.WheelRipple => 0.4,
        _ => FrameComposer.ClickRingLifetime
    };
}

internal sealed class ClickEffects
{
    private const int Capacity = 32;

    private readonly ClickPulse[] _items = new ClickPulse[Capacity];
    private int _count;

    public int Count => _count;

    public ref readonly ClickPulse this[int index] => ref _items[index];

    public void Add(float x, float y, double time, float phase, float scale) =>
        Add(new ClickPulse(x, y, time, phase, scale));

    public void Add(in ClickPulse pulse)
    {
        if (_count == Capacity)
        {
            Array.Copy(_items, 1, _items, 0, Capacity - 1);
            _count--;
        }

        _items[_count++] = pulse;
    }

    public void Expire(double now)
    {
        var kept = 0;
        for (var i = 0; i < _count; i++)
        {
            if (now - _items[i].Time < _items[i].Lifetime)
            {
                _items[kept++] = _items[i];
            }
        }

        _count = kept;
    }

    public void Clear() => _count = 0;
}

/// <summary>Turns a button press or a wheel turn into pulses and particle bursts.</summary>
internal static class EffectSpawner
{
    public static void Click(ClickEffects pulses, Particles particles, ClickEffect effect, float x, float y, double now,
        float phase, float scale, uint color)
    {
        switch (effect)
        {
            case ClickEffect.Ripple:
                pulses.Add(new ClickPulse(x, y, now, phase, scale, color));
                break;
            case ClickEffect.DoubleRipple:
                pulses.Add(new ClickPulse(x, y, now, phase, scale, color, PulseKind.DoubleRipple));
                break;
            case ClickEffect.Glint:
                pulses.Add(new ClickPulse(x, y, now, phase, scale, color, PulseKind.Glint));
                break;
            case ClickEffect.Shockwave:
                pulses.Add(new ClickPulse(x, y, now, phase, scale, color, PulseKind.Shockwave));
                break;
            case ClickEffect.Burst:
                pulses.Add(new ClickPulse(x, y, now, phase, scale, color, PulseKind.Flash));
                particles.Burst(x, y, 10, 150f, scale, ParticleShape.Dot, phase, color);
                particles.Burst(x, y, 4, 110f, scale, ParticleShape.Glint, phase, color);
                break;
            case ClickEffect.Hearts:
                pulses.Add(new ClickPulse(x, y, now, phase, scale, color, PulseKind.Flash));
                particles.Burst(x, y, 7, 120f, scale, ParticleShape.Heart, phase, color);
                break;
        }
    }

    /// <summary>One wheel notch (or a burst of fine-grained scrolling) in the given direction.</summary>
    public static void Wheel(ClickEffects pulses, Particles particles, WheelEffect effect, float x, float y,
        float directionX, float directionY, double now, float phase, float scale, uint color)
    {
        switch (effect)
        {
            case WheelEffect.Chevrons:
                pulses.Add(new ClickPulse(x + directionX * 18f * scale, y + directionY * 18f * scale, now, phase, scale,
                    color, PulseKind.Chevron, directionX, directionY));
                break;
            case WheelEffect.Ripple:
                pulses.Add(new ClickPulse(x + directionX * 14f * scale, y + directionY * 14f * scale, now, phase, scale,
                    color, PulseKind.WheelRipple, directionX, directionY));
                break;
            case WheelEffect.Stream:
                particles.Burst(x + directionX * 12f * scale, y + directionY * 12f * scale, 6, 190f, scale,
                    ParticleShape.Dot, phase, color, directionX, directionY, 0.4f, 2f);
                break;
        }
    }
}
