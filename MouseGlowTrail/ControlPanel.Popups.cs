using MouseGlowTrail.Ui;
using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

internal sealed unsafe partial class ControlPanel
{
    private enum PopupKind
    {
        None,
        Combo,
        Color
    }

    private enum ColorSlot
    {
        CustomStop,
        Particle,
        Left,
        Right,
        Middle,
        Wheel
    }

    private const float ComboItemHeight = 36f;

    private static readonly uint[] Presets =
    [
        0xFF5F6D, 0xFF9A55, 0xFFD36B, 0x9BEA8C, 0x48DACD, 0x00E5FF, 0x6FB9FF, 0x4F6BFF, 0xB84DFF,
        0xF69FC2, 0xFF9D8A, 0xFFE3A3, 0xC8F2B8, 0xA8EDE6, 0xB9E9FF, 0xB09CFF, 0xE3EAFB, 0xFFFFFF
    ];

    private PopupKind _popup;
    private int _popupOwner;
    private RectF _popupAnchor;
    private RectF _popupRect;
    private double _popupOpened;
    private string[] _comboItems = [];
    private int _comboSelected;
    private Action<int>? _comboPick;
    private ColorSlot _colorSlot;
    private int _colorIndex;
    private float _hue;
    private float _saturation;
    private float _value;
    private int _editing;
    private string _editText = "";
    private bool _editSelectAll;
    private int _editCaret;
    private bool _caretVisible = true;

    // ---- Combo box ----

    private void Combo(string key, RectF rect, string[] items, int selected, Action<int> pick, bool enabled = true)
    {
        var id = Gui.Id(key);
        var clicked = _gui.Behavior(id, rect, out var hover, out var held) && enabled;
        var open = _popup == PopupKind.Combo && _popupOwner == id;
        _gfx.Fill(rect, !enabled ? _theme.ControlFillDisabled : held ? _theme.ControlFillPressed : hover ? _theme.ControlFillHover : _theme.ControlFill, 4f);
        _gui.ControlBorder(rect, 4f);
        var text = selected >= 0 && selected < items.Length ? items[selected] : "";
        _gfx.TextIn(text, TextStyle.Body, new RectF(rect.X + 11f, rect.Y, rect.W - 44f, rect.H),
            enabled ? _theme.TextPrimary : _theme.TextDisabled);
        var chevronOffset = held ? 1.5f : 0f;
        _gfx.Icon('\uE70D', new RectF(rect.Right - 30f, rect.Y + chevronOffset, 20f, rect.H),
            enabled ? _theme.TextSecondary : _theme.TextDisabled, TextStyle.IconSmall);
        _gui.FocusRing(id, rect, 4f);

        // Arrow keys change the value without opening the list, as in WinUI.
        if (enabled && _gui.Focus == id && _gui.Key is { Key: VK_UP or VK_DOWN } press)
        {
            var next = Math.Clamp(selected + (press.Key == VK_DOWN ? 1 : -1), 0, items.Length - 1);
            if (next != selected)
            {
                pick(next);
                _gui.Changed = _gui.Committed = true;
            }

            _gui.Consumed = true;
        }

        if (clicked && !open)
        {
            _popup = PopupKind.Combo;
            _popupOwner = id;
            _popupAnchor = rect;
            _comboItems = items;
            _comboSelected = selected;
            _comboPick = pick;
            _popupOpened = _clock.Elapsed.TotalSeconds;
            _gui.Snap(Gui.Id("popup.fade"), 0f);
        }
    }

    // ---- Colour button ----

