// GPU port of Canvas.cs. Each instance is one primitive drawn as a quad; the pixel shader evaluates
// the same analytic distance fields and profile tables as the CPU rasteriser. "Highest alpha wins"
// compositing is done by the depth test: every fragment writes depth = alpha and passes only if it
// is strictly greater than what is already there, exactly like the CPU's per-pixel comparison.
//
// Compiled offline for feature level 10_0 by build-shaders.ps1 into Shaders.cs.

Texture2D<float2> Profiles : register(t0); // x = coverage, y = white; one row per ProfileLut

cbuffer Frame : register(b0)
{
    int2 Origin;      // screen pixel mapped to the render target's (0, 0)
    float2 ClipScale; // 2 / viewport size
};

// Instance kinds (Quad1.z).
#define KIND_RIBBON 0
#define KIND_SPARKLE 1
#define KIND_RING 2
#define KIND_SHAPE 3

// Particle shapes (Param1.w), as ParticleShape in Effects.cs.
#define SHAPE_HEART 2
#define SHAPE_STAR 3

struct Instance
{
    float4 Quad0 : QUAD0;   // corner.xy, edge U.xy in screen pixels
    float4 Quad1 : QUAD1;   // edge V.xy, kind, profile row
    float4 Param0 : PARAM0;
    float4 Param1 : PARAM1;
    float4 Param2 : PARAM2;
    float4 Color0 : COLOR0;
    float4 Color1 : COLOR1;
};

struct Varyings
{
    float4 Position : SV_Position;
    nointerpolation float2 Kind : KIND;
    nointerpolation float4 Param0 : PARAM0;
    nointerpolation float4 Param1 : PARAM1;
    nointerpolation float4 Param2 : PARAM2;
    nointerpolation float4 Color0 : COLOR0;
    nointerpolation float4 Color1 : COLOR1;
};

Varyings VS(uint vertex : SV_VertexID, Instance instance)
{
    Varyings output;
    float2 corner = float2(vertex & 1, vertex >> 1);
    float2 screen = instance.Quad0.xy + corner.x * instance.Quad0.zw + corner.y * instance.Quad1.xy;
    float2 local = screen - (float2)Origin;
    output.Position = float4(local.x * ClipScale.x - 1.0, 1.0 - local.y * ClipScale.y, 0.0, 1.0);
    output.Kind = instance.Quad1.zw;
    output.Param0 = instance.Param0;
    output.Param1 = instance.Param1;
    output.Param2 = instance.Param2;
    output.Color0 = instance.Color0;
    output.Color1 = instance.Color1;
    return output;
}

// 4x4 ordered dither added before truncating alpha, as on the CPU.
static const float Bayer[16] =
{
    0.5 / 16, 8.5 / 16, 2.5 / 16, 10.5 / 16,
    12.5 / 16, 4.5 / 16, 14.5 / 16, 6.5 / 16,
    3.5 / 16, 11.5 / 16, 1.5 / 16, 9.5 / 16,
    15.5 / 16, 7.5 / 16, 13.5 / 16, 5.5 / 16
};

// Signed distance fields of the particle shapes, as ShapeField in FrameComposer.cs (y up, units of size).
float Heart(float x, float y)
{
    x = abs(x);
    if (y + x > 1.0)
    {
        float dx = x - 0.25;
        float dy = y - 0.75;
        return sqrt(dx * dx + dy * dy) - 0.35355339;
    }

    float a = x * x + (y - 1.0) * (y - 1.0);
    float m = 0.5 * max(x + y, 0.0);
    float bx = x - m;
    float by = y - m;
    float b = bx * bx + by * by;
    return sqrt(min(a, b)) * sign(x - y);
}

float Star(float x, float y)
{
    const float K1X = 0.809016994;
    const float K1Y = -0.587785252;
    const float Inner = 0.5;
    x = abs(x);
    float d = max(K1X * x + K1Y * y, 0.0);
    x -= 2.0 * d * K1X;
    y -= 2.0 * d * K1Y;
    d = max(-K1X * x + K1Y * y, 0.0);
    x -= 2.0 * d * -K1X;
    y -= 2.0 * d * K1Y;
    x = abs(x);
    y -= 1.0;
    float baX = Inner * -K1Y;
    float baY = Inner * K1X - 1.0;
    float h = clamp((x * baX + y * baY) / (baX * baX + baY * baY), 0.0, 1.0);
    float ex = x - baX * h;
    float ey = y - baY * h;
    return sqrt(ex * ex + ey * ey) * sign(y * baX - x * baY);
}

float Segment(float px, float py, float ax, float ay, float bx, float by)
{
    float ex = bx - ax;
    float ey = by - ay;
    float wx = px - ax;
    float wy = py - ay;
    float t = clamp((wx * ex + wy * ey) / (ex * ex + ey * ey), 0.0, 1.0);
    float dx = wx - ex * t;
    float dy = wy - ey * t;
    return sqrt(dx * dx + dy * dy);
}

