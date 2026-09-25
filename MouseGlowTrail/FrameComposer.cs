namespace MouseGlowTrail;

internal readonly record struct PixelBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public PixelBounds Union(in PixelBounds other) => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public PixelBounds Intersect(in PixelBounds other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top),
        Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
}

/// <summary>
/// The primitives a frame is made of. <see cref="Canvas"/> rasterises them on the CPU;
/// <see cref="GpuCanvas"/> turns each into one instanced quad for the same shading on the GPU.
/// </summary>
internal interface IPrimitiveSink
{
    void Capsule(float ax, float ay, float bx, float by, float radius, float fade, uint rgb, ProfileLut lut);

    /// <summary>One piece of a stroke; see <see cref="RibbonLimits"/> for how joined pieces share the plane.</summary>
    void Ribbon(float ax, float ay, float bx, float by, float radiusA, float radiusB, float fadeA, float fadeB,
        uint rgbA, uint rgbB, ProfileLut lut, bool capStart, bool capEnd, float nextX, float nextY);

    void Ring(float cx, float cy, float radius, float thickness, float fade, uint rgb, float white);

    void Sparkle(float cx, float cy, float size, float fade, uint rgb, float white);

    /// <summary>A small glowing heart, star or snowflake (see <see cref="ShapeField"/>), turned by <paramref name="angle"/>.</summary>
    void Shape(float cx, float cy, float size, float angle, float fade, uint rgb, float white, ParticleShape shape);
}

internal static class TrailEngineMath
{
    /// <summary>
    /// Overlay extent with hysteresis: grow with headroom, shrink only once the content needs less
    /// than half, so the layered window is not reallocated on every frame.
    /// </summary>
    public static int PickExtent(int current, int needed, int limit)
    {
        if (needed > current || needed * 2 < current)
        {
            current = (needed + needed / 4 + 32 + 63) & ~63;
        }

        return Math.Min(current, limit);
    }
}

/// <summary>Draws one frame of the scene; shared by the overlay, the control panel's preview and the offline tools.</summary>
internal static class FrameComposer
{
    /// <summary>Half of the 8 px ribbon width at 100 % scaling (unchanged from v1).</summary>
    public const float BaseHalfWidth = 4f;

    public const double ClickDotLifetime = 0.24;
    public const double ClickRingLifetime = 0.5;

    private const float ClickDotRadius = 5f;
    private const float RingStartRadius = 5f;
    private const float RingEndRadius = 24f;

    /// <summary>Reach of a shape particle in multiples of its size (outline plus glow).</summary>
    public const float ShapeReach = 2f;

    public static bool TryGetBounds(TrailModel model, Particles particles, ClickEffects clicks, RenderConfig config,
        out PixelBounds bounds, TrailModel? preview = null)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        AddRibbonBounds(model, config, ref minX, ref minY, ref maxX, ref maxY);
        if (preview is not null)
        {
            AddRibbonBounds(preview, config, ref minX, ref minY, ref maxX, ref maxY);
        }

        for (var i = 0; i < particles.Count; i++)
        {
            ref readonly var particle = ref particles[i];
            var pad = ParticleReach(particle) + 2f;
            minX = MathF.Min(minX, particle.X - pad);
            minY = MathF.Min(minY, particle.Y - pad);
            maxX = MathF.Max(maxX, particle.X + pad);
            maxY = MathF.Max(maxY, particle.Y + pad);
        }

        for (var i = 0; i < clicks.Count; i++)
        {
            ref readonly var click = ref clicks[i];
            var pad = PulseReach(click.Kind) * click.Scale;
            minX = MathF.Min(minX, click.X - pad);
            minY = MathF.Min(minY, click.Y - pad);
            maxX = MathF.Max(maxX, click.X + pad);
            maxY = MathF.Max(maxY, click.Y + pad);
        }

        if (minX > maxX || minY > maxY)
        {
            bounds = default;
            return false;
        }