    /// <summary>A colour chooser button at the card's right edge; returns its left edge.</summary>
    private float ColorButton(string key, RectF card, ColorSlot slot, string? color, bool enabled = true)
    {
        const float Width = 144f;
        var rect = new RectF(card.Right - 16f - Width, card.CenterY - 16f, Width, 32f);
        var id = Gui.Id(key);
        var clicked = _gui.Behavior(id, rect, out var hover, out var held) && enabled;
        _gfx.Fill(rect, !enabled ? _theme.ControlFillDisabled : held ? _theme.ControlFillPressed : hover ? _theme.ControlFillHover : _theme.ControlFill, 4f);
        _gui.ControlBorder(rect, 4f);
        var chip = new RectF(rect.X + 8f, rect.Y + 7f, 26f, 18f);
        var follow = !ColorText.TryParse(color, out var rgb);
        if (follow)
        {
            // The trail's own colours: a strip of its palette.
            var style = _config.Style;
            Span<(float, uint)> stops = stackalloc (float, uint)[5];
            for (var i = 0; i < stops.Length; i++)
            {
                stops[i] = (i / 4f, 0xFF000000u | style.ColorAt(i / 8f));
            }

            _gfx.FillGradient(chip, 3f, stops, false);
        }
        else
        {
            _gfx.Fill(chip, 0xFF000000u | rgb, 3f);
        }

        if (!enabled)
        {
            _gfx.Fill(chip, Theme.Fade(_theme.Dark ? 0xFF202020u : 0xFFF3F3F3u, 0.6f), 3f);
        }

        _gfx.Stroke(chip, _theme.ControlStroke, 1f, 3f);
        _gfx.TextIn(follow ? Loc.T("跟随光迹", "Match trail") : ColorText.Format(rgb), TextStyle.Body, new RectF(chip.Right + 8f, rect.Y, 80f, rect.H),
            enabled ? _theme.TextPrimary : _theme.TextDisabled);
        _gfx.Icon('\uE70D', new RectF(rect.Right - 26f, rect.Y, 18f, rect.H), enabled ? _theme.TextSecondary : _theme.TextDisabled,
            TextStyle.IconSmall);
        _gui.FocusRing(id, rect, 4f);
        if (clicked)
        {
            OpenColor(id, rect, slot, 0);
        }

        return rect.X;
    }

    private void OpenColor(int owner, RectF anchor, ColorSlot slot, int index)
    {
        _popup = PopupKind.Color;
        _popupOwner = owner;
        _popupAnchor = anchor;
        _colorSlot = slot;
        _colorIndex = index;
        _popupOpened = _clock.Elapsed.TotalSeconds;
        _gui.Snap(Gui.Id("popup.fade"), 0f);
        var current = CurrentColor() ?? _config.Style.ColorAt(0.25f);
        (_hue, _saturation, _value) = ToHsv(current);
        StopEditing();
    }

    private uint? CurrentColor()
    {
        var text = _colorSlot switch
        {
            ColorSlot.CustomStop => _colorIndex < S.CustomColors.Count ? S.CustomColors[_colorIndex] : null,
            ColorSlot.Particle => S.ParticleColor,
            ColorSlot.Left => S.LeftClickColor,
            ColorSlot.Right => S.RightClickColor,
            ColorSlot.Middle => S.MiddleClickColor,
            _ => S.WheelColor
        };
        return ColorText.TryParse(text, out var rgb) ? rgb : null;
    }

    private void SetColor(uint? rgb)
    {
        var text = rgb is { } value ? ColorText.Format(value) : null;
        switch (_colorSlot)
        {
            case ColorSlot.CustomStop:
                if (text is not null && _colorIndex < S.CustomColors.Count)
                {
                    S.CustomColors[_colorIndex] = text;
                    StyleChanged();
                }

                break;
            case ColorSlot.Particle:
                S.ParticleColor = text;
                break;
            case ColorSlot.Left:
                S.LeftClickColor = text;
                break;
            case ColorSlot.Right:
                S.RightClickColor = text;
                break;
            case ColorSlot.Middle:
                S.MiddleClickColor = text;
                break;
            default:
                S.WheelColor = text;
                break;
        }

        _gui.Changed = true;
    }

    private void ClosePopup()
    {
        if (_popup != PopupKind.None)
        {
            _popup = PopupKind.None;
            _popupOwner = 0;
            StopEditing();
            _gui.Committed = true;
            Invalidate(baseChanged: true);
        }
    }

    // ---- Flyouts ----

