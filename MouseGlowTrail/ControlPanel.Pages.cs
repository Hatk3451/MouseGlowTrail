using MouseGlowTrail.Ui;
using static MouseGlowTrail.Native;

namespace MouseGlowTrail;

internal sealed unsafe partial class ControlPanel
{
    private const float RowHeight = 70f;
    private const float CompactRowHeight = 56f;
    private const float SliderWidth = 220f;
    private const float ValueWidth = 64f;

    private Rows _row;
    private bool _confirmReset;

    /// <summary>Vertical layout cursor for the current page.</summary>
    private struct Rows(ControlPanel panel, float left, float width)
    {
        public readonly ControlPanel Panel = panel;
        public readonly float Left = left;
        public readonly float Width = width;
        public float Y;
    }

    private void Section(string title)
    {
        _row.Y += 18f;
        _gfx.Text(title, TextStyle.BodyStrong, _row.Left + 2f, _row.Y, _theme.TextPrimary);
        _row.Y += 30f;
    }

    private RectF NextCard(float height)
    {
        var card = new RectF(_row.Left, _row.Y, _row.Width, height);
        _row.Y += height + 4f;
        _gfx.Fill(card, _theme.CardFill, 4f);
        _gfx.Stroke(card, _theme.CardStroke, 1f, 4f);
        return card;
    }

    /// <summary>A settings card with icon, title and description; controls go at its right edge.</summary>
    private RectF Setting(char glyph, string title, string? description, float height = 0f, bool enabled = true,
        float reserve = 320f)
    {
        var card = NextCard(height > 0 ? height : description is null ? CompactRowHeight : RowHeight);
        CardText(card, glyph, title, description, enabled, card.CenterY, reserve);
        return card;
    }

    /// <param name="reserve">Width kept free at the card's right edge for its controls.</param>
    private void CardText(RectF card, char glyph, string title, string? description, bool enabled, float centerY,
        float reserve = 0f)
    {
        var textX = card.X + 16f;
        if (glyph != '\0')
        {
            _gfx.Icon(glyph, new RectF(card.X + 16f, centerY - 10f, 20f, 20f), enabled ? _theme.TextPrimary : _theme.TextDisabled,
                TextStyle.IconLarge);
            textX = card.X + 56f;
        }

        var room = MathF.Max(40f, card.Right - 16f - reserve - 12f - textX);
        if (description is null)
        {
            var (_, height) = _gfx.Measure(title, TextStyle.Body);
            _gfx.Text(title, TextStyle.Body, textX, centerY - height * 0.5f, enabled ? _theme.TextPrimary : _theme.TextDisabled,
                clipWidth: room);
        }
        else
        {
            _gfx.Text(title, TextStyle.Body, textX, centerY - 20f, enabled ? _theme.TextPrimary : _theme.TextDisabled,
                clipWidth: room);
            _gfx.Text(description, TextStyle.Caption, textX, centerY + 1f,
                enabled ? _theme.TextSecondary : _theme.TextDisabled, clipWidth: room);
        }
    }

    private void ToggleSetting(string key, char glyph, string title, string? description, bool value, Action<bool> set,
        bool enabled = true)
    {
        var card = Setting(glyph, title, description, enabled: enabled, reserve: 110f);
        if (_gui.Toggle(Gui.Id(key), card.Right - 16f, card.CenterY, ref value, enabled))
        {
            set(value);
        }
    }

    private void SliderSetting(string key, char glyph, string title, string? description, int value, int min, int max,
        int step, Func<int, string> format, Action<int> set, bool enabled = true)
    {
        var card = Setting(glyph, title, description, enabled: enabled);
        SliderAt(key, card, value, min, max, step, format, set, enabled);
    }