        bounds = new PixelBounds((int)MathF.Floor(minX), (int)MathF.Floor(minY),
            (int)MathF.Ceiling(maxX) + 1, (int)MathF.Ceiling(maxY) + 1);
        return true;
    }

    public static void Draw(IPrimitiveSink canvas, TrailModel model, Particles particles, ClickEffects clicks,
        RenderConfig config, double now, float originX, float originY, TrailModel? preview = null)
    {
        DrawRibbon(canvas, model, config, now, originX, originY);
        if (preview is not null)
        {
            DrawRibbon(canvas, preview, config, now, originX, originY);
        }

        DrawParticles(canvas, particles, config.Style, config.Opacity, originX, originY);
        DrawClicks(canvas, clicks, config.Style, config.Opacity, now, originX, originY);
    }

    private static float ParticleReach(in Particle particle) => particle.Shape switch
    {
        ParticleShape.Dot => particle.Size * Profiles.Particle.Extent,
        ParticleShape.Glint => particle.Size * 3.2f,
        ParticleShape.Bubble => particle.Size * (1.25f + 0.28f * 2.6f),
        ParticleShape.Streak => particle.Size * Profiles.Particle.Extent +
                                StreakLength * MathF.Sqrt(particle.VelocityX * particle.VelocityX +
                                                          particle.VelocityY * particle.VelocityY),
        _ => particle.Size * ShapeReach
    };

    /// <summary>Seconds of travel a spark's streak spans.</summary>
    private const float StreakLength = 0.035f;

    private static float PulseReach(PulseKind kind) => kind switch
    {
        PulseKind.DoubleRipple => 40f,
        PulseKind.Glint => 22f + 8f,
        PulseKind.Shockwave => 56f,
        PulseKind.Flash => ClickDotRadius * 2.6f,
        PulseKind.Chevron => 44f,
        PulseKind.WheelRipple => 24f,
        _ => RingEndRadius + 6f
    };

    private static void AddRibbonBounds(TrailModel model, RenderConfig config, ref float minX, ref float minY,
        ref float maxX, ref float maxY)
    {
        if (!model.HasVisibleSegments)
        {
            return;
        }

        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        model.GetBounds(ref left, ref top, ref right, ref bottom, out var maxScale);
        var pad = BaseHalfWidth * config.Width * maxScale * config.Style.Ribbon.Extent + 2f;
        minX = MathF.Min(minX, left - pad);
        minY = MathF.Min(minY, top - pad);
        maxX = MathF.Max(maxX, right + pad);
        maxY = MathF.Max(maxY, bottom + pad);
    }

    private static void DrawRibbon(IPrimitiveSink canvas, TrailModel model, RenderConfig config, double now,
        float originX, float originY)
    {
        var count = model.Count;
        var style = config.Style;
        var profile = style.Ribbon;
        var inverseLifetime = (float)(1.0 / config.Lifetime);
        var baseRadius = BaseHalfWidth * config.Width;
        var opacity = config.Opacity;
        var taper = config.Taper;
        var start = 0;
        var end = -1;
        var joined = false; // a drawn piece of the same stroke ends at `start`
        while (start < count - 1)
        {
            if (model[start + 1].Break)
            {
                start++;
                end = -1;
                joined = false;
                continue;
            }

            if (end < 0)
            {
                end = ExtendRun(model, start, count);
            }

            // The next piece of the stroke, if any, shapes the joint at `end`.
            var hasNext = end < count - 1 && !model[end + 1].Break;
            var nextEnd = hasNext ? ExtendRun(model, end, count) : -1;
            ref readonly var a = ref model[start];
            ref readonly var b = ref model[end];
            var ageA = (float)(now - a.Time) * inverseLifetime;
            var ageB = (float)(now - b.Time) * inverseLifetime;
            var drawn = ageB < 1f;
            if (drawn)
            {
                float ax = a.X - originX, ay = a.Y - originY, bx = b.X - originX, by = b.Y - originY;
                float nextX = 0f, nextY = 0f;
                if (hasNext)
                {
                    // Exactly the chord the next piece will be drawn with.
                    ref readonly var c = ref model[nextEnd];
                    nextX = (c.X - originX) - bx;
                    nextY = (c.Y - originY) - by;
                }

                canvas.Ribbon(ax, ay, bx, by,
                    baseRadius * a.Scale * (taper ? Taper(ageA) : 1f), baseRadius * b.Scale * (taper ? Taper(ageB) : 1f),
                    Fade(ageA) * opacity, Fade(ageB) * opacity,
                    style.ColorAt(a.Phase), style.ColorAt(b.Phase), profile, !joined, !hasNext, nextX, nextY);
            }

            joined = drawn;
            start = end;
            end = nextEnd;
        }
    }

    /// <summary>
    /// Greedily extends a run from <paramref name="start"/> while every skipped vertex stays within a
    /// quarter pixel of the chord, so merging is visually lossless.
    /// </summary>
    private static int ExtendRun(TrailModel model, int start, int count)
    {
        const int MaxVertices = 16;
        const float MaxLength = 42f;
        const float Tolerance = 0.25f;
        ref readonly var anchor = ref model[start];
        var end = start + 1;
        while (end + 1 < count && end + 1 - start <= MaxVertices && !model[end + 1].Break)
        {
            ref readonly var candidate = ref model[end + 1];
            var ex = candidate.X - anchor.X;
            var ey = candidate.Y - anchor.Y;
            var lengthSquared = ex * ex + ey * ey;
            if (lengthSquared > MaxLength * MaxLength || lengthSquared < 1e-6f)
            {
                break;
            }

            var inverseLength = 1f / MathF.Sqrt(lengthSquared);
            var straight = true;
            for (var k = start + 1; k <= end && straight; k++)
            {
                ref readonly var middle = ref model[k];
                var px = middle.X - anchor.X;
                var py = middle.Y - anchor.Y;
                var along = (px * ex + py * ey) / lengthSquared;
                straight = along is >= 0f and <= 1f && MathF.Abs(px * ey - py * ex) * inverseLength <= Tolerance;
            }

            if (!straight)
            {
                break;
            }

            end++;
        }

        return end;
    }

    /// <summary>Comet falloff: the tail both fades and narrows, so it dissolves instead of ending bluntly.</summary>
    private static float Fade(float age) => age >= 1f ? 0f : age <= 0f ? 1f : MathF.Pow(1f - age, 1.2f);

    private static float Taper(float age) =>
        0.22f + 0.78f * (age >= 1f ? 0f : age <= 0f ? 1f : MathF.Pow(1f - age, 0.7f));

    private static void DrawParticles(IPrimitiveSink canvas, Particles particles, TrailStyle style, float opacity,
        float originX, float originY)
    {
        for (var i = 0; i < particles.Count; i++)
        {
            ref readonly var particle = ref particles[i];
            var life = particle.Age / particle.Life;
            var fade = MathF.Pow(1f - life, 1.4f) * MathF.Min(1f, life * 10f) * opacity;
            if (particle.Twinkle > 0f)
            {
                fade *= 1f - particle.Twinkle + particle.Twinkle * MathF.Sin(particle.Age * 17f + particle.Spin);
            }

            var x = particle.X - originX;
            var y = particle.Y - originY;
            var color = EffectColor.Resolve(particle.Color, style, particle.Phase);
            switch (particle.Shape)
            {
                case ParticleShape.Glint:
                    canvas.Sparkle(x, y, particle.Size, fade, color, 0.7f);
                    break;
                case ParticleShape.Dot:
                    canvas.Capsule(x, y, x, y, particle.Size, fade * 0.9f, color, Profiles.Particle);
                    break;
                case ParticleShape.Streak:
                    canvas.Capsule(x, y, x - particle.VelocityX * StreakLength, y - particle.VelocityY * StreakLength,
                        particle.Size, fade, color, Profiles.Particle);
                    break;
                case ParticleShape.Bubble:
                    canvas.Ring(x, y, particle.Size * 1.25f, particle.Size * 0.28f, fade * 0.85f, color, 0.35f);
                    break;
                default:
                    canvas.Shape(x, y, particle.Size, particle.Angle, fade, color, 0.35f, particle.Shape);
                    break;
            }
        }
    }

    private static void DrawClicks(IPrimitiveSink canvas, ClickEffects clicks, TrailStyle style, float opacity, double now,
        float originX, float originY)
    {
        for (var i = 0; i < clicks.Count; i++)
        {
            ref readonly var click = ref clicks[i];
            var age = now - click.Time;
            var progress = (float)(age / click.Lifetime);
            if (progress >= 1f)
            {
                continue;
            }

            var x = click.X - originX;
            var y = click.Y - originY;
            var s = click.Scale;
            var color = EffectColor.Resolve(click.Color, style, click.Phase);
            switch (click.Kind)
            {
                case PulseKind.Ripple:
                    Dot(canvas, x, y, s, age, color, opacity);
                    Ring(canvas, x, y, s, progress, RingEndRadius, color, opacity);
                    break;
                case PulseKind.DoubleRipple:
                    Dot(canvas, x, y, s, age, color, opacity);
                    Ring(canvas, x, y, s, (float)(age / ClickRingLifetime), RingEndRadius, color, opacity);
                    Ring(canvas, x, y, s, (float)((age - 0.16) / (click.Lifetime - 0.16)), 34f, color, opacity * 0.8f);
                    break;
                case PulseKind.Flash:
                    Dot(canvas, x, y, s, age, color, opacity);
                    break;
                case PulseKind.Glint:
                {
                    // A star of tapering rays that flares up, turns a little and shrinks away.
                    var length = (5f + 17f * MathF.Sin(MathF.PI * MathF.Pow(progress, 0.55f))) * s;
                    var fade = MathF.Pow(1f - progress, 1.2f) * opacity;
                    var turn = progress * 0.5f;
                    for (var ray = 0; ray < 8; ray++)
                    {
                        var angle = turn + ray * (MathF.PI / 4f);
                        var reach = ray % 2 == 0 ? length : length * 0.42f;
                        canvas.Ribbon(x, y, x + MathF.Cos(angle) * reach, y + MathF.Sin(angle) * reach,
                            2.6f * s, 0.3f * s, fade, fade * 0.6f, color, color, style.Ribbon, true, true, 0f, 0f);
                    }

                    canvas.Sparkle(x, y, 2.6f * s, fade, color, 0.9f);
                    break;
                }

                case PulseKind.Shockwave:
                {
                    var eased = 1f - MathF.Pow(1f - progress, 4f);
                    var radius = (6f + 38f * eased) * s;
                    var thickness = (4.2f - 2.6f * progress) * s;
                    canvas.Ring(x, y, radius, thickness, 0.85f * MathF.Pow(1f - progress, 1.8f) * opacity, color, 0.3f);
                    if (progress < 0.4f)
                    {
                        canvas.Capsule(x, y, x, y, 7f * s, MathF.Pow(1f - progress / 0.4f, 1.5f) * 0.6f * opacity, color,
                            Profiles.Particle);
                    }

                    break;
                }

                case PulseKind.Chevron:
                {
                    // A chevron pointing the way the page scrolls, gliding on and fading out.
                    var travel = 16f * (1f - MathF.Pow(1f - progress, 2f)) * s;
                    var fade = MathF.Min(1f, progress * 10f) * MathF.Pow(1f - progress, 1.3f) * opacity;
                    var cx = x + click.DirectionX * travel;
                    var cy = y + click.DirectionY * travel;
                    float dx = click.DirectionX, dy = click.DirectionY;
                    var tipX = cx + dx * 4f * s;
                    var tipY = cy + dy * 4f * s;
                    var backX = cx - dx * 3f * s;
                    var backY = cy - dy * 3f * s;
                    var armX = -dy * 7f * s;
                    var armY = dx * 7f * s;
                    var radius = 1.55f * s;
                    canvas.Capsule(tipX, tipY, backX + armX, backY + armY, radius, fade, color, style.Ribbon);
                    canvas.Capsule(tipX, tipY, backX - armX, backY - armY, radius, fade, color, style.Ribbon);
                    break;
                }

                case PulseKind.WheelRipple:
                {
                    var eased = 1f - MathF.Pow(1f - progress, 3f);
                    canvas.Ring(x, y, (3f + 12f * eased) * s, (1.8f - 0.8f * progress) * s,
                        0.75f * MathF.Pow(1f - progress, 1.5f) * opacity, color, 0.2f);
                    break;
                }
            }
        }
    }

    private static void Dot(IPrimitiveSink canvas, float x, float y, float scale, double age, uint color, float opacity)
    {
        if (age < ClickDotLifetime)
        {
            var fade = MathF.Pow(1f - (float)(age / ClickDotLifetime), 1.5f) * opacity;
            canvas.Capsule(x, y, x, y, ClickDotRadius * scale, fade, color, Profiles.ClickDot);
        }
    }

    private static void Ring(IPrimitiveSink canvas, float x, float y, float scale, float progress, float endRadius,
        uint color, float opacity)
    {
        if (progress is <= 0f or >= 1f)
        {
            return;
        }

        var eased = 1f - MathF.Pow(1f - progress, 3f);
        var radius = (RingStartRadius + (endRadius - RingStartRadius) * eased) * scale;
        var thickness = (2.2f - 1.2f * progress) * scale;
        canvas.Ring(x, y, radius, thickness, 0.72f * MathF.Pow(1f - progress, 1.5f) * opacity, color, 0.2f);
    }
}