    private void DrawPopup()
    {
        if (_popup == PopupKind.None)
        {
            return;
        }

        // Light dismiss: a press outside the flyout (or Escape) closes it and goes no further.
        var pressedOutside = _gui.Pressed && !_popupRect.Contains(_gui.MouseX, _gui.MouseY);
        var escape = _gui.Key?.Key == VK_ESCAPE && _editing == 0;
        if (pressedOutside || escape)
        {
            ClosePopup();
            _gui.Consumed = true;
            return;
        }

        var fade = _gui.Animate(Gui.Id("popup.fade"), 1f, 22f);
        var slide = (1f - fade) * -6f;
        _gfx.Opacity = fade;
        if (_popup == PopupKind.Combo)
        {
            DrawComboList(slide);
        }
        else
        {
            DrawColorFlyout(slide);
        }

        _gfx.Opacity = 1f;
    }

    private void Flyout(RectF rect)
    {
        // A soft shadow built from a few widening, fading outlines.
        for (var i = 1; i <= 4; i++)
        {
            _gfx.Fill(rect.Offset(0, 2f + i).Inflate(i * 1.5f), (uint)(0x0A - i) << 24, 8f + i * 1.5f);
        }

        _gfx.Fill(rect, _theme.FlyoutFill, 8f);
        _gfx.Stroke(rect, _theme.Dark ? 0x33000000u : 0x1A000000u, 1f, 8f);
        if (_theme.Dark)
        {
            _gfx.Stroke(rect.Inflate(-1f), 0x0FFFFFFFu, 1f, 7f);
        }
    }

    private void DrawComboList(float slide)
    {
        var width = _popupAnchor.W;
        foreach (var item in _comboItems)
        {
            width = MathF.Max(width, _gfx.Measure(item, TextStyle.Body).Width + 48f);
        }

        var height = _comboItems.Length * ComboItemHeight + 8f;
        // Open so that the selected item sits over the box, kept inside the window.
        var y = _popupAnchor.Y - 4f - Math.Max(0, _comboSelected) * ComboItemHeight - 2f;
        y = Math.Clamp(y, TitleHeight + 4f, MathF.Max(TitleHeight + 4f, _height - height - 8f));
        var x = Math.Clamp(_popupAnchor.X - 4f, 8f, MathF.Max(8f, _width - width - 8f));
        _popupRect = new RectF(x, y, width, height);
        Flyout(_popupRect.Offset(0, slide));
        for (var i = 0; i < _comboItems.Length; i++)
        {
            var item = new RectF(x + 4f, y + 4f + i * ComboItemHeight + slide, width - 8f, ComboItemHeight - 4f);
            var id = Gui.Id("combo.item", i);
            var clicked = _gui.Behavior(id, item, out var hover, out var held, focusable: false);
            var keyboard = i == _comboSelected && _gui.Key is { Key: VK_RETURN or VK_SPACE };
            if (i == _comboSelected || hover)
            {
                _gfx.Fill(item, held ? _theme.SubtlePressed : _theme.SubtleHover, 4f);
            }

            if (i == _comboSelected)
            {
                _gfx.Fill(new RectF(item.X, item.CenterY - 8f, 3f, 16f), _theme.Accent, 1.5f);
            }

            _gfx.TextIn(_comboItems[i], TextStyle.Body, new RectF(item.X + 12f, item.Y, item.W - 16f, item.H),
                held ? _theme.TextSecondary : _theme.TextPrimary);
            if (clicked || keyboard)
            {
                _comboPick?.Invoke(i);
                _gui.Changed = true;
                ClosePopup();
                return;
            }
        }

        if (_gui.Key is { Key: VK_UP or VK_DOWN } key)
        {
            _comboSelected = Math.Clamp(_comboSelected + (key.Key == VK_DOWN ? 1 : -1), 0, _comboItems.Length - 1);
            _gui.Consumed = true;
        }
    }

