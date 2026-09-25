using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MouseGlowTrail.Bench;

/// <summary>Faithful port of the v1 GDI+ drawing path, used only for timing and visual comparison.</summary>
internal sealed class OldRenderer : IDisposable
{
    private const int MinimumCanvasSize = 768;
    private const int CanvasPadding = 150;
    private const int TrailOffsetX = 8;
    private const int TrailOffsetY = 14;
    private const double TrailLifetimeSeconds = 1.05;

    private readonly List<TrailPoint> _points = [];
    private readonly int _maximumWidth;
    private readonly int _maximumHeight;
    private Bitmap _frame = new(MinimumCanvasSize, MinimumCanvasSize, PixelFormat.Format32bppPArgb);
    private Point _lastCursor;
    private bool _hasLastCursor;
    private float _huePhase;

    public OldRenderer(int maximumWidth, int maximumHeight)
    {
        _maximumWidth = maximumWidth;
        _maximumHeight = maximumHeight;
    }

    public Size CanvasSize => _frame.Size;

    public void Sample(Point cursor, double now)
    {
        if (_hasLastCursor)
        {
            var dx = cursor.X - _lastCursor.X;
            var dy = cursor.Y - _lastCursor.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= 0.8)
            {
                _huePhase = (_huePhase + (float)(distance * 0.45)) % 360f;
                Add(cursor, now);
            }
        }
        else
        {
            Add(cursor, now);
        }

