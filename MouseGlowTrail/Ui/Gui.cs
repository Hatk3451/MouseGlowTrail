namespace MouseGlowTrail.Ui;

internal enum CursorKind
{
    Arrow,
    Hand,
    IBeam
}

internal readonly record struct KeyPress(int Key, bool Shift, bool Control);

/// <summary>
/// Immediate-mode controls in the Windows 11 (WinUI) style. Every input event runs one pass of the
/// layout code with drawing switched off, which lets widgets react to that event; a separate pass
/// then draws the result. Widget state lives here, keyed by an id per control.
/// </summary>
internal sealed class Gui
{
    private readonly Dictionary<int, float> _animations = [];
    private readonly List<int> _focusOrder = [];
    private int[] _lastFocusOrder = [];
    private int _pendingFocusMove;

    public Gui(Gfx gfx, Theme theme)
    {
        G = gfx;
        T = theme;
    }

    public Gfx G { get; }
    public Theme T { get; set; }

    // Input for the current pass.
    public float MouseX { get; set; } = -1;
    public float MouseY { get; set; } = -1;
    public bool MouseInWindow { get; set; }
    public bool LeftDown { get; set; }
    public bool Pressed { get; set; }
    public bool Released { get; set; }
    public float Wheel { get; set; }
    public KeyPress? Key { get; set; }
    public string Typed { get; set; } = "";

    /// <summary>Base controls ignore the mouse (a flyout is open above them).</summary>
    public bool Blocked { get; set; }

    /// <summary>Where hit testing is currently allowed (scrolling regions clip their content).</summary>
    public RectF Clip { get; set; } = new(-1e6f, -1e6f, 2e6f, 2e6f);

    public int Active { get; set; }
    public int Focus { get; set; }
    public bool FocusVisible { get; set; }
    public CursorKind Cursor { get; set; }

    /// <summary>Seconds since the previous drawn frame (animations advance only while drawing).</summary>
    public float Dt { get; set; }

    /// <summary>An animation is still moving: another frame is needed.</summary>
    public bool Animating { get; set; }

    /// <summary>Animations jump straight to their end (the system's "animation effects" are off).</summary>
    public bool ReduceMotion { get; set; }

    /// <summary>A value changed during this pass.</summary>
    public bool Changed { get; set; }

    /// <summary>A change was completed (released slider, clicked option): worth saving.</summary>
    public bool Committed { get; set; }

    /// <summary>Input was consumed by some control (so e.g. a click outside a flyout is not reused).</summary>
    public bool Consumed { get; set; }

    public static int Id(string name) => name.GetHashCode();

    public static int Id(string name, int index) => HashCode.Combine(name.GetHashCode(), index);

    public void BeginPass()
    {
        _focusOrder.Clear();
        FocusRect = null;
        Cursor = CursorKind.Arrow;
        Animating = false;
        Changed = false;
        Committed = false;
        Consumed = false;
        Blocked = false;
        Clip = new RectF(-1e6f, -1e6f, 2e6f, 2e6f);
    }

    public void EndPass()
    {
        if (Released)
        {
            Active = 0;
        }

        if (Key is { Key: Native.VK_TAB } tab && !Consumed)
        {
            _pendingFocusMove = tab.Shift ? -1 : 1;
        }

        _lastFocusOrder = [.. _focusOrder];
        if (_pendingFocusMove != 0 && _lastFocusOrder.Length > 0)
        {
            var index = Array.IndexOf(_lastFocusOrder, Focus);
            index = index < 0
                ? (_pendingFocusMove > 0 ? 0 : _lastFocusOrder.Length - 1)
                : (index + _pendingFocusMove + _lastFocusOrder.Length) % _lastFocusOrder.Length;
            Focus = _lastFocusOrder[index];
            FocusVisible = true;
        }

        _pendingFocusMove = 0;
        if (Pressed && !Consumed)
        {
            // Clicking empty space takes the keyboard focus away from whatever had it.
            Focus = 0;
        }
    }

    public bool Hover(RectF rect) =>
        !Blocked && MouseInWindow && rect.Contains(MouseX, MouseY) && Clip.Contains(MouseX, MouseY);