    private void DrawColorFlyout(float slide)
    {
        const float Width = 300f;
        const float Padding = 12f;
        var allowFollow = _colorSlot != ColorSlot.CustomStop;
        var height = Padding + (allowFollow ? 40f : 0f) + 150f + 12f + 16f + 14f + 32f + 14f + 24f * 2 + 6f + Padding +
                     (_colorSlot == ColorSlot.CustomStop && S.CustomColors.Count > 1 ? 44f : 0f);
        var below = _popupAnchor.Bottom + 6f;
        var y = below + height <= _height - 8f ? below : MathF.Max(TitleHeight + 4f, _popupAnchor.Y - 6f - height);
        var x = Math.Clamp(_popupAnchor.Right - Width, 8f, MathF.Max(8f, _width - Width - 8f));
        _popupRect = new RectF(x, y, Width, height);
        var rect = _popupRect.Offset(0, slide);
        Flyout(rect);
        var inner = rect.Inflate(-Padding);
        var cursor = inner.Y;
        var current = CurrentColor();
        var following = current is null;

        if (allowFollow)
        {
            // Segmented choice between the trail's colours and a fixed colour.
            var half = (inner.W - 4f) * 0.5f;
            var followRect = new RectF(inner.X, cursor, half, 32f);
            var fixedRect = new RectF(inner.X + half + 4f, cursor, half, 32f);
            if (Segment(Gui.Id("color.follow"), followRect, Loc.T("跟随光迹", "Match trail"), following))
            {
                SetColor(null);
                _gui.Committed = true;
            }

            if (Segment(Gui.Id("color.fixed"), fixedRect, Loc.T("固定颜色", "Fixed color"), !following))
            {
                SetColor(FromHsv(_hue, _saturation, _value));
                _gui.Committed = true;
            }

            cursor += 40f;
        }

        // Saturation / value square.
        var square = new RectF(inner.X, cursor, inner.W, 150f);
        var hueColor = 0xFF000000u | FromHsv(_hue, 1f, 1f);
        _gfx.Fill(square, hueColor, 4f);
        _gfx.FillGradient(square, 4f, [(0f, 0xFFFFFFFFu), (1f, 0x00FFFFFFu)], false);
        _gfx.FillGradient(square, 4f, [(0f, 0x00000000u), (1f, 0xFF000000u)], true);
        _gfx.Stroke(square, _theme.ControlStroke, 1f, 4f);
        var squareId = Gui.Id("color.square");
        if (_gui.Hover(square) && _gui.Pressed)
        {
            _gui.Active = squareId;
            _gui.Consumed = true;
        }

        if (_gui.Active == squareId && (_gui.LeftDown || _gui.Pressed))
        {
            _saturation = Math.Clamp((_gui.MouseX - square.X) / square.W, 0f, 1f);
            _value = 1f - Math.Clamp((_gui.MouseY - square.Y) / square.H, 0f, 1f);
            SetColor(FromHsv(_hue, _saturation, _value));
            StopEditing();
        }

        if (_gui.Active == squareId && _gui.Released)
        {
            _gui.Committed = true;
        }

        var markX = square.X + _saturation * square.W;
        var markY = square.Y + (1f - _value) * square.H;
        _gfx.StrokeEllipse(markX, markY, 7f, 7f, 0xFFFFFFFFu, 2f);
        _gfx.StrokeEllipse(markX, markY, 8.5f, 8.5f, 0x66000000u, 1f);
        if (following)
        {
            _gfx.Fill(square, Theme.Fade(_theme.FlyoutFill, 0.55f), 4f);
        }

        cursor = square.Bottom + 12f;

        // Hue strip.
        var strip = new RectF(inner.X, cursor, inner.W, 16f);
        _gfx.FillGradient(strip, 8f,
        [
            (0f, 0xFFFF0000u), (1f / 6, 0xFFFFFF00u), (2f / 6, 0xFF00FF00u), (3f / 6, 0xFF00FFFFu), (4f / 6, 0xFF0000FFu),
            (5f / 6, 0xFFFF00FFu), (1f, 0xFFFF0000u)
        ], false);
        var stripId = Gui.Id("color.hue");
        if (_gui.Hover(strip.Inflate(0, 4f)) && _gui.Pressed)
        {
            _gui.Active = stripId;
            _gui.Consumed = true;
        }

        if (_gui.Active == stripId && (_gui.LeftDown || _gui.Pressed))
        {
            _hue = Math.Clamp((_gui.MouseX - strip.X) / strip.W, 0f, 1f) * 360f;
            if (_saturation < 0.05f)
            {
                // Picking a hue on a grey would show nothing; bring some colour in.
                _saturation = 0.7f;
                _value = MathF.Max(_value, 0.8f);
            }

            SetColor(FromHsv(_hue, _saturation, _value));
            StopEditing();
        }

        if (_gui.Active == stripId && _gui.Released)
        {
            _gui.Committed = true;
        }

        var hueX = strip.X + _hue / 360f * strip.W;
        _gfx.FillEllipse(hueX, strip.CenterY, 9f, 9f, 0xFFFFFFFFu);
        _gfx.FillEllipse(hueX, strip.CenterY, 6f, 6f, hueColor);
        _gfx.StrokeEllipse(hueX, strip.CenterY, 9.5f, 9.5f, 0x40000000u, 1f);
        cursor = strip.Bottom + 14f;

        // Current colour and its hex code.
        var swatch = new RectF(inner.X, cursor, 32f, 32f);
        var shown = current ?? FromHsv(_hue, _saturation, _value);
        _gfx.Fill(swatch, 0xFF000000u | shown, 4f);
        _gfx.Stroke(swatch, _theme.ControlStroke, 1f, 4f);
        HexBox(new RectF(swatch.Right + 8f, cursor, 120f, 32f), shown);
        cursor += 32f + 14f;

        // Presets.
        const float Chip = 24f;
        var gap = (inner.W - 9 * Chip) / 8f;
        for (var i = 0; i < Presets.Length; i++)
        {
            var chip = new RectF(inner.X + i % 9 * (Chip + gap), cursor + i / 9 * (Chip + 6f), Chip, Chip);
            var id = Gui.Id("color.preset", i);
            var clicked = _gui.Behavior(id, chip, out var hover, out _, focusable: false);
            _gfx.Fill(chip, 0xFF000000u | Presets[i], 4f);
            _gfx.Stroke(chip, hover ? _theme.ControlStrongStroke : _theme.ControlStroke, hover ? 2f : 1f, 4f);
            if (current == Presets[i])
            {
                _gfx.Icon('\uE73E', chip, Palette.Luminance(Presets[i]) > 0.5f ? 0xE4000000u : 0xFFFFFFFFu, TextStyle.IconSmall);
            }

            if (clicked)
            {
                (_hue, _saturation, _value) = ToHsv(Presets[i]);
                SetColor(Presets[i]);
                StopEditing();
                _gui.Committed = true;
            }
        }

        cursor += Chip * 2 + 6f + 12f;
        if (_colorSlot == ColorSlot.CustomStop && S.CustomColors.Count > 1)
        {
            var remove = new RectF(inner.X, cursor, inner.W, 32f);
            if (_gui.Button(Gui.Id("color.remove"), remove, Loc.T("删除这个颜色", "Remove this color"), icon: '\uE74D'))
            {
                S.CustomColors.RemoveAt(Math.Min(_colorIndex, S.CustomColors.Count - 1));
                StyleChanged();
                _gui.Changed = _gui.Committed = true;
                ClosePopup();
            }
        }
    }