/// <summary>
/// Signed distance fields of the particle shapes, in units of the particle's size (negative inside),
/// with y pointing up. Trail.hlsl carries the same formulas for the GPU.
/// </summary>
internal static class ShapeField
{
    public static float Distance(ParticleShape shape, float x, float y) => shape switch
    {
        ParticleShape.Heart => Heart(x, y + 0.55f),
        ParticleShape.Star => Star(x, y) - 0.06f,
        _ => Snowflake(x, y)
    };

    /// <summary>A heart with its tip at the origin, about 1.2 wide and 1.1 tall (after Inigo Quilez).</summary>
    public static float Heart(float x, float y)
    {
        x = MathF.Abs(x);
        if (y + x > 1f)
        {
            var dx = x - 0.25f;
            var dy = y - 0.75f;
            return MathF.Sqrt(dx * dx + dy * dy) - 0.35355339f;
        }

        var a = x * x + (y - 1f) * (y - 1f);
        var m = 0.5f * MathF.Max(x + y, 0f);
        var bx = x - m;
        var by = y - m;
        var b = bx * bx + by * by;
        return MathF.Sqrt(MathF.Min(a, b)) * MathF.Sign(x - y);
    }

    /// <summary>A five-pointed star of outer radius 1 and inner ratio 0.5 (after Inigo Quilez).</summary>
    public static float Star(float x, float y)
    {
        const float K1X = 0.809016994f;
        const float K1Y = -0.587785252f;
        const float Inner = 0.5f;
        x = MathF.Abs(x);
        var d = MathF.Max(K1X * x + K1Y * y, 0f);
        x -= 2f * d * K1X;
        y -= 2f * d * K1Y;
        d = MathF.Max(-K1X * x + K1Y * y, 0f);
        x -= 2f * d * -K1X;
        y -= 2f * d * K1Y;
        x = MathF.Abs(x);
        y -= 1f;
        var baX = Inner * -K1Y;
        var baY = Inner * K1X - 1f;
        var h = Math.Clamp((x * baX + y * baY) / (baX * baX + baY * baY), 0f, 1f);
        var ex = x - baX * h;
        var ey = y - baY * h;
        return MathF.Sqrt(ex * ex + ey * ey) * MathF.Sign(y * baX - x * baY);
    }

