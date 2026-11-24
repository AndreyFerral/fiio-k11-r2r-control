using System.Drawing.Drawing2D;
using System.Drawing.Text;
using K11Control.Config;

namespace K11Control.UI;

internal enum ButtonVariant
{
    Primary,
    Secondary,
    Amber,
    Danger
}

internal enum NavIconKind
{
    Output,
    Lcd,
    Rgb,
    Hotkeys,
    Diagnostics
}

internal enum PlayGlyphKind
{
    None,
    Play,
    Pause
}

internal sealed class RoundedCardPanel : Panel
{
    public float CornerRadius { get; set; } = 10f;
    public Color CardColor { get; set; } = Theme.BgCard;
    public Color BorderColor { get; set; } = Theme.BorderSubtle;

    public RoundedCardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Padding = new Padding(16);
    }

    private Color ResolveParentColor()
    {
        Control? p = Parent;
        while (p is not null)
        {
            if (p is RoundedCardPanel card)
                return card.CardColor;
            if (p.BackColor != Color.Transparent)
                return p.BackColor;
            p = p.Parent;
        }
        return Theme.BgWindow;
    }

    private void PaintCardSurface(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ResolveParentColor());
        var rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using var path = Theme.CreateRoundedPath(rect, CornerRadius);
        using var fill = new SolidBrush(CardColor);
        g.FillPath(fill, path);
        if (BorderColor != Color.Transparent)
        {
            using var pen = new Pen(BorderColor, 1f);
            g.DrawPath(pen, path);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PaintCardSurface(e.Graphics);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintCardSurface(e.Graphics);
        base.OnPaint(e);
    }
}