    private void SliderAt(string key, RectF card, int value, int min, int max, int step, Func<int, string> format,
        Action<int> set, bool enabled = true)
    {
        var slider = new RectF(card.Right - 16f - ValueWidth - 8f - SliderWidth, card.CenterY - 16f, SliderWidth, 32f);
        var current = (float)value;
        if (_gui.Slider(Gui.Id(key), slider, ref current, min, max, step, enabled))
        {
            value = (int)MathF.Round(current);
            set(value);
        }

        _gfx.TextIn(format(value), TextStyle.Body, new RectF(card.Right - 16f - ValueWidth, card.Y, ValueWidth, card.H),
            enabled ? _theme.TextSecondary : _theme.TextDisabled, 1f);
    }

    private static string Percent(int value) => $"{value}%";

    // ---- Pages ----

    private void AppearancePage()
    {
        Section(Loc.T("配色", "Color"));
        StyleGallery();
        if (S.Style == TrailStyleId.Custom)
        {
            CustomColorsCard();
        }

        SliderSetting("look.colorSpeed", '\uE9E9', Loc.T("色彩流动", "Color flow"), Loc.T("颜色沿轨迹变化的快慢", "How quickly the colors change along the trail"), S.ColorSpeed, 25, 400, 5,
            v => $"{v / 100f:0.00}×", v => S.ColorSpeed = v);

        Section(Loc.T("形态", "Shape"));
        SliderSetting("look.length", '\uE916', Loc.T("轨迹长度", "Length"), Loc.T("光迹停留的时间", "How long the trail lingers"), S.LengthMs, 200, 3000, 50,
            v => Loc.T($"{v / 1000f:0.00} 秒", $"{v / 1000f:0.00} s"), v => S.LengthMs = v);
        SliderSetting("look.width", '\uE76F', Loc.T("轨迹粗细", "Width"), null, S.WidthPercent, 40, 250, 5, Percent, v => S.WidthPercent = v);
        SliderSetting("look.opacity", '\uE793', Loc.T("浓淡", "Opacity"), Loc.T("整体不透明度，粒子与点击效果一起变化", "Overall opacity, particles and effects included"), S.Opacity, 10, 100, 5, Percent,
            v => S.Opacity = v);
        SliderSetting("look.glow", '\uE706', Loc.T("光晕", "Glow"), Loc.T("丝带周围柔和的辉光", "The soft glow around the ribbon"), S.Glow, 0, 200, 5, Percent, v => S.Glow = v);
        SliderSetting("look.core", '\uEA80', Loc.T("中心高光", "Core highlight"), Loc.T("丝带中央的白色亮线", "The white-hot line along the middle"), S.Core, 0, 200, 5, Percent, v => S.Core = v);
        ToggleSetting("look.speed", '\uEC4A', Loc.T("随速度变化粗细", "Width follows speed"), Loc.T("快速划动时更粗，慢慢移动时更细", "Bolder on quick flicks, finer on slow moves"), S.SpeedResponse,
            v => S.SpeedResponse = v);
        ToggleSetting("look.taper", '\uE73F', Loc.T("尾部收窄", "Tapered tail"), Loc.T("尾巴在变淡的同时逐渐变细", "The tail narrows as it fades"), S.Taper, v => S.Taper = v);
    }

    private void StyleGallery()
    {
        const float TileWidth = 124f;
        const float TileHeight = 100f;
        const float Gap = 8f;
        const float Header = 58f;
        var styles = TrailStyles.All.Select(style => (style.Id, style.Name, Style: style))
            .Append((TrailStyleId.Custom, Loc.T("自定义", "Custom"), S.Style == TrailStyleId.Custom ? S.BaseStyle() : CustomPreviewStyle()))
            .ToArray();
        var perRow = Math.Max(1, (int)((_row.Width - 32f + Gap) / (TileWidth + Gap)));
        var rows = (styles.Length + perRow - 1) / perRow;
        var card = NextCard(Header + rows * (TileHeight + Gap) + 10f);
        CardText(card, '\uE790', Loc.T("流光配色", "Trail colors"), Loc.T("选择预设，或用自己的颜色", "Pick a preset or use your own colors"), true, card.Y + Header * 0.5f + 2f);
        for (var i = 0; i < styles.Length; i++)
        {
            var (id, name, style) = styles[i];
            var tile = new RectF(card.X + 16f + i % perRow * (TileWidth + Gap), card.Y + Header + i / perRow * (TileHeight + Gap),
                TileWidth, TileHeight);
            var key = id == TrailStyleId.Custom ? "style.custom:" + string.Join(",", S.CustomColors) + S.CustomFlow : "style." + id;
            var art = _gfx.Rendering ? _art.Get(_gfx, key, tile.W - 12f, 58f, Swoosh, SwatchConfig(style)) : null;
            if (Tile(Gui.Id("style", (int)id), tile, S.Style == id, art, 58f, name))
            {
                if (S.Style != id)
                {
                    S.Style = id;
                    S.Enabled = true;
                    _gui.Changed = _gui.Committed = true;
                    StyleChanged();
                }
            }
        }
    }