    /// <summary>Six arms of length 1, each with two pairs of side branches.</summary>
    public static float Snowflake(float x, float y)
    {
        x = MathF.Abs(x);
        y = MathF.Abs(y);
        // Fold into the 0-30 degree sector; the arm then lies along its 30 degree edge.
        var d = -0.8660254f * x + 0.5f * y;
        if (d > 0f)
        {
            x -= 2f * d * -0.8660254f;
            y -= 2f * d * 0.5f;
        }

        d = -0.5f * x + 0.8660254f * y;
        if (d > 0f)
        {
            x -= 2f * d * -0.5f;
            y -= 2f * d * 0.8660254f;
        }

        var arm = Segment(x, y, 0f, 0f, 0.8660254f, 0.5f);
        var branch = Segment(x, y, 0.4763140f, 0.275f, 0.7582216f, 0.1723939f);
        var twig = Segment(x, y, 0.6928203f, 0.4f, 0.8619665f, 0.3384364f);
        return MathF.Min(arm, MathF.Min(branch, twig)) - 0.085f;
    }

    private static float Segment(float px, float py, float ax, float ay, float bx, float by)
    {
        var ex = bx - ax;
        var ey = by - ay;
        var wx = px - ax;
        var wy = py - ay;
        var t = Math.Clamp((wx * ex + wy * ey) / (ex * ex + ey * ey), 0f, 1f);
        var dx = wx - ex * t;
        var dy = wy - ey * t;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