    /// <summary>Eases a value towards a target; framerate independent.</summary>
    public float Animate(int id, float target, float speed = 14f)
    {
        if (!_animations.TryGetValue(id, out var value) || ReduceMotion)
        {
            value = target;
        }
        else if (G.Rendering)
        {
            value += (target - value) * (1f - MathF.Exp(-speed * Dt));
            if (MathF.Abs(target - value) < 0.002f)
            {
                value = target;
            }
        }

        if (value != target)
        {
            Animating = true;
        }

        _animations[id] = value;
        return value;
    }

    /// <summary>Sets an animated value without easing (e.g. when a view appears).</summary>
    public void Snap(int id, float value) => _animations[id] = value;

    /// <summary>Where the focused control was laid out in the last pass (to scroll it into view).</summary>
    public RectF? FocusRect { get; private set; }

    public void Focusable(int id, RectF rect = default)
    {
        _focusOrder.Add(id);
        if (id == Focus && rect.W > 0)
        {
            FocusRect = rect;
        }
    }

    public bool KeyFor(int id, int key) => Focus == id && Key?.Key == key;

    /// <summary>Click behaviour shared by buttons: press, capture, release inside.</summary>
    public bool Behavior(int id, RectF rect, out bool hover, out bool held, bool focusable = true)
    {
        hover = Hover(rect);
        if (focusable)
        {
            Focusable(id, rect);
        }

        if (hover && Pressed)
        {
            Active = id;
            Focus = focusable ? id : Focus;
            FocusVisible = false;
            Consumed = true;
        }

        held = Active == id && LeftDown;
        var clicked = false;
        if (Released && Active == id)
        {
            clicked = hover;
            Consumed = true;
        }

        if (Focus == id && Key is { Key: Native.VK_SPACE or Native.VK_RETURN })
        {
            clicked = true;
            Consumed = true;
        }

        return clicked;
    }

    public void FocusRing(int id, RectF rect, float radius)
    {
        if (Focus == id && FocusVisible)
        {
            G.Stroke(rect.Inflate(3f), T.FocusOuter, 2f, radius + 3f);
            G.Stroke(rect.Inflate(1f), T.FocusInner, 1f, radius + 1f);
        }
    }

    // ---- Controls ----

    public bool Button(int id, RectF rect, string text, bool accent = false, char icon = '\0', bool enabled = true)
    {
        var clicked = Behavior(id, rect, out var hover, out var held) && enabled;
        uint fill, textColor;
        if (!enabled)
        {
            fill = accent ? T.ControlStrongFill : T.ControlFillDisabled;
            textColor = accent ? T.TextOnAccent : T.TextDisabled;
        }
        else if (accent)
        {
            fill = held ? T.AccentPressed : hover ? T.AccentHover : T.Accent;
            textColor = held ? Theme.Fade(T.TextOnAccent, 0.8f) : T.TextOnAccent;
        }
        else
        {
            fill = held ? T.ControlFillPressed : hover ? T.ControlFillHover : T.ControlFill;
            textColor = held ? T.TextSecondary : T.TextPrimary;
        }

        G.Fill(rect, fill, 4f);
        if (!accent)
        {
            ControlBorder(rect, 4f);
        }

        var (width, _) = G.Measure(text, TextStyle.Body);
        var contentWidth = width + (icon != '\0' ? 24f : 0f);
        var x = rect.CenterX - contentWidth * 0.5f;
        if (icon != '\0')
        {
            G.Icon(icon, new RectF(x, rect.Y, 16f, rect.H), textColor, TextStyle.Icon);
            x += 24f;
        }

        G.TextIn(text, TextStyle.Body, new RectF(x, rect.Y, width, rect.H), textColor);
        FocusRing(id, rect, 4f);
        return clicked;
    }

    /// <summary>A borderless glyph button (title bar, remove buttons).</summary>
    public bool IconButton(int id, RectF rect, char glyph, uint hoverFill = 0, uint glyphColor = 0, bool focusable = true,
        TextStyle style = TextStyle.Icon, float radius = 4f)
    {
        var clicked = Behavior(id, rect, out var hover, out var held, focusable);
        if (hover || held)
        {
            G.Fill(rect, hoverFill != 0 ? hoverFill : held ? T.SubtlePressed : T.SubtleHover, radius);
        }

        G.Icon(glyph, rect, glyphColor != 0 ? glyphColor : held ? T.TextSecondary : T.TextPrimary, style);
        FocusRing(id, rect, radius);
        return clicked;
    }