    private TrailStyle CustomPreviewStyle() => TrailStyles.Custom([.. S.CustomColors.Select(ColorText.ParseOrDefault)], S.CustomFlow);

    private static RenderConfig SwatchConfig(TrailStyle style) => new()
    {
        Style = style,
        Lifetime = 0.9,
        Width = 1.15f,
        Opacity = 0.95f
    };

    private static void Swoosh(TileArt.Scene scene)
    {
        var w = scene.Width;
        var h = scene.Height;
        scene.Stroke(t => (w * (0.1f + 0.8f * t), h * (0.72f - 0.44f * t) + h * 0.16f * MathF.Sin(MathF.Tau * t)), 0.5);
    }

    /// <summary>A selectable gallery tile with a picture above its label.</summary>
    private bool Tile(int id, RectF rect, bool selected, void* art, float artHeight, string label)
    {
        var clicked = _gui.Behavior(id, rect, out var hover, out var held);
        _gfx.Fill(rect, held ? _theme.ControlFillPressed : hover ? _theme.ControlFillHover : _theme.ControlFill, 6f);
        _gfx.Stroke(rect, _theme.ControlStroke, 1f, 6f);
        var picture = new RectF(rect.X + 6f, rect.Y + 6f, rect.W - 12f, artHeight);
        _gfx.FillGradient(picture, 4f, [(0f, _theme.StageTop), (1f, _theme.StageBottom)], true);
        if (art != null)
        {
            _gfx.DrawBitmap(art, picture);
        }

        _gfx.TextIn(label, selected ? TextStyle.BodyStrong : TextStyle.Body,
            new RectF(rect.X, picture.Bottom + 2f, rect.W, rect.Bottom - picture.Bottom - 2f),
            held ? _theme.TextSecondary : _theme.TextPrimary, 0.5f);
        var ring = _gui.Animate(id + 7, selected ? 1f : 0f, 18f);
        if (ring > 0.01f)
        {
            _gfx.Stroke(rect, Theme.Fade(_theme.Accent, ring), 2f, 6f);
            var badge = new RectF(rect.Right - 22f, rect.Y + 10f, 16f, 16f);
            _gfx.FillEllipse(badge.CenterX, badge.CenterY, 8f * ring, 8f * ring, _theme.Accent);
            _gfx.Icon('\uE73E', badge, Theme.Fade(_theme.TextOnAccent, ring), TextStyle.IconSmall);
        }

        _gui.FocusRing(id, rect, 6f);
        return clicked;
    }