        _points.RemoveAll(point => now - point.Created > TrailLifetimeSeconds);
    }

    /// <summary>The v1 per-tick work: size the moving canvas, clear it and redraw the ribbon.</summary>
    public void RenderFrame(Point cursor, double now)
    {
        var bounds = GetCanvasBounds(cursor);
        if (_frame.Size != bounds.Size)
        {
            _frame.Dispose();
            _frame = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        }

        using var graphics = Graphics.FromImage(_frame);
        Prepare(graphics);
        DrawSmoothRibbon(graphics, bounds.Location, now);
    }

    /// <summary>Draws the ribbon at absolute coordinates into an existing surface (for previews).</summary>
    public void RenderInto(Graphics graphics, double now)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        DrawSmoothRibbon(graphics, Point.Empty, now);
    }

    public void Dispose() => _frame.Dispose();

    private static void Prepare(Graphics graphics)
    {
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    private void Add(Point cursor, double now)
    {
        _points.Add(new TrailPoint(new PointF(cursor.X + TrailOffsetX, cursor.Y + TrailOffsetY), now, _huePhase));
        _lastCursor = cursor;
        _hasLastCursor = true;
    }

    private Rectangle GetCanvasBounds(Point cursor)
    {
        int minX = cursor.X, maxX = cursor.X, minY = cursor.Y, maxY = cursor.Y;
        foreach (var point in _points)
        {
            minX = Math.Min(minX, (int)point.Position.X);
            maxX = Math.Max(maxX, (int)point.Position.X);
            minY = Math.Min(minY, (int)point.Position.Y);
            maxY = Math.Max(maxY, (int)point.Position.Y);
        }

        var visible = _points.Count > 0;
        var width = SelectDimension(_frame.Width, maxX - minX + CanvasPadding * 2, _maximumWidth, visible);
        var height = SelectDimension(_frame.Height, maxY - minY + CanvasPadding * 2, _maximumHeight, visible);
        var left = Math.Clamp((int)Math.Round((minX + maxX - width) / 2.0), cursor.X - width + CanvasPadding,
            cursor.X - CanvasPadding);
        var top = Math.Clamp((int)Math.Round((minY + maxY - height) / 2.0), cursor.Y - height + CanvasPadding,
            cursor.Y - CanvasPadding);
        return new Rectangle(left, top, width, height);
    }

    private static int SelectDimension(int current, int required, int maximum, bool visible)
    {
        if (!visible)
        {
            return MinimumCanvasSize;
        }

        var bounded = Quantize(required, maximum);
        return bounded <= current ? current : Quantize(Math.Max(bounded, current * 3), maximum);
    }

    private static int Quantize(int dimension, int maximum)
    {
        var bounded = Math.Clamp(dimension, MinimumCanvasSize, maximum);
        return Math.Min(maximum, (bounded + 63) / 64 * 64);
    }

    private void DrawSmoothRibbon(Graphics graphics, Point origin, double now)
    {
        if (_points.Count < 2)
        {
            return;
        }

        var stride = Math.Max(1, (int)Math.Ceiling(_points.Count / 120.0));
        var lastIndex = _points.Count - 1;
        if (lastIndex <= stride)
        {
            var end = Mid(_points[0].Position, _points[^1].Position);
            Segment(graphics, _points[0].Position, end, _points[0].Created,
                (_points[0].Created + _points[^1].Created) / 2, _points[0].Hue,
                LerpHue(_points[0].Hue, _points[^1].Hue, 0.5f), origin, now);
            return;
        }

        var first = _points[0];
        var second = _points[Math.Min(stride, lastIndex)];
        Segment(graphics, first.Position, Mid(first.Position, second.Position), first.Created,
            (first.Created + second.Created) / 2, first.Hue, LerpHue(first.Hue, second.Hue, 0.5f), origin, now);
        for (var index = stride; index + stride < _points.Count; index += stride)
        {
            var before = _points[index - stride];
            var current = _points[index];
            var after = _points[index + stride];
            var start = Mid(before.Position, current.Position);
            var stop = Mid(current.Position, after.Position);
            var startCreated = (before.Created + current.Created) / 2;
            var endCreated = (current.Created + after.Created) / 2;
            var startHue = LerpHue(before.Hue, current.Hue, 0.5f);
            var endHue = LerpHue(current.Hue, after.Hue, 0.5f);
            var length = Distance(start, current.Position) + Distance(current.Position, stop);
            var subdivisions = Math.Clamp((int)Math.Ceiling(length / 8.0), 1, 6);
            var previous = start;
            for (var s = 1; s <= subdivisions; s++)
            {
                var progress = (float)s / subdivisions;
                var point = Bezier(start, current.Position, stop, progress);
                var created = startCreated + (endCreated - startCreated) * progress;
                var hue = LerpHue(startHue, endHue, progress);
                Segment(graphics, previous, point, created, created, hue, hue, origin, now);
                previous = point;
            }
        }
    }

    private static void Segment(Graphics graphics, PointF from, PointF to, double fromCreated, double toCreated,
        float fromHue, float toHue, Point origin, double now)
    {
        var age = Math.Clamp((now - (fromCreated + toCreated) / 2) / TrailLifetimeSeconds, 0.0, 1.0);
        var opacity = (float)Math.Pow(1.0 - age, 1.35);
        var color = HueToColor(LerpHue(fromHue, toHue, 0.5f));
        var a = new PointF(from.X - origin.X, from.Y - origin.Y);
        var b = new PointF(to.X - origin.X, to.Y - origin.Y);
        using var halo = new Pen(Color.FromArgb((int)(4 * opacity), color), 20f)
            { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
        graphics.DrawLine(halo, a, b);
        using var body = new Pen(Color.FromArgb((int)(60 * opacity), color), 8f)
            { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };
        graphics.DrawLine(body, a, b);
    }

    private static PointF Mid(PointF a, PointF b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double Distance(PointF a, PointF b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static PointF Bezier(PointF a, PointF c, PointF b, float t)
    {
        var v = 1 - t;
        return new PointF(v * v * a.X + 2 * v * t * c.X + t * t * b.X, v * v * a.Y + 2 * v * t * c.Y + t * t * b.Y);
    }

    private static float LerpHue(float from, float to, float t)
    {
        var delta = (to - from + 540f) % 360f - 180f;
        return (from + delta * t + 360f) % 360f;
    }

    private static Color HueToColor(float hue)
    {
        var blend = hue % 360f / 360f;
        blend = blend * blend * (3 - 2 * blend);
        return Color.FromArgb((int)Math.Round(72 + (246 - 72) * blend), (int)Math.Round(218 + (159 - 218) * blend),
            (int)Math.Round(205 + (194 - 205) * blend));
    }

    private readonly record struct TrailPoint(PointF Position, double Created, float Hue);
}