    /// <summary>The subtle two-tone edge of a raised control.</summary>
    public void ControlBorder(RectF rect, float radius)
    {
        G.Stroke(rect, T.ControlStroke, 1f, radius);
        if (!T.Dark)
        {
            G.Fill(new RectF(rect.X + radius, rect.Bottom - 1f / G.Scale, rect.W - 2 * radius, 1f / G.Scale),
                T.ControlStrokeBottom);
        }
    }

    /// <summary>ToggleSwitch with its 开 / 关 label on the left; returns true when toggled.</summary>
    public bool Toggle(int id, float right, float centerY, ref bool value, bool enabled = true)
    {
        var track = new RectF(right - 40f, centerY - 10f, 40f, 20f);
        var label = value ? Loc.T("开", "On") : Loc.T("关", "Off");
        var (labelWidth, _) = G.Measure(label, TextStyle.Body);
        var hit = new RectF(track.X - labelWidth - 12f, track.Y - 6f, track.W + labelWidth + 12f, track.H + 12f);
        var clicked = Behavior(id, hit, out var hover, out var held) && enabled;
        if (clicked)
        {
            value = !value;
            Changed = Committed = true;
        }

        var on = Animate(id, value ? 1f : 0f, 18f);
        G.TextIn(label, TextStyle.Body, new RectF(hit.X, track.Y, labelWidth, track.H), enabled ? T.TextPrimary : T.TextDisabled);
        if (!enabled)
        {
            G.Fill(track, value ? T.ControlStrongFill : 0, 10f);
            G.Stroke(track, value ? 0 : T.TextDisabled, 1f, 10f);
        }
        else if (value)
        {
            G.Fill(track, held ? T.AccentPressed : hover ? T.AccentHover : T.Accent, 10f);
        }
        else
        {
            G.Fill(track, hover ? T.ControlAltFillHover : T.ControlAltFill, 10f);
            G.Stroke(track, T.ControlStrongStroke, 1f, 10f);
        }

        // The knob grows on hover and stretches while pressed, like WinUI's.
        var knobHeight = held ? 14f : hover ? 14f : 12f;
        var knobWidth = held ? 17f : knobHeight;
        var travel = track.W - 8f - knobWidth;
        var knobX = track.X + 4f + travel * on;
        var knobColor = !enabled ? T.TextDisabled : Theme.Mix(T.TextSecondary, T.TextOnAccent, on);
        G.Fill(new RectF(knobX, centerY - knobHeight * 0.5f, knobWidth, knobHeight), knobColor, knobHeight * 0.5f);
        FocusRing(id, track, 10f);
        return clicked;
    }

    /// <summary>Horizontal slider; returns true while the value changes.</summary>
    public bool Slider(int id, RectF rect, ref float value, float min, float max, float step, bool enabled = true)
    {
        var hit = rect.Inflate(0f, 6f);
        var hover = Hover(hit) && enabled;
        Focusable(id, rect);
        var changed = false;
        var before = value;
        var trackLeft = rect.X + 10f;
        var trackWidth = rect.W - 20f;
        if (hover && Pressed)
        {
            Active = id;
            Focus = id;
            FocusVisible = false;
            Consumed = true;
        }

        if (Active == id && (LeftDown || Pressed))
        {
            var t = Math.Clamp((MouseX - trackLeft) / trackWidth, 0f, 1f);
            value = Quantize(min + t * (max - min), min, max, step);
        }

        if (Active == id && Released)
        {
            Committed = true;
            Consumed = true;
        }

        if (Focus == id && enabled && Key is { } key)
        {
            var delta = key.Key switch
            {
                Native.VK_LEFT or Native.VK_DOWN => -step,
                Native.VK_RIGHT or Native.VK_UP => step,
                Native.VK_PRIOR => step * 10f,
                Native.VK_NEXT => -step * 10f,
                _ => 0f
            };
            if (key.Key == Native.VK_HOME)
            {
                value = min;
            }
            else if (key.Key == Native.VK_END)
            {
                value = max;
            }
            else if (delta != 0f)
            {
                value = Quantize(value + delta, min, max, step);
            }

            if (delta != 0f || key.Key is Native.VK_HOME or Native.VK_END)
            {
                Committed = true;
                Consumed = true;
            }
        }

        if (value != before)
        {
            changed = true;
            Changed = true;
        }

        var fraction = max > min ? (value - min) / (max - min) : 0f;
        var held = Active == id;
        var y = rect.CenterY;
        G.Fill(new RectF(trackLeft, y - 2f, trackWidth, 4f), enabled ? T.ControlStrongFill : T.ControlFillDisabled, 2f);
        G.Fill(new RectF(trackLeft, y - 2f, trackWidth * fraction, 4f), enabled ? T.Accent : T.TextDisabled, 2f);
        var thumbX = trackLeft + trackWidth * fraction;
        G.FillEllipse(thumbX, y, 10f, 10f, T.ControlSolid);
        G.StrokeEllipse(thumbX, y, 10f, 10f, T.Dark ? 0x23FFFFFFu : 0x1F000000u, 1f / G.Scale);
        var inner = Animate(id, held ? 5f : hover ? 7f : 6f, 20f);
        G.FillEllipse(thumbX, y, inner, inner, enabled ? T.Accent : T.TextDisabled);
        FocusRing(id, new RectF(thumbX - 10f, y - 10f, 20f, 20f), 10f);
        return changed;
    }