float Snowflake(float x, float y)
{
    x = abs(x);
    y = abs(y);
    float d = -0.8660254 * x + 0.5 * y;
    if (d > 0.0)
    {
        x -= 2.0 * d * -0.8660254;
        y -= 2.0 * d * 0.5;
    }

    d = -0.5 * x + 0.8660254 * y;
    if (d > 0.0)
    {
        x -= 2.0 * d * -0.5;
        y -= 2.0 * d * 0.8660254;
    }

    float arm = Segment(x, y, 0.0, 0.0, 0.8660254, 0.5);
    float branch = Segment(x, y, 0.4763140, 0.275, 0.7582216, 0.1723939);
    float twig = Segment(x, y, 0.6928203, 0.4, 0.8619665, 0.3384364);
    return min(arm, min(branch, twig)) - 0.085;
}

float4 PS(Varyings input, out float depth : SV_Depth) : SV_Target
{
    int2 pixel = int2(input.Position.xy) + Origin;
    float2 center = float2(pixel) + 0.5;
    float dither = Bayer[((pixel.y & 3) << 2) | (pixel.x & 3)];
    int kind = (int)input.Kind.x;
    int alpha;
    float3 color;

    [branch]
    if (kind == KIND_RIBBON)
    {
        // Param0 = A.xy, E.xy (B - A); Param1 = 1/|E|^2, reach^2, index scale at A, its change to B;
        // Param2 = alpha (x255) at A, its change to B, then the t range this piece owns (RibbonLimits);
        // Color0 = rgb at A + next chord x, Color1 = rgb change to B + next chord y (scaled).
        float2 p = center - input.Param0.xy;
        float2 e = input.Param0.zw;
        float tRaw = dot(p, e) * input.Param1.x;
        float2 fromB = p - e;
        if (tRaw < input.Param2.z ||
            (tRaw > input.Param2.w && fromB.x * input.Color0.w + fromB.y * input.Color1.w >= 1.0))
        {
            discard;
        }

        float t = saturate(tRaw);
        float2 d = p - t * e;
        float distanceSquared = dot(d, d);
        if (distanceSquared >= input.Param1.y)
        {
            discard;
        }

        int index = (int)(distanceSquared * (input.Param1.z + input.Param1.w * t));
        if (index >= 1024)
        {
            discard;
        }

        float2 profile = Profiles.Load(int3(index, (int)input.Kind.y, 0));
        alpha = (int)(profile.x * (input.Param2.x + input.Param2.y * t) + dither);
        float3 base = input.Color0.rgb + input.Color1.rgb * t;
        color = base + (255.0 - base) * profile.y;
    }
    else if (kind == KIND_SPARKLE)
    {
        // Param0.xy = centre; Param1 = 1/size, alpha scale (x255), white. Factored per axis like the CPU.
        float2 a = abs(center - input.Param0.xy) * input.Param1.x;
        float2 core2 = exp(-2.2 * a * a);
        float2 slope = exp(-1.7 * a);
        float2 square = exp(-10.0 * a * a);
        float core = core2.x * core2.y;
        float flare = slope.x * square.y + slope.y * square.x;
        alpha = (int)(min(1.0, core + 0.5 * flare) * input.Param1.y + dither);
        float mix = input.Param1.z * min(1.0, core * 1.3 + flare * 0.25);
        color = input.Color0.rgb + (255.0 - input.Color0.rgb) * mix;
    }
    else if (kind == KIND_SHAPE)
    {
        // Param0 = centre.xy, cos and sin of the angle over size; Param1 = size, alpha scale (x255), white, shape.
        float2 d = center - input.Param0.xy;
        float qx = d.x * input.Param0.z + d.y * input.Param0.w;
        float qy = d.x * input.Param0.w - d.y * input.Param0.z;
        int shape = (int)input.Param1.w;
        float distance = shape == SHAPE_HEART ? Heart(qx, qy + 0.55)
            : shape == SHAPE_STAR ? Star(qx, qy) - 0.06
            : Snowflake(qx, qy);
        float coverage = clamp(0.5 - distance * input.Param1.x, 0.0, 1.0);
        float outside = max(distance, 0.0) * (1.0 / 0.45);
        float glow = 0.42 * exp(-outside * outside);
        float intensity = 1.0 - (1.0 - 0.95 * coverage) * (1.0 - glow);
        alpha = (int)(intensity * input.Param1.y + dither);
        float mix = input.Param1.z * coverage * 0.4;
        color = input.Color0.rgb + (255.0 - input.Color0.rgb) * mix;
    }
    else
    {
        // Param0 = centre.xy, radius, 1/thickness; Param1 = inner^2, reach^2, alpha scale (x255);
        // Color0 = rgb already pushed towards white.
        float2 d = center - input.Param0.xy;
        float distanceSquared = dot(d, d);
        if (distanceSquared >= input.Param1.y || distanceSquared <= input.Param1.x)
        {
            discard;
        }

        float v = (sqrt(distanceSquared) - input.Param0.z) * input.Param0.w;
        alpha = (int)(exp(-1.4 * v * v) * input.Param1.z + dither);
        color = input.Color0.rgb;
    }

    if (alpha <= 0)
    {
        discard;
    }

    alpha = min(alpha, 255);
    float scale = alpha / 255.0;
    depth = scale;
    return float4(floor(color * scale + 0.5) / 255.0, scale);
}