    private void CustomColorsCard()
    {
        const float Chip = 36f;
        var card = NextCard(RowHeight + 8f);
        CardText(card, '\uE771', Loc.T("自定义颜色", "Custom colors"), Loc.T("点击色块修改，最多 6 种", "Click a swatch to change it, up to 6"), true, card.CenterY);
        var count = S.CustomColors.Count;
        var flowWidth = 112f;
        var x = card.Right - 16f - flowWidth - 16f - (count + (count < ColorText.MaxStops ? 1 : 0)) * (Chip + 6f);
        for (var i = 0; i < count; i++)
        {
            var chip = new RectF(x + i * (Chip + 6f), card.CenterY - Chip * 0.5f, Chip, Chip);
            var color = ColorText.ParseOrDefault(S.CustomColors[i]);
            var id = Gui.Id("custom.stop", i);
            var clicked = _gui.Behavior(id, chip, out var hover, out var held);
            _gfx.Fill(chip, 0xFF000000u | color, 6f);
            _gfx.Stroke(chip, hover || _popupOwner == id ? _theme.ControlStrongStroke : _theme.ControlStroke, hover ? 2f : 1f, 6f);
            _gui.FocusRing(id, chip, 6f);
            if (clicked)
            {
                OpenColor(id, chip, ColorSlot.CustomStop, i);
            }
        }

        if (count < ColorText.MaxStops)
        {
            var add = new RectF(x + count * (Chip + 6f), card.CenterY - Chip * 0.5f, Chip, Chip);
            var id = Gui.Id("custom.add");
            var clicked = _gui.Behavior(id, add, out var hover, out var held);
            _gfx.Fill(add, held ? _theme.ControlFillPressed : hover ? _theme.ControlFillHover : _theme.ControlFill, 6f);
            _gfx.Stroke(add, _theme.ControlStrongStroke, 1f, 6f);
            _gfx.Icon('\uE710', add, _theme.TextSecondary);
            _gui.FocusRing(id, add, 6f);
            if (clicked)
            {
                // A new stop continues the gradient: halfway between the last colour and the first.
                var first = ColorText.ParseOrDefault(S.CustomColors[0]);
                var last = ColorText.ParseOrDefault(S.CustomColors[^1]);
                S.CustomColors.Add(ColorText.Format(Palette.Mix(last, first, 0.5f)));
                _gui.Changed = _gui.Committed = true;
                StyleChanged();
                OpenColor(Gui.Id("custom.stop", count), add, ColorSlot.CustomStop, count);
            }
        }

        var flow = new RectF(card.Right - 16f - flowWidth, card.CenterY - 16f, flowWidth, 32f);
        Combo("custom.flow", flow, [Loc.T("往返渐变", "Back and forth"), Loc.T("循环渐变", "Loop")], S.CustomFlow == ColorFlow.PingPong ? 0 : 1, index =>
        {
            S.CustomFlow = index == 0 ? ColorFlow.PingPong : ColorFlow.Loop;
            StyleChanged();
        });
    }

    private void ParticlesPage()
    {
        ToggleSetting("particles.on", '\uF4A5', Loc.T("显示粒子", "Show particles"), Loc.T("光迹经过的地方洒落粒子", "Scatter particles along the trail"), S.Sparkles, v => S.Sparkles = v);
        ParticleGallery();
        SliderSetting("particles.density", '\uE8FD', Loc.T("数量", "Amount"), null, S.ParticleDensity, 25, 300, 5, Percent,
            v => S.ParticleDensity = v, S.Sparkles);
        SliderSetting("particles.size", '\uEA3A', Loc.T("大小", "Size"), null, S.ParticleSize, 50, 250, 5, Percent,
            v => S.ParticleSize = v, S.Sparkles);
        SliderSetting("particles.drift", '\uE9F3', Loc.T("飘散", "Drift"), Loc.T("粒子飞散和飘动的速度", "How fast particles scatter and drift"), S.ParticleDrift, 0, 300, 5, Percent,
            v => S.ParticleDrift = v, S.Sparkles);
        var card = Setting('\uE790', Loc.T("粒子颜色", "Particle color"), Loc.T("跟随光迹颜色，或使用固定颜色", "Match the trail's colors or use a fixed color"), enabled: S.Sparkles);
        ColorButton("particles.color", card, ColorSlot.Particle, S.ParticleColor);
    }