internal sealed class ModernButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public ButtonVariant Variant { get; set; } = ButtonVariant.Secondary;
    public string? BadgeText { get; set; }
    public float CornerRadius { get; set; } = 7f;
    public bool ShowResetIcon { get; set; }
    public bool ShowClearIcon { get; set; }
    public bool ShowDotsIcon { get; set; }
    private Color? _borderColorOverride;
    private Color? _foreColorOverride;
    public Color? BorderColorOverride { get => _borderColorOverride; set { _borderColorOverride = value; Invalidate(); } }
    public Color? ForeColorOverride { get => _foreColorOverride; set { _foreColorOverride = value; Invalidate(); } }
    private PlayGlyphKind _playGlyph = PlayGlyphKind.None;
    public PlayGlyphKind PlayGlyph { get => _playGlyph; set { _playGlyph = value; Invalidate(); } }

    public ModernButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Theme.FontBodyBold;
        Size = new Size(130, 34);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgWindow);
        if (parentBg == Color.Transparent) parentBg = Theme.BgWindow;
        g.Clear(parentBg);

        (Color bg, Color border, Color fg) = GetColors();
        var rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using var path = Theme.CreateRoundedPath(rect, CornerRadius);
        using var fill = new SolidBrush(bg);
        using var pen = new Pen(border, 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);

        if (ShowResetIcon)
        {
            Color iconCol = Enabled ? Theme.Danger : Theme.TextMuted;
            float r = 4.2f;
            float cx = 14f;
            float cy = Height / 2f;
            using (var arcPen = new Pen(iconCol, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arcPen, cx - r, cy - r, r * 2f, r * 2f, -35f, 290f);

            // Crisp arrowhead at the open upper-right tip of the reset arc
            PointF tip = new(cx + r * 0.88f, cy - r * 0.55f);
            using var head = new Pen(iconCol, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(head, tip.X - 3.2f, tip.Y - 0.3f, tip.X, tip.Y);
            g.DrawLine(head, tip.X + 0.3f, tip.Y - 3.0f, tip.X, tip.Y);

            var textRect = new Rectangle(23, 0, Math.Max(20, Width - 29), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else if (ShowClearIcon)
        {
            float cx = (Width - 1f) / 2f;
            float cy = (Height - 1f) / 2f;
            const float half = 3.3f;
            using var xPen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(xPen, cx - half, cy - half, cx + half, cy + half);
            g.DrawLine(xPen, cx - half, cy + half, cx + half, cy - half);
        }
        else if (ShowDotsIcon)
        {
            float cx = (Width - 1f) / 2f;
            float cy = (Height - 1f) / 2f;
            const float r = 1.55f;
            const float step = 5.0f;
            using var br = new SolidBrush(fg);
            for (int d = -1; d <= 1; d++)
                g.FillEllipse(br, cx + d * step - r, cy - r, r * 2f, r * 2f);
        }
        else if (_playGlyph != PlayGlyphKind.None)
        {
            if (Enabled && (_hovered || _playGlyph == PlayGlyphKind.Pause))
            {
                var innerRect = new RectangleF(4f, 4f, Width - 8f, Height - 8f);
                using var innerPath = Theme.CreateRoundedPath(innerRect, 6f);
                Color innerFill = _hovered
                    ? Theme.BgElevated
                    : Theme.AccentSoft;
                using var pillBg = new SolidBrush(innerFill);
                g.FillPath(pillBg, innerPath);
            }

            float cx = (Width - 1f) / 2f;
            float cy = (Height - 1f) / 2f;
            using var brush = new SolidBrush(fg);
            if (_playGlyph == PlayGlyphKind.Play)
            {
                PointF[] tri =
                [
                    new(cx - 3.5f, cy - 5.0f),
                    new(cx + 5.0f, cy),
                    new(cx - 3.5f, cy + 5.0f)
                ];
                g.FillPolygon(brush, tri);
            }
            else
            {
                g.FillRectangle(brush, cx - 4.2f, cy - 4.5f, 2.8f, 9f);
                g.FillRectangle(brush, cx + 1.4f, cy - 4.5f, 2.8f, 9f);
            }
        }
        else if (string.IsNullOrEmpty(BadgeText))
        {
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(rect), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        else
        {
            Size badgeSize = TextRenderer.MeasureText(BadgeText, Theme.FontSmall);
            int bw = badgeSize.Width + 10;
            int bh = 20;
            var leftRect = new Rectangle(12, 0, Math.Max(20, Width - bw - 26), Height);
            TextRenderer.DrawText(g, Text, Font, leftRect, fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            var badgeRect = new RectangleF(Width - bw - 10, (Height - bh) / 2f, bw, bh);
            using var badgePath = Theme.CreateRoundedPath(badgeRect, 5f);
            using var badgeBg = new SolidBrush(Color.FromArgb(45, 255, 255, 255));
            g.FillPath(badgeBg, badgePath);
            TextRenderer.DrawText(g, BadgeText, Theme.FontSmall, Rectangle.Round(badgeRect), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private (Color Bg, Color Border, Color Fg) GetColors()
    {
        if (!Enabled)
            return (Theme.BgInput, Theme.BorderSubtle, Theme.TextMuted);

        if (ShowResetIcon)
        {
            Color sidBg = _pressed
                ? Color.FromArgb(44, 22, 26)
                : (_hovered ? Color.FromArgb(58, 25, 30) : Theme.BgInput);
            Color sidBorder = _hovered ? Theme.Danger : Color.FromArgb(135, Theme.Danger);
            return (sidBg, sidBorder, Theme.TextPrimary);
        }

        if (ShowClearIcon)
        {
            Color clrBg = _pressed
                ? Color.FromArgb(44, 22, 26)
                : (_hovered ? Color.FromArgb(56, 28, 34) : Theme.BgCard);
            Color clrBorder = _hovered ? Color.FromArgb(165, Theme.Danger) : Theme.BorderSubtle;
            Color clrFg = _hovered ? Color.FromArgb(254, 202, 202) : Theme.TextSecondary;
            return (clrBg, clrBorder, clrFg);
        }

        if (_playGlyph != PlayGlyphKind.None)
        {
            if (!Enabled)
                return (Theme.BgInput, Theme.BorderSubtle, Color.FromArgb(62, 70, 88));

            Color playBorder = _hovered ? Theme.BorderStrong : Theme.BorderSubtle;
            Color playFg = _playGlyph == PlayGlyphKind.Pause ? Theme.Accent : Theme.TextPrimary;
            return (Theme.BgInput, playBorder, playFg);
        }

        Color baseBg, hoverBg, border, fg;
        switch (Variant)
        {
            case ButtonVariant.Primary:
                baseBg = Theme.Accent;
                hoverBg = Theme.AccentHover;
                border = Theme.AccentHover;
                fg = Color.FromArgb(10, 14, 20);
                break;
            case ButtonVariant.Amber:
                baseBg = Theme.AccentAmber;
                hoverBg = Theme.AccentAmberHover;
                border = Theme.AccentAmberHover;
                fg = Color.FromArgb(16, 12, 6);
                break;
            case ButtonVariant.Danger:
                baseBg = Color.FromArgb(54, 24, 28);
                hoverBg = Color.FromArgb(84, 30, 35);
                border = Theme.Danger;
                fg = Color.FromArgb(254, 202, 202);
                break;
            default:
                baseBg = Theme.BgElevated;
                hoverBg = Color.FromArgb(48, 55, 72);
                border = Theme.BorderStrong;
                fg = Theme.TextPrimary;
                break;
        }

        Color actual = _pressed ? Theme.Blend(baseBg, Color.Black, 0.16f) : (_hovered ? hoverBg : baseBg);
        return (actual, _borderColorOverride ?? border, _foreColorOverride ?? fg);
    }
}

internal static class SegmentedCapsulePainter
{
    public static int HitTest(int width, int count, int x)
    {
        if (count <= 0 || width <= 8)
            return -1;
        float segW = (width - 8f) / count;
        int idx = (int)Math.Floor((x - 4f) / segW);
        return idx >= 0 && idx < count ? idx : -1;
    }

    public static void Paint(
        Graphics g,
        Control host,
        int count,
        int selectedIndex,
        int hoverIndex,
        Func<int, bool> isAvailable,
        Func<int, string> getLabel,
        Func<int, Color?>? getAccentOverride = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = host.Parent is RoundedCardPanel card ? card.CardColor : (host.Parent?.BackColor ?? Theme.BgWindow);
        if (parentBg == Color.Transparent) parentBg = Theme.BgWindow;
        g.Clear(parentBg);

        var outer = new RectangleF(0.5f, 0.5f, host.Width - 1.5f, host.Height - 1.5f);
        using (var path = Theme.CreateRoundedPath(outer, 8f))
        using (var bg = new SolidBrush(Theme.BgInput))
        using (var border = new Pen(Theme.BorderSubtle, 1f))
        {
            g.FillPath(bg, path);
            g.DrawPath(border, path);
        }

        if (count <= 0)
            return;

        float segW = (host.Width - 8f) / count;
        for (int i = 0; i < count; i++)
        {
            bool avail = isAvailable(i);
            bool active = avail && i == selectedIndex;
            bool hovered = avail && !active && i == hoverIndex && host.Enabled;
            var segRect = new RectangleF(4f + i * segW, 4f, segW - 2f, host.Height - 8f);

            if (active || hovered)
            {
                Color accent = getAccentOverride?.Invoke(i) ?? Theme.Accent;
                Color fillCol = active
                    ? (host.Enabled ? accent : Theme.BorderStrong)
                    : Theme.BgElevated;
                using var segPath = Theme.CreateRoundedPath(segRect, 6f);
                using var fill = new SolidBrush(fillCol);
                g.FillPath(fill, segPath);
            }

            Color textColor = !avail
                ? Color.FromArgb(62, 70, 88)
                : active
                    ? (host.Enabled ? Color.FromArgb(12, 16, 22) : Theme.TextSecondary)
                    : (host.Enabled ? (hovered ? Theme.TextPrimary : Theme.TextSecondary) : Theme.TextMuted);

            TextRenderer.DrawText(g, getLabel(i), active ? Theme.FontBodyBold : Theme.FontBody,
                Rectangle.Round(segRect), textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

internal sealed class SegmentedToggle<T> : Control where T : notnull
{
    private readonly List<(T Value, string Label, Color? AccentOverride)> _items = [];
    private int _hoverIndex = -1;
    private T _selected = default!;

    public event EventHandler? SelectedChanged;

    public SegmentedToggle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Theme.FontBodyBold;
        Height = 36;
    }

    public void SetItems(params (T Value, string Label)[] items)
    {
        _items.Clear();
        foreach (var (val, label) in items)
            _items.Add((val, label, null));
        if (_items.Count > 0)
            _selected = _items[0].Value;
        Invalidate();
    }

    public void SetItemsWithAccents(params (T Value, string Label, Color? Accent)[] items)
    {
        _items.Clear();
        _items.AddRange(items);
        if (_items.Count > 0)
            _selected = _items[0].Value;
        Invalidate();
    }

    public T SelectedValue
    {
        get => _selected;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_selected, value))
                return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSelectedSilently(T value)
    {
        _selected = value;
        Invalidate();
    }

    public void UpdateLabels(params (T Value, string Label)[] updates)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            foreach (var (val, label) in updates)
            {
                if (EqualityComparer<T>.Default.Equals(_items[i].Value, val))
                {
                    _items[i] = (_items[i].Value, label, _items[i].AccentOverride);
                    break;
                }
            }
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            int idx = HitTest(e.Location);
            if (idx >= 0 && idx < _items.Count)
                SelectedValue = _items[idx].Value;
        }
        base.OnMouseDown(e);
    }

    private int HitTest(Point pt) => SegmentedCapsulePainter.HitTest(Width, _items.Count, pt.X);

    private int GetSelectedIndex()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(_items[i].Value, _selected))
                return i;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        SegmentedCapsulePainter.Paint(
            e.Graphics,
            this,
            _items.Count,
            GetSelectedIndex(),
            _hoverIndex,
            _ => true,
            i => _items[i].Label,
            i => _items[i].AccentOverride);
    }
}

internal sealed class ModernToggleSwitch : Control
{
    private bool _checked;
    private bool _hovered;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetCheckedSilently(bool value)
    {
        _checked = value;
        Invalidate();
    }

    public ModernToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Theme.FontBody;
        Size = new Size(340, 28);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
            Checked = !Checked;
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgWindow);
        if (parentBg == Color.Transparent) parentBg = Theme.BgWindow;
        g.Clear(parentBg);

        int pillW = 42;
        int pillH = 22;
        var pillRect = new RectangleF(1f, (Height - pillH) / 2f, pillW, pillH);

        Color track = _checked
            ? (Enabled ? (_hovered ? Theme.AccentHover : Theme.Accent) : Theme.BorderStrong)
            : (Enabled ? (_hovered ? Theme.BgElevated : Theme.BgInput) : Theme.BgInput);

        using (var pillPath = Theme.CreateRoundedPath(pillRect, pillH / 2f))
        using (var trackBrush = new SolidBrush(track))
        using (var borderPen = new Pen(_checked ? track : Theme.BorderStrong, 1f))
        {
            g.FillPath(trackBrush, pillPath);
            if (!_checked)
                g.DrawPath(borderPen, pillPath);
        }

        float knobD = 16f;
        float knobX = _checked ? pillRect.Right - knobD - 3f : pillRect.X + 3f;
        float knobY = pillRect.Y + (pillH - knobD) / 2f;
        Color knobColor = Enabled ? (_checked ? Color.FromArgb(14, 18, 26) : Theme.TextPrimary) : Theme.TextMuted;
        using (var knobBrush = new SolidBrush(knobColor))
            g.FillEllipse(knobBrush, knobX, knobY, knobD, knobD);

        var textRect = new Rectangle(pillW + 12, 0, Math.Max(10, Width - pillW - 12), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.TextPrimary : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class ModernSlider : Control
{
    private int _minimum = 0;
    private int _maximum = 100;
    private int _value = 80;
    private bool _dragging;

    public event EventHandler? ValueChanged;

    public int Minimum { get => _minimum; set { _minimum = value; Invalidate(); } }
    public int Maximum { get => _maximum; set { _maximum = Math.Max(_minimum + 1, value); Invalidate(); } }
    public string Suffix { get; set; } = "%";

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetValueSilently(int value)
    {
        _value = Math.Clamp(value, _minimum, _maximum);
        Invalidate();
    }

    public ModernSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(280, 30);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            _dragging = true;
            UpdateFromX(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && Enabled)
            UpdateFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    private void UpdateFromX(int x)
    {
        int trackLeft = 8;
        int trackRight = Math.Max(trackLeft + 20, Width - 52);
        float t = Math.Clamp((x - trackLeft) / (float)(trackRight - trackLeft), 0f, 1f);
        Value = (int)Math.Round(_minimum + t * (_maximum - _minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgWindow);
        if (parentBg == Color.Transparent) parentBg = Theme.BgWindow;
        g.Clear(parentBg);

        int trackLeft = 8;
        int trackRight = Math.Max(trackLeft + 20, Width - 52);
        int trackW = trackRight - trackLeft;
        float trackH = 6f;
        float trackY = (Height - trackH) / 2f;

        float t = (_value - _minimum) / (float)(_maximum - _minimum);
        float fillW = Math.Max(4f, trackW * t);

        var trackRect = new RectangleF(trackLeft, trackY, trackW, trackH);
        using (var trackPath = Theme.CreateRoundedPath(trackRect, 3f))
        using (var bgBrush = new SolidBrush(Theme.BgInput))
            g.FillPath(bgBrush, trackPath);

        if (t > 0.001f)
        {
            var fillRect = new RectangleF(trackLeft, trackY, fillW, trackH);
            using var fillPath = Theme.CreateRoundedPath(fillRect, 3f);
            using var fillBrush = Enabled
                ? (Brush)new LinearGradientBrush(fillRect, Color.FromArgb(0, 130, 210), Theme.Accent, 0f)
                : new SolidBrush(Theme.BorderStrong);
            g.FillPath(fillBrush, fillPath);
        }

        float thumbX = trackLeft + trackW * t;
        float thumbR = 7.5f;
        var thumbRect = new RectangleF(thumbX - thumbR, Height / 2f - thumbR, thumbR * 2f, thumbR * 2f);
        using (var thumbBrush = new SolidBrush(Enabled ? Theme.TextPrimary : Theme.TextMuted))
        using (var thumbBorder = new Pen(Enabled ? Theme.Accent : Theme.BorderStrong, 2f))
        {
            g.FillEllipse(thumbBrush, thumbRect);
            g.DrawEllipse(thumbBorder, thumbRect);
        }

        var valRect = new Rectangle(trackRight + 10, 0, Width - trackRight - 10, Height);
        TextRenderer.DrawText(g, $"{_value}{Suffix}", Theme.FontBodyBold, valRect,
            Enabled ? Theme.TextPrimary : Theme.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

internal sealed class SidebarNavButton : Control
{
    private bool _selected;
    private bool _hovered;

    public NavIconKind IconKind { get; set; }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    public SidebarNavButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Theme.FontBodyBold;
        Size = new Size(164, 38);
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.BgSidebar);

        if (_selected || _hovered)
        {
            var bgRect = new RectangleF(0, 1, Width - 1, Height - 2);
            using var bgPath = Theme.CreateRoundedPath(bgRect, 7f);
            using var bgBrush = new SolidBrush(_selected ? Theme.BgElevated : Color.FromArgb(27, 31, 41));
            g.FillPath(bgBrush, bgPath);
        }

        if (_selected)
        {
            using var barBrush = new SolidBrush(Theme.Accent);
            using var barPath = Theme.CreateRoundedPath(new RectangleF(2, 8, 3.5f, Height - 16), 1.8f);
            g.FillPath(barBrush, barPath);
        }

        Color fg = _selected ? Theme.TextPrimary : (_hovered ? Theme.TextPrimary : Theme.TextSecondary);
        Color iconColor = _selected ? Theme.Accent : fg;

        DrawVectorIcon(g, IconKind, new RectangleF(14, (Height - 16) / 2f, 16, 16), iconColor);

        var textRect = new Rectangle(38, 0, Width - 42, Height);
        TextRenderer.DrawText(g, Text, _selected ? Theme.FontBodyBold : Theme.FontBody, textRect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static void DrawVectorIcon(Graphics g, NavIconKind kind, RectangleF r, Color c)
    {
        using var pen = new Pen(c, 1.6f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        switch (kind)
        {
            case NavIconKind.Output:
                // Headphones/DAC arc + earcups
                g.DrawArc(pen, r.X + 2, r.Y + 2, 12, 11, 180, 180);
                g.DrawRectangle(pen, r.X + 1.5f, r.Y + 8, 3, 5.5f);
                g.DrawRectangle(pen, r.X + 11.5f, r.Y + 8, 3, 5.5f);
                break;

            case NavIconKind.Lcd:
                g.DrawRectangle(pen, r.X + 1.5f, r.Y + 3, 13, 10);
                g.DrawLine(pen, r.X + 4.5f, r.Y + 8, r.X + 11.5f, r.Y + 8);
                break;

            case NavIconKind.Rgb:
                // 3 LED spectrum dots/rings
                using (var b1 = new SolidBrush(Theme.Accent))
                using (var b2 = new SolidBrush(Theme.AccentAmber))
                using (var b3 = new SolidBrush(Color.FromArgb(167, 139, 250)))
                {
                    g.FillEllipse(b1, r.X + 1, r.Y + 5, 5, 5);
                    g.FillEllipse(b2, r.X + 6, r.Y + 2, 5, 5);
                    g.FillEllipse(b3, r.X + 9.5f, r.Y + 7.5f, 5, 5);
                }
                break;

            case NavIconKind.Hotkeys:
                g.DrawRectangle(pen, r.X + 1, r.Y + 3.5f, 14, 9);
                g.DrawLine(pen, r.X + 4, r.Y + 6.5f, r.X + 5.5f, r.Y + 6.5f);
                g.DrawLine(pen, r.X + 8, r.Y + 6.5f, r.X + 9.5f, r.Y + 6.5f);
                g.DrawLine(pen, r.X + 4.5f, r.Y + 9.5f, r.X + 11.5f, r.Y + 9.5f);
                break;

            case NavIconKind.Diagnostics:
                // Pulse waveform
                g.DrawLines(pen, [
                    new PointF(r.X + 1, r.Y + 8),
                    new PointF(r.X + 4.5f, r.Y + 8),
                    new PointF(r.X + 6.5f, r.Y + 3),
                    new PointF(r.X + 9.5f, r.Y + 13),
                    new PointF(r.X + 11.5f, r.Y + 8),
                    new PointF(r.X + 15, r.Y + 8)
                ]);
                break;
        }
    }
}

internal sealed class LcdPreviewWidget : Control
{
    private CustomLcdMode _previewMode = CustomLcdMode.Normal;
    private bool _interactiveSegments = true;
    private bool _hideTopIcons;
    private bool _screenOff;
    private string _customText = "K11 R2R";
    private ushort[] _segmentMasks = new ushort[7];
    private OutputMode _previewOutput = OutputMode.PRE;
    private GainMode _previewGain = GainMode.Mid;

    private int _hoverCell = -1;
    private int _hoverSeg = -1;
    private bool _draggingPaint;
    private bool _dragTargetLit;
    private int _lastDragCell = -1;
    private int _lastDragSeg = -1;

    public event EventHandler? SegmentMasksChanged;

    public bool InteractiveSegments
    {
        get => _interactiveSegments;
        set
        {
            if (_interactiveSegments == value)
                return;
            _interactiveSegments = value;
            if (!_interactiveSegments)
            {
                _hoverCell = -1;
                _hoverSeg = -1;
                _draggingPaint = false;
                Cursor = Cursors.Default;
            }
            Invalidate();
        }
    }

    public CustomLcdMode PreviewMode
    {
        get => _previewMode;
        set
        {
            if (_previewMode == value)
                return;
            _previewMode = value;
            if (_previewMode != CustomLcdMode.Segments)
            {
                _hoverCell = -1;
                _hoverSeg = -1;
                _draggingPaint = false;
                Cursor = Cursors.Default;
            }
            Invalidate();
        }
    }

    public bool HideTopIcons
    {
        get => _hideTopIcons;
        set { _hideTopIcons = value; Invalidate(); }
    }

    public bool ScreenOff
    {
        get => _screenOff;
        set { _screenOff = value; Invalidate(); }
    }

    public string CustomText
    {
        get => _customText;
        set { _customText = value ?? ""; Invalidate(); }
    }

    public ushort[] SegmentMasks
    {
        get => _segmentMasks;
        set
        {
            var next = new ushort[7];
            if (value is not null)
            {
                for (int i = 0; i < Math.Min(7, value.Length); i++)
                    next[i] = (ushort)(value[i] & K11LcdFont.AllSegmentsMask);
            }
            _segmentMasks = next;
            Invalidate();
        }
    }

    public OutputMode PreviewOutput
    {
        get => _previewOutput;
        set { _previewOutput = value; Invalidate(); }
    }

    public GainMode PreviewGain
    {
        get => _previewGain;
        set { _previewGain = value; Invalidate(); }
    }

    public LcdPreviewWidget()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(430, 108);
    }

    private (float CellH, float BodyW, float Slant, float Pitch, float TotalRowW, float StartX, float RowY) GetLayoutMetrics()
    {
        const float cellH = 54f;
        const float bodyW = 28f;
        const float slant = cellH * 0.087f;
        const float gap = 6.5f;
        const float pitch = bodyW + gap;
        float totalRowW = 6f * pitch + bodyW + slant;
        float startX = (Width - totalRowW) * 0.5f;
        const float rowY = 35f;
        return (cellH, bodyW, slant, pitch, totalRowW, startX, rowY);
    }

    private static (PointF A, PointF B)[] GetCellSegments(float x0, float y0, float bodyW, float cellH, float slant)
    {
        PointF Pt(float u, float v) => new(x0 + slant * (1f - v) + u * bodyW, y0 + v * cellH);
        return
        [
            /* 0  SegLeftTop      */ (Pt(0.00f, 0.03f), Pt(0.00f, 0.48f)),
            /* 1  SegLeftBottom   */ (Pt(0.00f, 0.52f), Pt(0.00f, 0.97f)),
            /* 2  SegTop          */ (Pt(0.04f, 0.00f), Pt(0.96f, 0.00f)),
            /* 3  SegMidLeft      */ (Pt(0.04f, 0.50f), Pt(0.47f, 0.50f)),
            /* 4  SegDiagTopLeft  */ (Pt(0.11f, 0.09f), Pt(0.45f, 0.44f)),
            /* 5  SegDiagBotLeft  */ (Pt(0.45f, 0.56f), Pt(0.11f, 0.91f)),
            /* 6  SegCenterTop    */ (Pt(0.50f, 0.04f), Pt(0.50f, 0.46f)),
            /* 7  SegBottom       */ (Pt(0.04f, 1.00f), Pt(0.96f, 1.00f)),
            /* 8  SegDiagTopRight */ (Pt(0.89f, 0.09f), Pt(0.55f, 0.44f)),
            /* 9  SegCenterBot    */ (Pt(0.50f, 0.54f), Pt(0.50f, 0.96f)),
            /* 10 SegMidRight     */ (Pt(0.53f, 0.50f), Pt(0.96f, 0.50f)),
            /* 11 SegDiagBotRight */ (Pt(0.55f, 0.56f), Pt(0.87f, 0.89f)),
            /* 12 SegRightTop     */ (Pt(1.00f, 0.03f), Pt(1.00f, 0.48f)),
            /* 13 SegRightBottom  */ (Pt(1.00f, 0.52f), Pt(1.00f, 0.97f)),
        ];
    }

    private (int Cell, int Seg) HitTestSegment(Point p)
    {
        if (_previewMode != CustomLcdMode.Segments || !_interactiveSegments)
            return (-1, -1);

        var (cellH, bodyW, slant, pitch, _, startX, rowY) = GetLayoutMetrics();
        const float maxDistSq = 34f; // ~5.8px generous clickable tolerance
        float bestDistSq = maxDistSq;
        int bestCell = -1;
        int bestSeg = -1;

        for (int c = 0; c < 7; c++)
        {
            float cellX = startX + c * pitch;
            if (p.X < cellX - 6f || p.X > cellX + bodyW + slant + 6f || p.Y < rowY - 6f || p.Y > rowY + cellH + 6f)
                continue;

            var segs = GetCellSegments(cellX, rowY, bodyW, cellH, slant);
            for (int s = 0; s < segs.Length; s++)
            {
                float d2 = DistancePointToSegmentSq(p, segs[s].A, segs[s].B);
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    bestCell = c;
                    bestSeg = s;
                }
            }
        }
        return (bestCell, bestSeg);
    }

    private static float DistancePointToSegmentSq(Point p, PointF a, PointF b)
    {
        float vx = b.X - a.X;
        float vy = b.Y - a.Y;
        float lenSq = vx * vx + vy * vy;
        if (lenSq < 0.001f)
            return (p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y);

        float t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lenSq, 0f, 1f);
        float px = a.X + t * vx;
        float py = a.Y + t * vy;
        float dx = p.X - px;
        float dy = p.Y - py;
        return dx * dx + dy * dy;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_previewMode != CustomLcdMode.Segments || !_interactiveSegments)
            return;

        var (cell, seg) = HitTestSegment(e.Location);
        if (cell != _hoverCell || seg != _hoverSeg)
        {
            _hoverCell = cell;
            _hoverSeg = seg;
            Cursor = cell >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        if (_draggingPaint && e.Button == MouseButtons.Left && cell >= 0 && seg >= 0
            && (cell != _lastDragCell || seg != _lastDragSeg))
        {
            _lastDragCell = cell;
            _lastDragSeg = seg;
            SetSegmentBit(cell, seg, _dragTargetLit);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_previewMode != CustomLcdMode.Segments || !_interactiveSegments || e.Button != MouseButtons.Left)
            return;

        var (cell, seg) = HitTestSegment(e.Location);
        if (cell < 0 || seg < 0)
            return;

        bool wasLit = ((_segmentMasks[cell] >> seg) & 1) != 0;
        _dragTargetLit = !wasLit;
        _draggingPaint = true;
        _lastDragCell = cell;
        _lastDragSeg = seg;
        SetSegmentBit(cell, seg, _dragTargetLit);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _draggingPaint = false;
        _lastDragCell = -1;
        _lastDragSeg = -1;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _draggingPaint = false;
        if (_hoverCell != -1 || _hoverSeg != -1)
        {
            _hoverCell = -1;
            _hoverSeg = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    private void SetSegmentBit(int cell, int seg, bool lit)
    {
        ushort bit = (ushort)(1 << seg);
        ushort prev = _segmentMasks[cell];
        ushort next = lit ? (ushort)(prev | bit) : (ushort)(prev & ~bit);
        if (prev == next)
            return;
        _segmentMasks[cell] = next;
        Invalidate();
        SegmentMasksChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgWindow);
        if (parentBg == Color.Transparent) parentBg = Theme.BgWindow;
        g.Clear(parentBg);

        // High-contrast VA glass bezel (or darkened glass when Screen Off is enabled in Normal mode)
        bool screenOff = _previewMode == CustomLcdMode.Normal && _screenOff;
        var outer = new RectangleF(1, 1, Width - 2, Height - 2);
        using (var path = Theme.CreateRoundedPath(outer, 10f))
        using (var glassBrush = new LinearGradientBrush(
            outer,
            screenOff ? Color.FromArgb(4, 5, 8) : Color.FromArgb(8, 10, 14),
            screenOff ? Color.FromArgb(6, 8, 12) : Color.FromArgb(14, 18, 24),
            90f))
        using (var rim = new Pen(screenOff ? Theme.BorderSubtle : Theme.BorderStrong, 1.4f))
        {
            g.FillPath(glassBrush, path);
            g.DrawPath(rim, path);
        }

        // Exact K11 R2R 7-cell slanted VA geometry centered cleanly inside bezel
        var (cellH, bodyW, slant, pitch, totalRowW, startX, rowY) = GetLayoutMetrics();

        bool customActive = _previewMode != CustomLcdMode.Normal;
        bool topVisible = !screenOff && !_hideTopIcons;
        Color glassGhost = screenOff
            ? Color.FromArgb(14, 18, 25)
            : (_previewMode == CustomLcdMode.Segments && _interactiveSegments)
                ? Color.FromArgb(38, 48, 66)
                : Color.FromArgb(24, 30, 42);
        Color segLit = customActive ? Color.FromArgb(236, 246, 255) : Color.FromArgb(222, 236, 250);
        Color topLit = Color.FromArgb(215, 232, 248);

        DrawUpperGlassRow(g, startX + slant * 0.5f, rowY - 22f, totalRowW - slant * 0.5f, pitch, topVisible, _previewOutput, _previewGain, topLit, glassGhost);

        string display = _previewMode == CustomLcdMode.Text
            ? CustomLcdConfig.SanitizeSegment(_customText, 7)
            : (screenOff ? "       " : "48K  92");

        for (int i = 0; i < 7; i++)
        {
            float cellX = startX + i * pitch;
            ushort mask = _previewMode == CustomLcdMode.Segments
                ? _segmentMasks[i]
                : K11LcdFont.GetSegmentMask(i < display.Length ? display[i] : ' ');
            int hoverSeg = (_previewMode == CustomLcdMode.Segments && _interactiveSegments && _hoverCell == i) ? _hoverSeg : -1;
            DrawStarburstCell(g, cellX, rowY, bodyW, cellH, slant, mask, segLit, glassGhost, hoverSeg);
        }
    }

    private static void DrawUpperGlassRow(
        Graphics g,
        float rowX,
        float topY,
        float rowW,
        float pitch,
        bool topVisible,
        OutputMode previewOut,
        GainMode previewGain,
        Color lit,
        Color ghost)
    {
        using var iconFont = new Font("Segoe UI Semibold", 8.4f, FontStyle.Bold);
        Color mainCol = topVisible ? lit : ghost;

        // "G." icon above cell 0 left
        TextRenderer.DrawText(g, "G.", iconFont, new Point((int)MathF.Round(rowX - 2f), (int)MathF.Round(topY)), mainCol,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        // 3 ascending gain signal bars right after "G." (L = 1, M = 2, H = 3)
        int litBars = previewGain switch
        {
            GainMode.Low => 1,
            GainMode.Mid => 2,
            _ => 3
        };
        float barsX = rowX + 12f;
        float barBaseY = topY + 12.5f;
        float[] barHeights = [3.5f, 6.0f, 8.8f];
        for (int b = 0; b < 3; b++)
        {
            Color bc = topVisible && b < litBars ? lit : ghost;
            using var br = new SolidBrush(bc);
            g.FillRectangle(br, barsX + b * 4.0f, barBaseY - barHeights[b], 2.5f, barHeights[b]);
        }

        // "PCM" indicator immediately right of G.ll (over cell 1..2)
        TextRenderer.DrawText(g, "PCM", iconFont, new Point((int)MathF.Round(rowX + pitch * 1.12f), (int)MathF.Round(topY)), mainCol,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        // "PO" (over cell 3.7..4.5) and "LO SE" (right-aligned over cell 5..6) matching hardware glass
        Color poCol = topVisible && previewOut == OutputMode.PO ? lit : ghost;
        Color loSeCol = topVisible && previewOut == OutputMode.PRE ? lit : ghost;

        TextRenderer.DrawText(g, "PO", iconFont, new Point((int)MathF.Round(rowX + pitch * 3.82f), (int)MathF.Round(topY)), poCol,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        Size loSeSize = TextRenderer.MeasureText(g, "LO SE", iconFont, new Size(200, 30), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "LO SE", iconFont, new Point((int)MathF.Round(rowX + rowW - loSeSize.Width + 1f), (int)MathF.Round(topY)), loSeCol,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private static void DrawStarburstCell(
        Graphics g,
        float x0,
        float y0,
        float bodyW,
        float cellH,
        float slant,
        ushort mask,
        Color litColor,
        Color ghostColor,
        int hoverSeg)
    {
        var segs = GetCellSegments(x0, y0, bodyW, cellH, slant);
        const float halfThick = 1.90f;
        using var ghostBrush = new SolidBrush(ghostColor);
        using var litBrush = new SolidBrush(litColor);
        using var hoverLitBrush = new SolidBrush(Theme.AccentHover);
        using var hoverGhostBrush = new SolidBrush(Color.FromArgb(145, Theme.Accent));

        for (int s = 0; s < segs.Length; s++)
        {
            if (((mask >> s) & 1) == 0)
                FillBeveledSegment(g, s == hoverSeg ? hoverGhostBrush : ghostBrush, segs[s].A, segs[s].B, halfThick);
        }

        for (int s = 0; s < segs.Length; s++)
        {
            if (((mask >> s) & 1) != 0)
                FillBeveledSegment(g, s == hoverSeg ? hoverLitBrush : litBrush, segs[s].A, segs[s].B, halfThick);
        }
    }

    private static void FillBeveledSegment(Graphics g, Brush brush, PointF a, PointF b, float halfThick)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f)
            return;

        float ux = dx / len;
        float uy = dy / len;
        float nx = -uy;
        float ny = ux;
        float tip = Math.Min(len * 0.28f, halfThick * 1.15f);

        PointF[] poly =
        [
            a,
            new(a.X + ux * tip + nx * halfThick, a.Y + uy * tip + ny * halfThick),
            new(b.X - ux * tip + nx * halfThick, b.Y - uy * tip + ny * halfThick),
            b,
            new(b.X - ux * tip - nx * halfThick, b.Y - uy * tip - ny * halfThick),
            new(a.X + ux * tip - nx * halfThick, a.Y + uy * tip - ny * halfThick),
        ];
        g.FillPolygon(brush, poly);
    }
}

internal sealed class ModernDelayStepper : Control
{
    private readonly System.Windows.Forms.Timer _repeatTimer;
    private int _value = 2500;
    private int _hoverZone; // -1 minus, 0 none, +1 plus
    private int _holdDir;
    private int _holdTicks;
    private AppLanguage _language = AppLanguage.Ru;

    public int SideButtonWidth { get; set; } = 40;
    public bool CompactMsOnly { get; set; } = false;
    public int Minimum { get; set; } = 500;
    public int Maximum { get; set; } = 30000;
    public int Step { get; set; } = 250;
    public AppLanguage Language { get => _language; set { _language = value; Invalidate(); } }
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, Minimum, Maximum);
            if (_value == clamped)
                return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetValueSilently(int value)
    {
        _value = Math.Clamp(value, Minimum, Maximum);
        Invalidate();
    }

    public ModernDelayStepper()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        Size = new Size(296, 34);
        _repeatTimer = new System.Windows.Forms.Timer { Interval = 75 };
        _repeatTimer.Tick += (_, _) =>
        {
            if (_holdDir == 0 || !Enabled)
            {
                _repeatTimer.Stop();
                return;
            }
            if (++_holdTicks >= 5)
                Nudge(_holdDir);
        };
    }

    private void Nudge(int dir)
    {
        int delta = (ModifierKeys.HasFlag(Keys.Shift) ? Step * 4 : Step) * dir;
        Value = _value + delta;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int z = HitZone(e.X);
        if (z != _hoverZone)
        {
            _hoverZone = z;
            Cursor = z != 0 && Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverZone = 0;
        _holdDir = 0;
        _repeatTimer.Stop();
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            Focus();
            int dir = HitZone(e.X);
            if (dir != 0)
            {
                Nudge(dir);
                _holdDir = dir;
                _holdTicks = 0;
                _repeatTimer.Start();
                Invalidate();
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _holdDir = 0;
        _repeatTimer.Stop();
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Enabled && e.Delta != 0)
            Nudge(e.Delta > 0 ? 1 : -1);
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled)
        {
            if (e.KeyCode is Keys.Left or Keys.Down) { Nudge(-1); e.Handled = true; }
            else if (e.KeyCode is Keys.Right or Keys.Up) { Nudge(1); e.Handled = true; }
        }
        base.OnKeyDown(e);
    }

    private int HitZone(int x)
    {
        int bw = SideButtonWidth;
        if (x < bw) return -1;
        if (x >= Width - bw) return 1;
        return 0;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgCard);
        if (parentBg == Color.Transparent) parentBg = Theme.BgCard;
        g.Clear(parentBg);

        int bw = SideButtonWidth;
        var outer = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using var outerPath = Theme.CreateRoundedPath(outer, 7f);
        using (var bg = new SolidBrush(Theme.BgInput))
            g.FillPath(bg, outerPath);

        // Left (-) and Right (+) interactive segments clipped inside outer pill
        var oldClip = g.Clip;
        g.SetClip(outerPath);
        DrawSideButton(g, new RectangleF(0, 0, bw, Height), "−", -1, _value > Minimum);
        DrawSideButton(g, new RectangleF(Width - bw, 0, bw, Height), "+", 1, _value < Maximum);
        using (var divPen = new Pen(Theme.BorderSubtle, 1f))
        {
            g.DrawLine(divPen, bw, 4, bw, Height - 4);
            g.DrawLine(divPen, Width - bw - 1, 4, Width - bw - 1, Height - 4);
        }
        g.Clip = oldClip;

        using (var border = new Pen(Focused ? Theme.Accent : (Enabled ? Theme.BorderStrong : Theme.BorderSubtle), 1f))
            g.DrawPath(border, outerPath);

        string unitMs = _language == AppLanguage.En ? "ms" : "мс";
        string unitSec = _language == AppLanguage.En ? "s" : "с";
        string label = CompactMsOnly
            ? $"{_value} {unitMs}"
            : $"{_value} {unitMs}  •  {_value / 1000.0:0.##} {unitSec}";
        var centerRect = new Rectangle(bw + 2, 0, Width - bw * 2 - 4, Height);
        TextRenderer.DrawText(g, label, Theme.FontMono, centerRect, Enabled ? Theme.TextPrimary : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private void DrawSideButton(Graphics g, RectangleF r, string sym, int zoneDir, bool canStep)
    {
        bool hot = Enabled && canStep && (_hoverZone == zoneDir || _holdDir == zoneDir);
        Color bg = _holdDir == zoneDir && Enabled && canStep
            ? Theme.Blend(Theme.BgElevated, Theme.Accent, 0.25f)
            : (hot ? Theme.BgElevated : Color.FromArgb(24, 28, 37));
        using (var br = new SolidBrush(bg))
            g.FillRectangle(br, r);

        Color fg = Enabled && canStep ? (hot ? Theme.Accent : Theme.TextPrimary) : Theme.TextMuted;
        TextRenderer.DrawText(g, sym, Theme.FontSection, Rectangle.Round(r), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _repeatTimer.Stop();
            _repeatTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class SidebarMuteButton : Control
{
    private bool _connected;
    private bool _muted;
    private bool _hovered;
    private bool _pressed;
    private AppLanguage _language = AppLanguage.Ru;

    public SidebarMuteButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Size = new Size(164, 32);
    }

    public void UpdateState(bool connected, bool muted, AppLanguage language)
    {
        _connected = connected;
        _muted = muted;
        _language = language;
        Enabled = connected;
        Text = !connected
            ? AppStrings.StatusAudioUnknown(language)
            : (muted ? AppStrings.StatusAudioMuted(language) : AppStrings.StatusAudioActive(language));
        if (!connected)
        {
            _hovered = false;
            _pressed = false;
        }
        Cursor = connected ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { if (_connected) { _hovered = true; Invalidate(); } base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (_connected && e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.BgSidebar);

        var outer = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using var outerPath = Theme.CreateRoundedPath(outer, 8f);

        Color bgColor = !_connected
            ? Theme.BgInput
            : (_muted
                ? (_pressed ? Color.FromArgb(44, 22, 26) : (_hovered ? Color.FromArgb(54, 25, 30) : Theme.BgInput))
                : (_pressed ? Theme.Blend(Theme.BgElevated, Color.Black, 0.16f) : (_hovered ? Theme.BgElevated : Theme.BgInput)));
        using (var bg = new SolidBrush(bgColor))
            g.FillPath(bg, outerPath);

        Color rimColor = !_connected
            ? Theme.BorderSubtle
            : (_muted
                ? (_hovered ? Theme.Danger : Color.FromArgb(145, Theme.Danger))
                : (_hovered ? Theme.Success : Color.FromArgb(115, Theme.Success)));
        using (var rim = new Pen(rimColor, 1.2f))
            g.DrawPath(rim, outerPath);

        Color dotColor = !_connected ? Theme.TextMuted : (_muted ? Theme.Danger : Theme.Success);
        using (var b = new SolidBrush(dotColor))
            g.FillEllipse(b, 10f, (Height - 7.5f) / 2f, 7.5f, 7.5f);

        Color textColor = _connected ? Theme.TextPrimary : Theme.TextMuted;
        string text = !_connected
            ? AppStrings.StatusAudioUnknown(_language)
            : (_muted ? AppStrings.StatusAudioMuted(_language) : AppStrings.StatusAudioActive(_language));
        TextRenderer.DrawText(g, text, Theme.FontSmall,
            new Rectangle(23, 0, Width - 29, Height),
            textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class SidebarBalanceButton : Control
{
    private bool _hovered;
    private bool _pressed;
    private string _topText = "";
    private string _bottomText = "";

    public string TopText => _topText;
    public string BottomText => _bottomText;

    public SidebarBalanceButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        Cursor = Cursors.Hand;
        Size = new Size(164, 50);
    }

    public void UpdateLabels(string topText, string bottomText)
    {
        if (string.Equals(_topText, topText, StringComparison.Ordinal)
            && string.Equals(_bottomText, bottomText, StringComparison.Ordinal))
            return;

        _topText = topText;
        _bottomText = bottomText;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.BgSidebar);

        var outer = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using var outerPath = Theme.CreateRoundedPath(outer, 8f);
        Color bgColor = _pressed
            ? Color.FromArgb(30, 25, 18)
            : (_hovered ? Color.FromArgb(46, 38, 24) : Theme.BgInput);
        using (var bg = new SolidBrush(bgColor))
            g.FillPath(bg, outerPath);

        Color rimColor = _hovered ? Theme.AccentAmberHover : Color.FromArgb(145, Theme.AccentAmber);
        using (var rim = new Pen(rimColor, 1.2f))
            g.DrawPath(rim, outerPath);

        Color dotColor = _hovered ? Theme.AccentAmberHover : Theme.AccentAmber;
        using (var b = new SolidBrush(dotColor))
            g.FillEllipse(b, 10f, 3f + (22f - 7.5f) / 2f, 7.5f, 7.5f);

        TextRenderer.DrawText(g, _topText, Theme.FontSmall,
            new Rectangle(23, 3, Width - 29, 22),
            Theme.AccentAmberHover,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        Color subColor = _hovered ? Theme.TextPrimary : Theme.TextSecondary;
        TextRenderer.DrawText(g, _bottomText, Theme.FontSmall,
            new Rectangle(23, 24, Width - 29, 22),
            subColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

internal sealed class DarkSelectorButton : Control
{
    private readonly List<(string ButtonLabel, string MenuLabel)> _items = [];
    private int _selectedIndex = -1;
    private bool _hovered;
    private ContextMenuStrip? _menu;

    public float CornerRadius { get; set; } = 7f;
    public bool SubtleBorder { get; set; }

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int next = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex == next)
                return;
            _selectedIndex = next;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DarkSelectorButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = Theme.FontBody;
        Size = new Size(192, 32);
    }

    public void SetItems(IReadOnlyList<string> labels, int selectedIndex)
    {
        _items.Clear();
        foreach (string label in labels)
            _items.Add((label, label));
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);
        Invalidate();
    }

    public void SetItems(IReadOnlyList<(string ButtonLabel, string MenuLabel)> items, int selectedIndex)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _items.Count - 1);
        Invalidate();
    }

    public void SetSelectedIndexSilently(int index)
    {
        _selectedIndex = _items.Count == 0 ? -1 : Math.Clamp(index, 0, _items.Count - 1);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || _items.Count == 0)
            return;

        _menu?.Dispose();
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = true,
            ShowCheckMargin = true,
            BackColor = Theme.BgSidebar,
            ForeColor = Theme.TextPrimary,
            Font = Theme.FontBody
        };

        for (int i = 0; i < _items.Count; i++)
        {
            int capturedIdx = i;
            var item = new ToolStripMenuItem(_items[i].MenuLabel) { Checked = i == _selectedIndex };
            item.Click += (_, _) => SelectedIndex = capturedIdx;
            _menu.Items.Add(item);
        }
        _menu.Show(this, new Point(0, Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgCard);
        if (parentBg == Color.Transparent) parentBg = Theme.BgCard;
        g.Clear(parentBg);

        var rect = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        using var path = Theme.CreateRoundedPath(rect, CornerRadius);
        using var bg = new SolidBrush(_hovered && Enabled ? Theme.BgElevated : Theme.BgInput);
        Color idleBorder = SubtleBorder ? Theme.BorderSubtle : Theme.BorderStrong;
        using var border = new Pen(Enabled ? (_hovered ? Theme.Accent : idleBorder) : Theme.BorderSubtle, 1f);
        g.FillPath(bg, path);
        g.DrawPath(border, path);

        string text = (_selectedIndex >= 0 && _selectedIndex < _items.Count) ? _items[_selectedIndex].ButtonLabel : "";
        var textRect = new Rectangle(10, 0, Math.Max(20, Width - 28), Height);
        TextRenderer.DrawText(g, text, Font, textRect, Enabled ? Theme.TextPrimary : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        float cx = Width - 13f;
        float cy = Height / 2f;
        using var chev = new Pen(Enabled ? Theme.TextSecondary : Theme.TextMuted, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(chev, cx - 3.5f, cy - 1.5f, cx, cy + 2.0f);
        g.DrawLine(chev, cx, cy + 2.0f, cx + 3.5f, cy - 1.5f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _menu?.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class FrameCapsuleSelector : Control
{
    private int _activeCount = 1;
    private int _selectedIndex;
    private int _hoverIndex = -1;
    private string _labelPrefix = "Кадр";

    public event EventHandler<int>? FrameClicked;

    public int ActiveCount
    {
        get => _activeCount;
        set { _activeCount = Math.Clamp(value, 1, SticksPreset.MaxFrames); Invalidate(); }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = Math.Clamp(value, 0, SticksPreset.MaxFrames - 1); Invalidate(); }
    }

    public string LabelPrefix
    {
        get => _labelPrefix;
        set { _labelPrefix = value ?? ""; Invalidate(); }
    }

    public FrameCapsuleSelector()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Size = new Size(252, 34);
        Font = Theme.FontBodyBold;
    }

    private int HitIndex(int x)
    {
        int idx = SegmentedCapsulePainter.HitTest(Width, SticksPreset.MaxFrames, x);
        return idx >= 0 && idx < _activeCount ? idx : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int idx = Enabled ? HitIndex(e.X) : -1;
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Enabled && e.Button == MouseButtons.Left)
        {
            int idx = HitIndex(e.X);
            if (idx >= 0)
            {
                _selectedIndex = idx;
                Invalidate();
                FrameClicked?.Invoke(this, idx);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        SegmentedCapsulePainter.Paint(
            e.Graphics,
            this,
            SticksPreset.MaxFrames,
            _selectedIndex,
            _hoverIndex,
            i => i < _activeCount,
            i => string.IsNullOrEmpty(_labelPrefix) ? (i + 1).ToString() : $"{_labelPrefix} {i + 1}");
    }
}

internal sealed class DarkPresetNameDialog : Form
{
    private readonly TextBox _input;

    public string PresetName => _input.Text.Trim();

    public DarkPresetNameDialog(string title, string prompt, string defaultName, AppLanguage lang)
    {
        Text = title;
        Icon = Theme.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.BgWindow;
        ForeColor = Theme.TextPrimary;
        Font = Theme.FontBody;
        ClientSize = new Size(320, 128);

        var lbl = new Label
        {
            Text = prompt,
            Location = new Point(16, 12),
            AutoSize = true,
            ForeColor = Theme.TextSecondary,
            Font = Theme.FontSmall
        };

        var wrap = new Panel
        {
            Location = new Point(16, 36),
            Size = new Size(288, 34),
            BackColor = Theme.BgInput
        };
        wrap.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, wrap.Width - 2f, wrap.Height - 2f);
            using var p = Theme.CreateRoundedPath(r, 7f);
            using var br = new SolidBrush(Theme.BgInput);
            using var pen = new Pen(Theme.Accent, 1f);
            e.Graphics.FillPath(br, p);
            e.Graphics.DrawPath(pen, p);
        };

        _input = new TextBox
        {
            Text = defaultName,
            MaxLength = 24,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.BgInput,
            ForeColor = Theme.TextPrimary,
            Font = Theme.FontBodyBold,
            Location = new Point(10, 7),
            Width = 268
        };
        wrap.Controls.Add(_input);

        void AcceptName()
        {
            if (string.IsNullOrWhiteSpace(_input.Text))
                return;
            DialogResult = DialogResult.OK;
            Close();
        }

        void CancelDialog()
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        var okBtn = new ModernButton
        {
            Text = "OK",
            Variant = ButtonVariant.Primary,
            Location = new Point(116, 82),
            Size = new Size(92, 32)
        };
        okBtn.Click += (_, _) => AcceptName();

        var cancelBtn = new ModernButton
        {
            Text = lang == AppLanguage.En ? "Cancel" : "Отмена",
            Variant = ButtonVariant.Secondary,
            Location = new Point(214, 82),
            Size = new Size(90, 32)
        };
        cancelBtn.Click += (_, _) => CancelDialog();

        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AcceptName();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CancelDialog();
            }
        };

        Controls.AddRange([lbl, wrap, okBtn, cancelBtn]);
        Shown += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkWindowChrome(Handle);
    }
}