    private bool Segment(int id, RectF rect, string text, bool selected)
    {
        var clicked = _gui.Behavior(id, rect, out var hover, out var held, focusable: false);
        _gfx.Fill(rect, selected ? _theme.Accent : held ? _theme.ControlFillPressed : hover ? _theme.ControlFillHover : _theme.ControlFill, 4f);
        if (!selected)
        {
            _gui.ControlBorder(rect, 4f);
        }

        _gfx.TextIn(text, TextStyle.Body, rect, selected ? _theme.TextOnAccent : _theme.TextPrimary, 0.5f);
        return clicked && !selected;
    }

    // ---- Hex text box ----

    private void HexBox(RectF rect, uint color)
    {
        var id = Gui.Id("color.hex");
        var hover = _gui.Hover(rect);
        if (hover)
        {
            _gui.Cursor = CursorKind.IBeam;
        }

        if (hover && _gui.Pressed)
        {
            if (_editing != id)
            {
                _editing = id;
                _editText = ColorText.Format(color);
                _editSelectAll = true;
                _editCaret = _editText.Length;
                StartCaret();
            }
            else
            {
                _editSelectAll = false;
                _editCaret = _gfx.HitTest(_editText, TextStyle.Body, _gui.MouseX - rect.X - 10f);
            }

            _gui.Consumed = true;
        }

        var editing = _editing == id;
        if (editing)
        {
            HandleEditKeys(color);
        }

        var text = editing ? _editText : ColorText.Format(color);
        _gfx.Fill(rect, editing ? (_theme.Dark ? 0xFF1F1F1Fu : 0xFFFFFFFFu) : hover ? _theme.ControlFillHover : _theme.ControlFill, 4f);
        _gfx.Stroke(rect, _theme.ControlStroke, 1f, 4f);
        var underline = editing ? 2f : 1f;
        _gfx.PushClip(rect);
        _gfx.Fill(new RectF(rect.X, rect.Bottom - underline, rect.W, underline), editing ? _theme.Accent : _theme.ControlStrongStroke);
        _gfx.PopClip();
        var textBox = new RectF(rect.X + 10f, rect.Y, rect.W - 20f, rect.H);
        if (editing && _editSelectAll && _editText.Length > 0)
        {
            var (width, _) = _gfx.Measure(_editText, TextStyle.Body);
            _gfx.Fill(new RectF(textBox.X, rect.Y + 7f, width, rect.H - 14f), _theme.Accent);
            _gfx.TextIn(text, TextStyle.Body, textBox, _theme.TextOnAccent);
        }
        else
        {
            _gfx.TextIn(text, TextStyle.Body, textBox, _theme.TextPrimary);
        }

        if (editing && _caretVisible && !_editSelectAll)
        {
            var caretX = textBox.X + _gfx.CaretX(_editText, TextStyle.Body, _editCaret);
            _gfx.Fill(new RectF(caretX, rect.Y + 8f, 1f, rect.H - 16f), _theme.TextPrimary);
        }
    }