    private void ParticleGallery()
    {
        const float TileWidth = 104f;
        const float TileHeight = 100f;
        const float Gap = 8f;
        const float Header = 58f;
        var kinds = Enum.GetValues<ParticleKind>();
        var perRow = Math.Max(1, (int)((_row.Width - 32f + Gap) / (TileWidth + Gap)));
        var rows = (kinds.Length + perRow - 1) / perRow;
        var card = NextCard(Header + rows * (TileHeight + Gap) + 10f);
        CardText(card, '\uE734', Loc.T("粒子样式", "Particle style"), Loc.T("选择后会自动开启粒子", "Picking one turns particles on"), true, card.Y + Header * 0.5f + 2f);
        var style = _config.Style;
        for (var i = 0; i < kinds.Length; i++)
        {
            var kind = kinds[i];
            var tile = new RectF(card.X + 16f + i % perRow * (TileWidth + Gap), card.Y + Header + i / perRow * (TileHeight + Gap),
                TileWidth, TileHeight);
            var key = $"particle.{kind}:{style.Name}:{string.Join(",", style.Stops)}:{S.ParticleColor}";
            var art = _gfx.Rendering
                ? _art.Get(_gfx, key, tile.W - 12f, 58f, ParticleSwoosh, ParticleConfig(style, kind))
                : null;
            if (Tile(Gui.Id("particle", i), tile, S.Sparkles && S.ParticleKind == kind, art, 58f, TrailOptions.Label(kind)))
            {
                S.ParticleKind = kind;
                S.Sparkles = true;
                _gui.Changed = _gui.Committed = true;
            }
        }
    }

    private RenderConfig ParticleConfig(TrailStyle style, ParticleKind kind) => new()
    {
        Style = style,
        Lifetime = 0.8,
        Width = 0.9f,
        Opacity = 0.95f,
        Particles = new ParticleOptions(true, kind, 2.4f, 1f, 1f, ColorText.ToEffectColor(S.ParticleColor))
    };

    private static void ParticleSwoosh(TileArt.Scene scene)
    {
        var w = scene.Width;
        var h = scene.Height;
        scene.Stroke(t => (w * (0.08f + 0.84f * t), h * (0.66f - 0.3f * t) + h * 0.12f * MathF.Sin(MathF.Tau * t)), 0.42);
        scene.Wait(0.12);
    }

    private void ClicksPage()
    {
        Section(Loc.T("鼠标按键", "Mouse buttons"));
        ClickSetting("click.left", Loc.T("左键", "Left button"), Loc.T("单击左键时在指针处显示", "Shown at the pointer on a left click"), S.LeftClick, v => S.LeftClick = v, ColorSlot.Left,
            S.LeftClickColor);
        ClickSetting("click.right", Loc.T("右键", "Right button"), Loc.T("单击右键时在指针处显示", "Shown at the pointer on a right click"), S.RightClick, v => S.RightClick = v, ColorSlot.Right,
            S.RightClickColor);
        ClickSetting("click.middle", Loc.T("中键", "Middle button"), Loc.T("按下滚轮时在指针处显示", "Shown at the pointer when the wheel is pressed"), S.MiddleClick, v => S.MiddleClick = v, ColorSlot.Middle,
            S.MiddleClickColor);
        SliderSetting("click.size", '\uE8A3', Loc.T("效果大小", "Effect size"), Loc.T("点击与滚轮效果的尺寸", "Size of click and wheel effects"), S.EffectSize, 50, 250, 5, Percent,
            v => S.EffectSize = v);

        Section(Loc.T("滚轮", "Wheel"));
        var card = Setting('\uEC8F', Loc.T("滚动效果", "Scroll effect"), Loc.T("滚动时在指针旁提示方向", "Shows the scroll direction next to the pointer"));
        var effects = Enum.GetValues<WheelEffect>();
        var colorRight = ColorButton("wheel.color", card, ColorSlot.Wheel, S.WheelColor, S.Wheel != WheelEffect.None);
        Combo("wheel.effect", new RectF(colorRight - 8f - 150f, card.CenterY - 16f, 150f, 32f),
            [.. effects.Select(TrailOptions.Label)], Array.IndexOf(effects, S.Wheel), index => S.Wheel = effects[index]);
        Note(Loc.T("滚轮没有可以直接读取的状态：开启滚动效果后，程序会一直接收系统的鼠标输入通知（只看滚轮，不记录任何内容），CPU 占用会略有增加。", "The wheel cannot be polled, so with a scroll effect on the app keeps receiving the system's mouse input notifications (it only looks at the wheel and records nothing). CPU use rises slightly."));
    }