    private static float Quantize(float value, float min, float max, float step) =>
        Math.Clamp(step > 0 ? min + MathF.Round((value - min) / step) * step : value, min, max);

    /// <summary>A navigation entry with its sliding selection pill drawn by the owner.</summary>
    public bool NavItem(int id, RectF rect, char glyph, string text, bool selected)
    {
        var clicked = Behavior(id, rect, out var hover, out var held);
        var fill = selected ? (held ? T.SubtlePressed : T.SubtleHover) : held ? T.SubtlePressed : hover ? T.SubtleHover : 0;
        G.Fill(rect, fill, 4f);
        G.Icon(glyph, new RectF(rect.X + 12f, rect.Y, 16f, rect.H), selected ? T.AccentText : T.TextPrimary);
        G.TextIn(text, selected ? TextStyle.BodyStrong : TextStyle.Body, new RectF(rect.X + 44f, rect.Y, rect.W - 48f, rect.H),
            held ? T.TextSecondary : T.TextPrimary);
        FocusRing(id, rect, 4f);
        return clicked;
    }

    /// <summary>A vertical scrollbar for a region; handles the wheel. Returns the (animated) offset.</summary>
    public float Scroll(int id, RectF viewport, float contentHeight, ref float target)
    {
        var maximum = MathF.Max(0f, contentHeight - viewport.H);
        if (Wheel != 0f && Hover(viewport))
        {
            target = Math.Clamp(target - Wheel * 96f, 0f, maximum);
            Consumed = true;
        }

        target = Math.Clamp(target, 0f, maximum);
        var offset = Animate(id, target, 16f);
        if (maximum <= 0f)
        {
            return 0f;
        }

        // A thin indicator that widens when the pointer comes near, as in WinUI.
        var near = Hover(new RectF(viewport.Right - 16f, viewport.Y, 16f, viewport.H)) || Active == id;
        var width = Animate(id + 1, near ? 6f : 2f, 20f);
        var barHeight = MathF.Max(32f, viewport.H * viewport.H / contentHeight);
        var barY = viewport.Y + (viewport.H - barHeight) * (offset / maximum);
        var bar = new RectF(viewport.Right - 4f - width, barY + 2f, width, barHeight - 4f);
        var thumbHit = new RectF(viewport.Right - 14f, barY, 14f, barHeight);
        if (Hover(thumbHit) && Pressed)
        {
            Active = id;
            Consumed = true;
            _dragStart = MouseY - barY;
        }

        if (Active == id && LeftDown)
        {
            var fraction = (MouseY - _dragStart - viewport.Y) / MathF.Max(1f, viewport.H - barHeight);
            target = Math.Clamp(fraction * maximum, 0f, maximum);
            Snap(id, target);
        }

        if (near)
        {
            G.Fill(new RectF(viewport.Right - 12f, viewport.Y + 2f, 10f, viewport.H - 4f), T.Dark ? 0x10FFFFFFu : 0x0C000000u, 5f);
        }

        G.Fill(bar, T.Dark ? 0x8BFFFFFFu : 0x72000000u, width * 0.5f);
        return offset;
    }

    private float _dragStart;
}