    private void HandleEditKeys(uint color)
    {
        if (_gui.Typed.Length > 0)
        {
            foreach (var character in _gui.Typed)
            {
                if (!Uri.IsHexDigit(character) && character != '#')
                {
                    continue;
                }

                if (_editSelectAll)
                {
                    _editText = "";
                    _editCaret = 0;
                    _editSelectAll = false;
                }

                if (_editText.Length < 7)
                {
                    _editText = _editText.Insert(_editCaret, char.ToUpperInvariant(character).ToString());
                    _editCaret++;
                }
            }

            _caretVisible = true;
            ApplyEdit(commit: false);
            _gui.Consumed = true;
        }

        if (_gui.Key is not { } key)
        {
            return;
        }

        _gui.Consumed = true;
        _caretVisible = true;
        switch (key.Key)
        {
            case VK_BACK:
                if (_editSelectAll)
                {
                    _editText = "";
                    _editCaret = 0;
                    _editSelectAll = false;
                }
                else if (_editCaret > 0)
                {
                    _editText = _editText.Remove(_editCaret - 1, 1);
                    _editCaret--;
                }

                ApplyEdit(commit: false);
                break;
            case VK_DELETE:
                if (_editSelectAll)
                {
                    _editText = "";
                    _editCaret = 0;
                    _editSelectAll = false;
                }
                else if (_editCaret < _editText.Length)
                {
                    _editText = _editText.Remove(_editCaret, 1);
                }

                ApplyEdit(commit: false);
                break;
            case VK_LEFT:
                _editCaret = _editSelectAll ? 0 : Math.Max(0, _editCaret - 1);
                _editSelectAll = false;
                break;
            case VK_RIGHT:
                _editCaret = _editSelectAll ? _editText.Length : Math.Min(_editText.Length, _editCaret + 1);
                _editSelectAll = false;
                break;
            case VK_HOME:
                _editCaret = 0;
                _editSelectAll = false;
                break;
            case VK_END:
                _editCaret = _editText.Length;
                _editSelectAll = false;
                break;
            case VK_RETURN:
                ApplyEdit(commit: true);
                StopEditing();
                break;
            case VK_ESCAPE:
                StopEditing();
                break;
            case 'A' when key.Control:
                _editSelectAll = true;
                break;
            case 'C' when key.Control:
                ClipboardText.Set(_hwnd, _editText);
                break;
            case 'V' when key.Control:
                if (ClipboardText.Get(_hwnd) is { } pasted && ColorText.TryParse(pasted, out var rgb))
                {
                    _editText = ColorText.Format(rgb);
                    _editCaret = _editText.Length;
                    _editSelectAll = false;
                    ApplyEdit(commit: true);
                }

                break;
        }
    }