    private void ClickSetting(string key, string title, string description, ClickEffect value, Action<ClickEffect> set,
        ColorSlot slot, string? color)
    {
        var card = Setting('\uE962', title, description);
        var effects = Enum.GetValues<ClickEffect>();
        var colorRight = ColorButton(key + ".color", card, slot, color, value != ClickEffect.None);
        Combo(key, new RectF(colorRight - 8f - 150f, card.CenterY - 16f, 150f, 32f), [.. effects.Select(TrailOptions.Label)],
            Array.IndexOf(effects, value), index => set(effects[index]));
    }

    /// <summary>Secondary explanatory text under a group of cards.</summary>
    private void Note(string text)
    {
        _row.Y += 4f;
        var (_, height) = _gfx.Text(text, TextStyle.Caption, _row.Left + 2f, _row.Y, _theme.TextSecondary, _row.Width - 4f);
        _row.Y += height + 8f;
    }

    private void PointerPage()
    {
        Section(Loc.T("光迹发出的位置", "Where the trail starts"));
        OriginGallery();
        if (S.Origin == TrailOrigin.Custom)
        {
            SliderSetting("origin.x", '\uE8AB', Loc.T("水平偏移", "Horizontal offset"), Loc.T("相对指针尖端，向右为正", "From the pointer tip, positive is right"), S.OffsetX, -48, 48, 1, v => $"{v} px",
                v => S.OffsetX = v);
            SliderSetting("origin.y", '\uE8CB', Loc.T("垂直偏移", "Vertical offset"), Loc.T("相对指针尖端，向下为正", "From the pointer tip, positive is down"), S.OffsetY, -48, 48, 1, v => $"{v} px",
                v => S.OffsetY = v);
        }

        Section(Loc.T("跟随", "Tracking"));
        SliderSetting("track.smoothing", '\uEDFB', Loc.T("平滑度", "Smoothing"), Loc.T("越高越顺滑圆润，越低越紧贴指针", "Higher is rounder and smoother, lower follows more tightly"), S.Smoothing, 0, 100, 5,
            v => v switch { < 25 => Loc.T("跟手", "Tight"), < 45 => Loc.T("偏跟手", "Tighter"), <= 55 => Loc.T("标准", "Standard"), < 80 => Loc.T("偏顺滑", "Smoother"), _ => Loc.T("顺滑", "Smooth") },
            v => S.Smoothing = v);

        Section(Loc.T("显示", "Display"));
        ToggleSetting("show.fullscreen", '\uE740', Loc.T("全屏应用中自动隐藏", "Hide in full-screen apps"), Loc.T("游戏、视频和演示全屏时不显示", "Stays out of games, videos and slideshows"), S.HideInFullscreen,
            v => S.HideInFullscreen = v);
        ToggleSetting("show.selecting", '\uE8D2', Loc.T("拖选文字时隐藏", "Hide while selecting text"), Loc.T("按住左键选择文字时，光迹不会盖住选区", "Keeps the trail off text you are selecting"), S.HideWhileSelecting,
            v => S.HideWhileSelecting = v);
    }

    private void OriginGallery()
    {
        const float TileWidth = 150f;
        const float TileHeight = 118f;
        const float Gap = 8f;
        const float Header = 58f;
        var origins = Enum.GetValues<TrailOrigin>();
        var perRow = Math.Max(1, (int)((_row.Width - 32f + Gap) / (TileWidth + Gap)));
        var rows = (origins.Length + perRow - 1) / perRow;
        var card = NextCard(Header + rows * (TileHeight + Gap) + 10f);
        CardText(card, '\uE8B0', Loc.T("拖尾发生点", "Trail origin"), Loc.T("光迹从指针的哪个位置发出", "Where on the pointer the trail starts"), true, card.Y + Header * 0.5f + 2f);
        for (var i = 0; i < origins.Length; i++)
        {
            var origin = origins[i];
            var tile = new RectF(card.X + 16f + i % perRow * (TileWidth + Gap), card.Y + Header + i / perRow * (TileHeight + Gap),
                TileWidth, TileHeight);
            var (offsetX, offsetY) = OriginOffset(origin);
            var pictureHeight = 76f;
            var pointerX = (tile.W - 12f) * 0.6f;
            var pointerY = pictureHeight * 0.3f;
            var cursorBase = PointerImage.CursorBaseScale();
            var key = $"origin.{origin}:{offsetX}:{offsetY}:{cursorBase}:{_config.Style.Name}:{string.Join(",", _config.Style.Stops)}";
            var art = _gfx.Rendering
                ? _art.Get(_gfx, key, tile.W - 12f, pictureHeight, scene =>
                {
                    var w = scene.Width;
                    var h = scene.Height;
                    scene.Stroke(t =>
                    {
                        var eased = 1f - (1f - t) * (1f - t);
                        return (w * 0.08f + (pointerX - w * 0.08f) * eased,
                            h * 0.86f + (pointerY - h * 0.86f) * eased + h * 0.1f * MathF.Sin(MathF.PI * t));
                    }, 0.45, offsetX * cursorBase, offsetY * cursorBase);
                }, SwatchConfig(_config.Style))
                : null;
            if (Tile(Gui.Id("origin", i), tile, S.Origin == origin, art, pictureHeight, TrailOptions.Label(origin)))
            {
                S.Origin = origin;
                _gui.Changed = _gui.Committed = true;
            }

            _pointer.Draw(_gfx, tile.X + 6f + pointerX, tile.Y + 6f + pointerY);

            // Mark where the ribbon starts, on top of the pointer.
            var markX = tile.X + 6f + pointerX + offsetX * cursorBase;
            var markY = tile.Y + 6f + pointerY + offsetY * cursorBase;
            _gfx.FillEllipse(markX, markY, 4.5f, 4.5f, 0xE6FFFFFFu);
            _gfx.FillEllipse(markX, markY, 3f, 3f, 0xFF000000u | _config.Style.ColorAt(0.1f));
        }
    }

    private (float X, float Y) OriginOffset(TrailOrigin origin) => origin switch
    {
        TrailOrigin.Tip => (0f, 0f),
        TrailOrigin.Center => CursorGeometry.CenterOffset(PointerImage.Arrow),
        TrailOrigin.Custom => (S.OffsetX, S.OffsetY),
        _ => (8f, 14f)
    };