    private void ApplyEdit(bool commit)
    {
        if (ColorText.TryParse(_editText, out var rgb) && _editText.TrimStart('#').Length == 6)
        {
            (_hue, _saturation, _value) = ToHsv(rgb);
            SetColor(rgb);
            if (commit)
            {
                _gui.Committed = true;
            }
        }
    }

    private void StartCaret()
    {
        _caretVisible = true;
        SetTimer(_hwnd, CaretTimer, (uint)Math.Max(300, GetCaretBlinkTime()), 0);
    }

    private void StopEditing()
    {
        if (_editing != 0)
        {
            _editing = 0;
            KillTimer(_hwnd, CaretTimer);
        }
    }

    private static int GetCaretBlinkTime() => 530;

    // ---- Colour maths ----

    private static (float Hue, float Saturation, float Value) ToHsv(uint rgb)
    {
        var r = ((rgb >> 16) & 255) / 255f;
        var g = ((rgb >> 8) & 255) / 255f;
        var b = (rgb & 255) / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        var hue = delta <= 0f ? 0f
            : max == r ? 60f * (((g - b) / delta % 6f + 6f) % 6f)
            : max == g ? 60f * ((b - r) / delta + 2f)
            : 60f * ((r - g) / delta + 4f);
        return (hue, max <= 0f ? 0f : delta / max, max);
    }

    private static uint FromHsv(float hue, float saturation, float value)
    {
        var c = value * saturation;
        var h = (hue % 360f + 360f) % 360f / 60f;
        var x = c * (1f - MathF.Abs(h % 2f - 1f));
        var (r, g, b) = h switch
        {
            < 1f => (c, x, 0f),
            < 2f => (x, c, 0f),
            < 3f => (0f, c, x),
            < 4f => (0f, x, c),
            < 5f => (x, 0f, c),
            _ => (c, 0f, x)
        };
        var m = value - c;
        uint Channel(float v) => (uint)Math.Clamp((int)MathF.Round((v + m) * 255f), 0, 255);
        return (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
    }
}

/// <summary>Plain-text clipboard access for the hex box.</summary>
internal static unsafe class ClipboardText
{
    public static string? Get(IntPtr owner)
    {
        if (OpenClipboard(owner) == 0)
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            var data = handle != 0 ? (char*)GlobalLock(handle) : null;
            if (data == null)
            {
                return null;
            }

            try
            {
                return new string(data);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void Set(IntPtr owner, string text)
    {
        if (OpenClipboard(owner) == 0)
        {
            return;
        }

        try
        {
            EmptyClipboard();
            var bytes = (nuint)((text.Length + 1) * 2);
            var memory = GlobalAlloc(GMEM_MOVEABLE, bytes);
            if (memory == 0)
            {
                return;
            }

            var target = (char*)GlobalLock(memory);
            text.AsSpan().CopyTo(new Span<char>(target, text.Length));
            target[text.Length] = '\0';
            GlobalUnlock(memory);
            if (SetClipboardData(CF_UNICODETEXT, memory) == 0)
            {
                GlobalFree(memory);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}