    private void GeneralPage()
    {
        ToggleSetting("general.enabled", '\uE7E8', Loc.T("启用鼠标流光", "Enable Mouse Glow Trail"), Loc.T("随时可按 Ctrl + Alt + T 暂停或继续", "Press Ctrl + Alt + T any time to pause or resume"), S.Enabled,
            v => S.Enabled = v);
        var startup = _host.StartupEnabled;
        ToggleSetting("general.startup", '\uE7B5', Loc.T("开机自动启动", "Start with Windows"), Loc.T("登录 Windows 后在后台运行", "Runs in the background after you sign in"), startup,
            v => _host.StartupEnabled = v);
        ToggleSetting("general.gpu", '\uE950', Loc.T("GPU 加速", "GPU acceleration"), _host.RendererStatus, S.GpuAcceleration,
            v => S.GpuAcceleration = v);
        Note(Loc.T("GPU 加速让显卡负责绘制，画面完全相同。轨迹越粗、越长，越能节省 CPU；代价是显卡驱动约多占 40–50 MB 内存。纤细、较短的轨迹用 CPU 绘制最划算。", "The graphics card draws the trail instead; the picture is identical. The bolder and longer the trail, the more CPU it saves, at the cost of about 40–50 MB more memory for the graphics driver. Thin, short trails are cheapest on the CPU."));

        // Always bilingual, so it can be found whichever language is showing.
        var language = Setting('\uF2B7', "语言 · Language", null, reserve: 200f);
        var languages = new[] { UiLanguage.Auto, UiLanguage.Chinese, UiLanguage.English };
        Combo("general.language", new RectF(language.Right - 16f - 170f, language.CenterY - 16f, 170f, 32f),
            [Loc.T("跟随系统", "System default"), "简体中文", "English"], Array.IndexOf(languages, S.Language), index =>
            {
                S.Language = languages[index];
                Loc.Apply(S.Language);
                LanguageChanged();
            });

        Section(Loc.T("快捷键", "Keyboard shortcuts"));
        Shortcut(Loc.T("暂停 / 继续", "Pause / resume"), ["Ctrl", "Alt", "T"]);
        Shortcut(Loc.T("退出", "Quit"), ["Ctrl", "Alt", "Q"]);

        Section(Loc.T("关于", "About"));
        var about = NextCard(112f);
        DrawAppIconAt(new RectF(about.X + 20f, about.Y + 20f, 40f, 40f));
        var version = typeof(ControlPanel).Assembly.GetName().Version;
        _gfx.Text(Loc.T($"鼠标流光 {version?.Major}.{version?.Minor}.{version?.Build}", $"Mouse Glow Trail {version?.Major}.{version?.Minor}.{version?.Build}"), TextStyle.BodyStrong, about.X + 76f,
            about.Y + 20f, _theme.TextPrimary);
        _gfx.Text(Loc.T("轻量、零干扰的鼠标流光轨迹。不联网，不收集任何数据。", "A light, unobtrusive glowing trail for your mouse. No network access, no data collected."), TextStyle.Caption, about.X + 76f, about.Y + 44f,
            _theme.TextSecondary);
        var folder = new RectF(about.X + 76f, about.Y + 68f, 140f, 32f);
        if (_gui.Button(Gui.Id("general.folder"), folder, Loc.T("打开设置文件夹", "Open settings folder")))
        {
            Directory.CreateDirectory(_host.SettingsFolder);
            ShellExecuteW(_hwnd, "open", _host.SettingsFolder, null, null, SW_SHOWNORMAL);
        }

        var reset = new RectF(folder.Right + 8f, folder.Y, 140f, 32f);
        if (_gui.Button(Gui.Id("general.reset"), reset, _confirmReset ? Loc.T("确定恢复？", "Restore now?") : Loc.T("恢复默认设置", "Restore defaults"), accent: _confirmReset))
        {
            if (_confirmReset)
            {
                KillTimer(_hwnd, ConfirmTimer);
                _confirmReset = false;
                ResetSettings();
            }
            else
            {
                _confirmReset = true;
                SetTimer(_hwnd, ConfirmTimer, 4000, 0);
            }
        }
    }

    private void ResetSettings()
    {
        S.CopyFrom(S.WithDefaults());

        _gui.Changed = _gui.Committed = true;
        StyleChanged();
    }

    private void Shortcut(string title, string[] keys)
    {
        var card = Setting('\uE765', title, null, reserve: 200f);
        var x = card.Right - 16f;
        for (var i = keys.Length - 1; i >= 0; i--)
        {
            var (width, _) = _gfx.Measure(keys[i], TextStyle.Body);
            var cap = new RectF(x - width - 20f, card.CenterY - 15f, width + 20f, 30f);
            _gfx.Fill(cap, _theme.ControlFill, 4f);
            _gui.ControlBorder(cap, 4f);
            _gfx.TextIn(keys[i], TextStyle.Body, cap, _theme.TextPrimary, 0.5f);
            x = cap.X - 6f;
        }
    }

    private void DrawAppIconAt(RectF rect)
    {
        if (!_gfx.Rendering)
        {
            return;
        }

        if (_appIconLarge == null || _largeIconGeneration != _gfx.Generation)
        {
            D2D.Release(ref _appIconLarge);
            _largeIconGeneration = _gfx.Generation;
            _appIconLarge = IconBitmap((int)MathF.Round(rect.W * _gfx.Scale));
        }

        _gfx.DrawBitmap(_appIconLarge, rect);
    }
}
