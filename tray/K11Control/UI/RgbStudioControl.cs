using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using K11Control.Config;

namespace K11Control.UI;

internal sealed class RgbZonePreviewBar : Control
{
    private readonly Color[] _zoneColors = [Color.DeepSkyBlue, Color.DeepSkyBlue, Color.DeepSkyBlue];
    private readonly System.Windows.Forms.Timer _animTimer;
    private int _phase;
    private int _selectedZone = -1; // -1 = all zones linked
    private RgbStateMode _state = RgbStateMode.On;
    private RgbEffectMode _effect = RgbEffectMode.Solid;
    private int _brightness = 80;

    public event Action<int>? ZoneClicked;

    public RgbZonePreviewBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(494, 72);

        _animTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _animTimer.Tick += (_, _) =>
        {
            _phase = (_phase + 3) & 0xFF;
            Invalidate();
        };
    }

    public void UpdateState(RgbStateMode state, RgbEffectMode effect, int brightness, IReadOnlyList<Color> colors, int selectedZone)
    {
        _state = state;
        _effect = effect;
        _brightness = Math.Clamp(brightness, 0, 100);
        _selectedZone = selectedZone;
        for (int i = 0; i < 3; i++)
            _zoneColors[i] = i < colors.Count ? colors[i] : Theme.Accent;

        bool needsAnim = Visible && _state == RgbStateMode.Custom
            && (_effect is RgbEffectMode.Breathing or RgbEffectMode.Rainbow or RgbEffectMode.Chase);
        if (needsAnim && !_animTimer.Enabled)
            _animTimer.Start();
        else if (!needsAnim && _animTimer.Enabled)
            _animTimer.Stop();

        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible && _animTimer.Enabled)
            _animTimer.Stop();
        else if (Visible && _state == RgbStateMode.Custom && _effect != RgbEffectMode.Solid && !_animTimer.Enabled)
            _animTimer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Width > 60)
        {
            int idx = Math.Clamp((int)Math.Floor(e.X / (Width / 3f)), 0, 2);
            ZoneClicked?.Invoke(idx);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 40 || Height < 24)
            return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color parentBg = Parent is RoundedCardPanel card ? card.CardColor : (Parent?.BackColor ?? Theme.BgCard);
        if (parentBg == Color.Transparent) parentBg = Theme.BgCard;
        g.Clear(parentBg);

        // Black anodized top window of K11 R2R
        var chassis = new RectangleF(1, 1, Width - 2, Height - 2);
        using var cPath = Theme.CreateRoundedPath(chassis, 10f);
        using (var cBrush = new LinearGradientBrush(chassis, Color.FromArgb(14, 17, 23), Color.FromArgb(8, 10, 14), 90f))
            g.FillPath(cBrush, cPath);

        const float logoW = 248f;
        const float logoH = 34f;
        float x0 = (Width - logoW) / 2f;
        float y0 = (Height - logoH) / 2f;
        float[] zoneCenters = [x0 + logoW * 0.145f, x0 + logoW * 0.480f, x0 + logoW * 0.826f];

        Color[] rendered = ComputeLiveZoneColors();
        bool lit = _state != RgbStateMode.Off && rendered.Any(c => c.R + c.G + c.B > 2);

        // Authentic F I I O vector wordmark matching the 3 hardware LED zones (F | II | O)
        using (var logoPath = BuildFiioLogoPath(x0, y0, logoW, logoH))
        {
            if (!lit)
            {
                using var offFill = new SolidBrush(Color.FromArgb(28, 33, 43));
                using var offPen = new Pen(Color.FromArgb(46, 54, 68), 1f);
                g.FillPath(offFill, logoPath);
                g.DrawPath(offPen, logoPath);
            }
            else
            {
                Color[] faceCols = [DiffuserFace(rendered[0]), DiffuserFace(rendered[1]), DiffuserFace(rendered[2])];
                Color[] rimCols = [DiffuserRim(rendered[0]), DiffuserRim(rendered[1]), DiffuserRim(rendered[2])];
                // Subtle bevel scatter right along the aluminum cutout border (no wide chassis fog)
                Color[] edgeCols =
                [
                    Color.FromArgb(52, rendered[0]),
                    Color.FromArgb(52, rendered[1]),
                    Color.FromArgb(52, rendered[2])
                ];

                var gradBounds = new RectangleF(x0 - 2f, y0 - 2f, logoW + 4f, logoH + 4f);
                using var fillBrush = CreateZoneGradientBrush(gradBounds, faceCols);
                using var rimBrush = CreateZoneGradientBrush(gradBounds, rimCols);
                using var edgeBrush = CreateZoneGradientBrush(gradBounds, edgeCols);
                using var edgePen = new Pen(edgeBrush, 2.2f) { LineJoin = LineJoin.Round };
                using var rimPen = new Pen(rimBrush, 1.1f) { LineJoin = LineJoin.Round };

                g.DrawPath(edgePen, logoPath);
                g.FillPath(fillBrush, logoPath);

                // Emissive acrylic diffuser spine highlight proportional to zone brightness (keeps saturated outer hues intact)
                float avgLum = (ChannelMax(rendered[0]) + ChannelMax(rendered[1]) + ChannelMax(rendered[2])) / (3f * 255f);
                int coreAlpha = Math.Clamp((int)MathF.Round(114f * MathF.Pow(avgLum, 1.32f)), 0, 114);
                if (coreAlpha >= 8)
                {
                    var oldCoreClip = g.Clip;
                    g.SetClip(logoPath);
                    var vBounds = new RectangleF(x0, y0, logoW, logoH);
                    using var coreBrush = new LinearGradientBrush(vBounds, Color.Transparent, Color.Transparent, 90f)
                    {
                        InterpolationColors = new ColorBlend
                        {
                            Colors =
                            [
                                Color.FromArgb(0, 255, 255, 255),
                                Color.FromArgb(coreAlpha / 2, 255, 255, 255),
                                Color.FromArgb(coreAlpha, 255, 255, 255),
                                Color.FromArgb(coreAlpha / 2, 255, 255, 255),
                                Color.FromArgb(0, 255, 255, 255)
                            ],
                            Positions = [0f, 0.30f, 0.50f, 0.70f, 1f]
                        }
                    };
                    g.FillRectangle(coreBrush, vBounds);
                    g.Clip = oldCoreClip;
                }

                g.DrawPath(rimPen, logoPath);
            }
        }

        // Subtle focus underline only when a specific single zone (1/2/3) is selected
        if (_state == RgbStateMode.Custom && _selectedZone >= 0 && _selectedZone < 3)
        {
            float cx = zoneCenters[_selectedZone];
            var tickRect = new RectangleF(cx - 19f, Height - 7.5f, 38f, 2.5f);
            using var tPath = Theme.CreateRoundedPath(tickRect, 1.2f);
            using var tBrush = new SolidBrush(Color.FromArgb(210, 240, 246, 255));
            g.FillPath(tBrush, tPath);
        }

        using var borderPen = new Pen(Theme.BorderStrong, 1.2f);
        g.DrawPath(borderPen, cPath);
    }

    private static GraphicsPath BuildFiioLogoPath(float x0, float y0, float totalW, float h)
    {
        PointF Pt(float nx, float ny) => new(x0 + nx * totalW, y0 + ny * h);

        void AddPillar(GraphicsPath target, float nxLeft, float nxRight)
        {
            target.AddPolygon([
                Pt(nxLeft, 0.91f),
                Pt(nxLeft, 0.22f),
                Pt(nxLeft + 0.036f, 0.00f),
                Pt(nxRight - 0.012f, 0.00f),
                Pt(nxRight, 0.09f),
                Pt(nxRight, 0.78f),
                Pt(nxRight - 0.036f, 1.00f),
                Pt(nxLeft + 0.012f, 1.00f),
            ]);
        }

        var path = new GraphicsPath(FillMode.Winding);

        // 'F' upper floating stencil bar (chamfered top-left, sleek horizontal beam)
        path.AddPolygon([
            Pt(0.014f, 0.27f),
            Pt(0.016f, 0.14f),
            Pt(0.056f, 0.00f),
            Pt(0.286f, 0.00f),
            Pt(0.312f, 0.12f),
            Pt(0.312f, 0.27f),
        ]);

        // 'F' lower stem + middle bar separated from top bar by the authentic chassis slit
        path.AddPolygon([
            Pt(0.024f, 1.00f),
            Pt(0.000f, 0.85f),
            Pt(0.003f, 0.48f),
            Pt(0.032f, 0.37f),
            Pt(0.248f, 0.37f),
            Pt(0.262f, 0.43f),
            Pt(0.262f, 0.59f),
            Pt(0.248f, 0.65f),
            Pt(0.118f, 0.65f),
            Pt(0.103f, 0.72f),
            Pt(0.102f, 1.00f),
        ]);

        // Twin chunky chamfered 'I' pillars over center LED zone
        AddPillar(path, 0.352f, 0.458f);
        AddPillar(path, 0.502f, 0.608f);

        // 'O' open-bottom geometric stencil loop over right LED zone
        path.AddPolygon([
            Pt(0.684f, 1.00f),
            Pt(0.652f, 0.80f),
            Pt(0.652f, 0.20f),
            Pt(0.684f, 0.00f),
            Pt(0.968f, 0.00f),
            Pt(1.000f, 0.20f),
            Pt(1.000f, 0.80f),
            Pt(0.968f, 1.00f),
            Pt(0.808f, 1.00f),
            Pt(0.808f, 0.77f),
            Pt(0.818f, 0.74f),
            Pt(0.880f, 0.74f),
            Pt(0.896f, 0.67f),
            Pt(0.896f, 0.34f),
            Pt(0.880f, 0.27f),
            Pt(0.770f, 0.27f),
            Pt(0.754f, 0.34f),
            Pt(0.754f, 0.91f),
            Pt(0.742f, 1.00f),
        ]);

        return path;
    }

    private static LinearGradientBrush CreateZoneGradientBrush(RectangleF bounds, Color[] cols) =>
        new(bounds, cols[0], cols[2], 0f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [cols[0], cols[0], cols[1], cols[2], cols[2]],
                Positions = [0f, 0.20f, 0.48f, 0.80f, 1f]
            }
        };

    private static int ChannelMax(Color c) => Math.Max(c.R, Math.Max(c.G, c.B));

    private static Color DiffuserFace(Color c)
    {
        float lum = ChannelMax(c) / 255f;
        return lum <= 0.01f ? c : Theme.Blend(c, Color.White, 0.12f * MathF.Pow(lum, 1.4f));
    }

    private static Color DiffuserRim(Color c)
    {
        float lum = ChannelMax(c) / 255f;
        return lum <= 0.01f ? c : Color.FromArgb(225, Theme.Blend(c, Color.White, 0.44f * MathF.Pow(lum, 1.15f)));
    }

    private Color[] ComputeLiveZoneColors()
    {
        if (_state == RgbStateMode.Off)
            return [Color.Black, Color.Black, Color.Black];

        if (_state == RgbStateMode.On)
        {
            Color stockCyan = SimulateHardwareLedToSrgb(Color.FromArgb(0, 224, 255), 1.0f);
            return [stockCyan, stockCyan, stockCyan];
        }

        float rawDuty = _brightness / 100f;
        if (_effect == RgbEffectMode.Solid)
        {
            return
            [
                SimulateHardwareLedToSrgb(_zoneColors[0], rawDuty),
                SimulateHardwareLedToSrgb(_zoneColors[1], rawDuty),
                SimulateHardwareLedToSrgb(_zoneColors[2], rawDuty)
            ];
        }

        if (_effect == RgbEffectMode.Breathing)
        {
            float wave = _phase < 128 ? (_phase * 2) / 255f : ((255 - _phase) * 2) / 255f;
            float duty = rawDuty * wave;
            return
            [
                SimulateHardwareLedToSrgb(_zoneColors[0], duty),
                SimulateHardwareLedToSrgb(_zoneColors[1], duty),
                SimulateHardwareLedToSrgb(_zoneColors[2], duty)
            ];
        }

        if (_effect == RgbEffectMode.Rainbow)
        {
            var res = new Color[3];
            for (int z = 0; z < 3; z++)
            {
                float h = (((_phase + z * 85) & 0xFF) / 255f) * 360f;
                res[z] = SimulateHardwareLedToSrgb(Theme.ColorFromHsv(h, 1f, 1f), rawDuty);
            }
            return res;
        }

        int active = _phase < 85 ? 0 : (_phase < 170 ? 1 : 2);
        var chase = new Color[3];
        for (int z = 0; z < 3; z++)
        {
            float duty = z == active ? rawDuty : rawDuty * 0.18f;
            chase[z] = SimulateHardwareLedToSrgb(_zoneColors[z], duty);
        }
        return chase;
    }

    private static Color SimulateHardwareLedToSrgb(Color baseColor, float duty01)
    {
        duty01 = Math.Clamp(duty01, 0f, 1f);
        float peakIn = Math.Max(baseColor.R, Math.Max(baseColor.G, baseColor.B));
        if (duty01 <= 0.001f || peakIn <= 0.5f)
            return Color.Black;

        // Real WS2812 SMD LEDs inside the frosted black acrylic window appear brighter than sRGB bytes * linear duty:
        // convert duty fraction via perceptual response (^0.34) and preserve exact RGB ratios down into deep dark tones.
        float visualDuty = MathF.Pow(duty01, 0.34f);
        float n = peakIn / 255f;
        float outPeak = (40f + 215f * MathF.Pow(n, 0.68f)) * visualDuty;
        float scale = outPeak / peakIn;

        return Color.FromArgb(
            Math.Clamp((int)MathF.Round(baseColor.R * scale), 0, 255),
            Math.Clamp((int)MathF.Round(baseColor.G * scale), 0, 255),
            Math.Clamp((int)MathF.Round(baseColor.B * scale), 0, 255));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class HsvSpectrumPicker : Control
{
    private const int HueBarWidth = 20;
    private const int Gap = 12;
    private const int Pad = 7;

    private float _hue = 197f;
    private float _sat = 1f;
    private float _val = 1f;
    private bool _draggingSv;
    private bool _draggingHue;

    private Bitmap? _svCache;
    private float _svCachedHue = -1f;
    private Size _svCachedSize;
    private Bitmap? _hueCache;
    private Size _hueCachedSize;

    public event EventHandler<Color>? ColorPicked;

    public Color CurrentColor => Theme.ColorFromHsv(_hue, _sat, _val);

    public HsvSpectrumPicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Cross;
        Size = new Size(216, 134);
    }

    public void SetColorSilently(Color c)
    {
        Theme.ColorToHsv(c, out float h, out float s, out float v);
        if (s > 0.005f)
            _hue = h;
        _sat = s;
        _val = v;
        Invalidate();
    }

    private Rectangle SvRect => new(Pad, Pad, Math.Max(32, Width - HueBarWidth - Gap - Pad * 2), Math.Max(32, Height - Pad * 2));
    private Rectangle HueRect => new(SvRect.Right + Gap, Pad, HueBarWidth, Math.Max(32, Height - Pad * 2));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (HueRect.Contains(e.Location))
            {
                _draggingHue = true;
                UpdateHueFromY(e.Y);
            }
            else if (SvRect.Contains(e.Location))
            {
                _draggingSv = true;
                UpdateSvFromPoint(e.Location);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingHue)
            UpdateHueFromY(e.Y);
        else if (_draggingSv)
            UpdateSvFromPoint(e.Location);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _draggingHue = false;
        _draggingSv = false;
        base.OnMouseUp(e);
    }

    private void UpdateHueFromY(int y)
    {
        var r = HueRect;
        _hue = Math.Clamp((y - r.Top) / (float)Math.Max(1, r.Height - 1), 0f, 1f) * 360f;
        Invalidate();
        ColorPicked?.Invoke(this, CurrentColor);
    }

    private void UpdateSvFromPoint(Point pt)
    {
        var r = SvRect;
        _sat = Math.Clamp((pt.X - r.Left) / (float)Math.Max(1, r.Width - 1), 0f, 1f);
        _val = 1f - Math.Clamp((pt.Y - r.Top) / (float)Math.Max(1, r.Height - 1), 0f, 1f);
        Invalidate();
        ColorPicked?.Invoke(this, CurrentColor);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BgCard);

        var svRect = SvRect;
        var hueRect = HueRect;

        EnsureSvCache(svRect.Size);
        EnsureHueCache(hueRect.Size);

        if (_svCache is not null)
        {
            using var clipPath = Theme.CreateRoundedPath(svRect, 6f);
            var oldClip = g.Clip;
            g.SetClip(clipPath);
            g.DrawImageUnscaled(_svCache, svRect.Location);
            g.Clip = oldClip;

            using var border = new Pen(Theme.BorderStrong, 1f);
            g.DrawPath(border, clipPath);
        }

        if (_hueCache is not null)
        {
            using var clipPath = Theme.CreateRoundedPath(hueRect, 6f);
            var oldClip = g.Clip;
            g.SetClip(clipPath);
            g.DrawImageUnscaled(_hueCache, hueRect.Location);
            g.Clip = oldClip;

            using var border = new Pen(Theme.BorderStrong, 1f);
            g.DrawPath(border, clipPath);
        }

        // SV thumb
        float sx = svRect.Left + _sat * (svRect.Width - 1);
        float sy = svRect.Top + (1f - _val) * (svRect.Height - 1);
        using (var fill = new SolidBrush(CurrentColor))
        using (var outer = new Pen(Color.White, 2f))
        using (var inner = new Pen(Color.Black, 1f))
        {
            g.FillEllipse(fill, sx - 6, sy - 6, 12, 12);
            g.DrawEllipse(outer, sx - 6, sy - 6, 12, 12);
            g.DrawEllipse(inner, sx - 7, sy - 7, 14, 14);
        }

        // Hue thumb
        float hy = hueRect.Top + (_hue / 360f) * (hueRect.Height - 1);
        var hMarker = new RectangleF(hueRect.Left - 2, hy - 3.5f, hueRect.Width + 4, 7f);
        using (var hPath = Theme.CreateRoundedPath(hMarker, 3f))
        using (var hPen = new Pen(Color.White, 2f))
            g.DrawPath(hPen, hPath);
    }

    private void EnsureSvCache(Size sz)
    {
        if (_svCache is not null && _svCachedSize == sz && Math.Abs(_svCachedHue - _hue) < 0.4f)
            return;

        _svCache?.Dispose();
        _svCache = new Bitmap(sz.Width, sz.Height);
        _svCachedHue = _hue;
        _svCachedSize = sz;

        using var g = Graphics.FromImage(_svCache);
        Color pureHue = Theme.ColorFromHsv(_hue, 1f, 1f);
        var full = new Rectangle(0, 0, sz.Width, sz.Height);
        using (var satGrad = new LinearGradientBrush(full, Color.White, pureHue, 0f))
            g.FillRectangle(satGrad, full);
        using (var valGrad = new LinearGradientBrush(full, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), 90f))
            g.FillRectangle(valGrad, full);
    }

    private void EnsureHueCache(Size sz)
    {
        if (_hueCache is not null && _hueCachedSize == sz)
            return;

        _hueCache?.Dispose();
        _hueCache = new Bitmap(sz.Width, sz.Height);
        _hueCachedSize = sz;

        using var g = Graphics.FromImage(_hueCache);
        var full = new RectangleF(0, 0, sz.Width, sz.Height);
        using var grad = new LinearGradientBrush(full, Color.Red, Color.Red, 90f);
        grad.InterpolationColors = new ColorBlend
        {
            Colors =
            [
                Color.FromArgb(255, 255, 0, 0),
                Color.FromArgb(255, 255, 255, 0),
                Color.FromArgb(255, 0, 255, 0),
                Color.FromArgb(255, 0, 255, 255),
                Color.FromArgb(255, 0, 0, 255),
                Color.FromArgb(255, 255, 0, 255),
                Color.FromArgb(255, 255, 0, 0)
            ],
            Positions = [0f, 1f / 6f, 2f / 6f, 3f / 6f, 4f / 6f, 5f / 6f, 1f]
        };
        g.FillRectangle(grad, full);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _svCache?.Dispose();
            _hueCache?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ColorSwatchChip : Control
{
    public Color SwatchColor { get; set; }
    public bool IsSelected { get; set; }

    public ColorSwatchChip(Color color)
    {
        SwatchColor = color;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Size = new Size(26, 26);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.BgCard);

        var r = new RectangleF(3, 3, Width - 6, Height - 6);
        using (var b = new SolidBrush(SwatchColor))
            g.FillEllipse(b, r);
        using var p = new Pen(IsSelected ? Color.White : Theme.BorderStrong, IsSelected ? 2f : 1f);
        g.DrawEllipse(p, r);
    }
}

internal sealed class RgbStudioControl : UserControl
{
    private static readonly Color[] PresetColors =
    [
        Color.FromArgb(0, 180, 255),   // FiiO Cyan
        Color.FromArgb(59, 130, 246),  // Cobalt Blue
        Color.FromArgb(139, 92, 246),  // Violet
        Color.FromArgb(236, 72, 153),  // Magenta
        Color.FromArgb(239, 68, 68),   // Crimson
        Color.FromArgb(245, 158, 11),  // Warm Amber
        Color.FromArgb(16, 185, 129),  // Emerald
        Color.FromArgb(248, 250, 252)  // Ice White
    ];

    private readonly Label _modeTitle;
    private readonly SegmentedToggle<RgbStateMode> _stateToggle;
    private readonly RgbZonePreviewBar _previewBar;
    private readonly SegmentedToggle<int> _zoneTargetToggle;
    private readonly SegmentedToggle<RgbEffectMode> _effectToggle;
    private readonly Label _brightLbl;
    private readonly ModernSlider _brightnessSlider;
    private readonly HsvSpectrumPicker _hsvPicker;
    private readonly Label _presetLbl;
    private readonly Panel _activeColorBadge;
    private readonly TextBox _hexInput;
    private readonly ModernButton _syncAllBtn;
    private readonly List<ColorSwatchChip> _swatches = [];

    private readonly Color[] _zoneColors = [Color.DeepSkyBlue, Color.DeepSkyBlue, Color.DeepSkyBlue];
    private RgbStateMode _state = RgbStateMode.On;
    private RgbEffectMode _effect = RgbEffectMode.Solid;
    private int _brightness = 80;
    private int _selectedZone = -1; // -1 = All 3 zones
    private AppLanguage _language = AppLanguage.Ru;
    private bool _suppressEvents;

    public RgbStudioControl()
    {
        BackColor = Theme.BgWindow;
        Dock = DockStyle.Fill;

        // Card 1: Mode Switch (On / Off / Custom) + Live 3-Zone FIIO Emblem Preview (14px top/bottom pad)
        var topCard = new RoundedCardPanel { Location = new Point(0, 0), Size = new Size(526, 206), Padding = new Padding(16, 14, 16, 14) };

        _modeTitle = new Label { Text = "Режим подсветки", Font = Theme.FontSection, ForeColor = Theme.TextPrimary, Location = new Point(16, 14), AutoSize = true };
        _stateToggle = new SegmentedToggle<RgbStateMode> { Location = new Point(16, 38), Size = new Size(494, 34) };
        _stateToggle.SetItems(
            (RgbStateMode.On, "Вкл"),
            (RgbStateMode.Off, "Выкл"),
            (RgbStateMode.Custom, "Кастом"));
        _stateToggle.SelectedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _state = _stateToggle.SelectedValue;
            RefreshStudioUi();
        };

        _previewBar = new RgbZonePreviewBar { Location = new Point(16, 82), Size = new Size(494, 72) };

        _zoneTargetToggle = new SegmentedToggle<int> { Location = new Point(16, 162), Size = new Size(494, 30) };
        _previewBar.ZoneClicked += z =>
        {
            EnsureCustomMode();
            _zoneTargetToggle.SelectedValue = z;
        };
        _zoneTargetToggle.SetItems(
            (-1, "Все 3 зоны"),
            (0, "Зона 1 (Лев.)"),
            (1, "Зона 2 (Центр)"),
            (2, "Зона 3 (Прав.)"));
        _zoneTargetToggle.SelectedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _selectedZone = _zoneTargetToggle.SelectedValue;
            SyncPickerFromActiveZone();
            RefreshStudioUi();
        };

        topCard.Controls.AddRange([_modeTitle, _stateToggle, _previewBar, _zoneTargetToggle]);

        // Card 2: Custom Effect, Brightness & Integrated HSV Palette (10px below topCard, 14px top/bottom pad)
        var editCard = new RoundedCardPanel { Location = new Point(0, 216), Size = new Size(526, 250), Padding = new Padding(16, 14, 16, 14) };

        _effectToggle = new SegmentedToggle<RgbEffectMode> { Location = new Point(16, 14), Size = new Size(494, 34) };
        _effectToggle.SetItems(
            (RgbEffectMode.Solid, "Solid"),
            (RgbEffectMode.Breathing, "Breathing"),
            (RgbEffectMode.Rainbow, "Rainbow"),
            (RgbEffectMode.Chase, "Chase"));
        _effectToggle.SelectedChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _effect = _effectToggle.SelectedValue;
            EnsureCustomMode();
            RefreshStudioUi();
        };

        _brightLbl = new Label { Text = "Яркость:", Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Location = new Point(16, 56), Size = new Size(74, 28), TextAlign = ContentAlignment.MiddleLeft };
        _brightnessSlider = new ModernSlider { Location = new Point(92, 56), Size = new Size(418, 28), Minimum = 0, Maximum = 100 };
        _brightnessSlider.ValueChanged += (_, _) =>
        {
            if (_suppressEvents) return;
            _brightness = _brightnessSlider.Value;
            EnsureCustomMode();
            RefreshStudioUi();
        };

        _hsvPicker = new HsvSpectrumPicker { Location = new Point(10, 94), Size = new Size(242, 149) };
        _hsvPicker.ColorPicked += (_, c) =>
        {
            if (_suppressEvents) return;
            EnsureCustomMode();
            ApplyColorToSelection(c);
        };

        _presetLbl = new Label { Text = "Быстрые цвета и HEX:", Font = Theme.FontSmall, ForeColor = Theme.TextSecondary, Location = new Point(262, 98), AutoSize = true, UseMnemonic = false };
        var swatchPanel = new FlowLayoutPanel
        {
            Location = new Point(262, 118),
            Size = new Size(248, 30),
            WrapContents = false,
            BackColor = Theme.BgCard,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        for (int idx = 0; idx < PresetColors.Length; idx++)
        {
            Color preset = PresetColors[idx];
            var chip = new ColorSwatchChip(preset)
            {
                Margin = idx < PresetColors.Length - 1 ? new Padding(0, 2, 5, 0) : new Padding(0, 2, 0, 0)
            };
            chip.Click += (_, _) =>
            {
                EnsureCustomMode();
                ApplyColorToSelection(chip.SwatchColor);
                SyncPickerFromActiveZone();
            };
            _swatches.Add(chip);
            swatchPanel.Controls.Add(chip);
        }

        var hexLbl = new Label { Text = "HEX:", Font = Theme.FontBody, ForeColor = Theme.TextSecondary, Location = new Point(262, 156), Size = new Size(36, 30), TextAlign = ContentAlignment.MiddleLeft };

        _activeColorBadge = new Panel
        {
            Location = new Point(302, 156),
            Size = new Size(38, 30),
            BackColor = Theme.BgCard
        };
        _activeColorBadge.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.BgCard);
            Color cur = _selectedZone >= 0 ? _zoneColors[_selectedZone] : _zoneColors[0];
            var r = new RectangleF(0.5f, 0.5f, _activeColorBadge.Width - 1.5f, _activeColorBadge.Height - 1.5f);
            using var path = Theme.CreateRoundedPath(r, 6f);
            using var fill = new SolidBrush(cur);
            using var pen = new Pen(Theme.BorderStrong, 1f);
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        };

        var hexBoxWrap = new Panel
        {
            Location = new Point(348, 156),
            Size = new Size(162, 30),
            BackColor = Theme.BgInput
        };
        hexBoxWrap.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderStrong, 1f);
            e.Graphics.DrawRectangle(pen, 0, 0, hexBoxWrap.Width - 1, hexBoxWrap.Height - 1);
        };
        _hexInput = new TextBox
        {
            Width = 154,
            BackColor = Theme.BgInput,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.None,
            Font = Theme.FontMono,
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 7
        };
        _hexInput.Location = new Point(4, Math.Max(3, (hexBoxWrap.Height - _hexInput.PreferredHeight) / 2));
        hexBoxWrap.Controls.Add(_hexInput);
        hexBoxWrap.Click += (_, _) => _hexInput.Focus();

        _hexInput.Leave += (_, _) => TryApplyHexText();
        _hexInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                TryApplyHexText();
            }
        };

        _syncAllBtn = new ModernButton
        {
            Text = "Применить цвет ко всем 3 зонам",
            Location = new Point(262, 196),
            Size = new Size(248, 40),
            Variant = ButtonVariant.Secondary
        };
        _syncAllBtn.Click += (_, _) =>
        {
            EnsureCustomMode();
            Color col = _selectedZone >= 0 ? _zoneColors[_selectedZone] : _zoneColors[0];
            for (int i = 0; i < 3; i++)
                _zoneColors[i] = col;
            _selectedZone = -1;
            _zoneTargetToggle.SetSelectedSilently(-1);
            RefreshStudioUi();
        };

        editCard.Controls.AddRange([
            _effectToggle, _brightLbl, _brightnessSlider,
            _hsvPicker, _presetLbl, swatchPanel, hexLbl, _activeColorBadge, hexBoxWrap, _syncAllBtn
        ]);

        Controls.Add(topCard);
        Controls.Add(editCard);
        ApplyLanguage(_language);
        RefreshStudioUi();
    }

    public void ApplyLanguage(AppLanguage lang)
    {
        _language = lang;
        _modeTitle.Text = AppStrings.RgbModeTitle(lang);
        _stateToggle.UpdateLabels(
            (RgbStateMode.On, AppStrings.RgbOn(lang)),
            (RgbStateMode.Off, AppStrings.RgbOff(lang)),
            (RgbStateMode.Custom, AppStrings.RgbCustom(lang)));
        _effectToggle.UpdateLabels(
            (RgbEffectMode.Solid, AppStrings.RgbEffectName(lang, RgbEffectMode.Solid)),
            (RgbEffectMode.Breathing, AppStrings.RgbEffectName(lang, RgbEffectMode.Breathing)),
            (RgbEffectMode.Rainbow, AppStrings.RgbEffectName(lang, RgbEffectMode.Rainbow)),
            (RgbEffectMode.Chase, AppStrings.RgbEffectName(lang, RgbEffectMode.Chase)));
        _zoneTargetToggle.UpdateLabels(
            (-1, AppStrings.RgbZoneAll(lang)),
            (0, AppStrings.RgbZone1(lang)),
            (1, AppStrings.RgbZone2(lang)),
            (2, AppStrings.RgbZone3(lang)));
        _brightLbl.Text = AppStrings.RgbBrightLbl(lang);
        _presetLbl.Text = AppStrings.RgbPresetsLbl(lang);
        _syncAllBtn.Text = AppStrings.RgbSyncAllBtn(lang);
        RefreshStudioUi();
    }

    public void ApplyExternalStateMode(RgbStateMode state)
    {
        _suppressEvents = true;
        try
        {
            _state = state;
            _stateToggle.SetSelectedSilently(state);
            RefreshStudioUi();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void LoadFromConfig(RgbConfig cfg)
    {
        _suppressEvents = true;
        try
        {
            _state = cfg.State;
            _effect = cfg.Effect;
            _brightness = Math.Clamp(cfg.Brightness, 0, 100);
            for (int i = 0; i < 3; i++)
                _zoneColors[i] = ParseHexOrDefault(cfg.ZoneHexColors.ElementAtOrDefault(i), Color.FromArgb(0, 180, 255));

            _stateToggle.SetSelectedSilently(_state);
            _effectToggle.SetSelectedSilently(_effect);
            _brightnessSlider.SetValueSilently(_brightness);
            _zoneTargetToggle.SetSelectedSilently(_selectedZone);
            SyncPickerFromActiveZone();
            RefreshStudioUi();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    public void WriteToConfig(RgbConfig cfg)
    {
        cfg.State = _state;
        cfg.Effect = _effect;
        cfg.Brightness = _brightness;
        cfg.ZoneHexColors = _zoneColors.Select(FormatHex).ToArray();
    }

    private void EnsureCustomMode()
    {
        if (_state == RgbStateMode.Custom)
            return;
        _state = RgbStateMode.Custom;
        _stateToggle.SetSelectedSilently(RgbStateMode.Custom);
    }

    private void ApplyColorToSelection(Color c)
    {
        if (_selectedZone < 0)
        {
            for (int i = 0; i < 3; i++)
                _zoneColors[i] = c;
        }
        else
        {
            _zoneColors[_selectedZone] = c;
        }
        RefreshStudioUi();
    }

    private void SyncPickerFromActiveZone()
    {
        Color c = _selectedZone >= 0 ? _zoneColors[_selectedZone] : _zoneColors[0];
        _hsvPicker.SetColorSilently(c);
        _hexInput.Text = FormatHex(c);
    }

    private void TryApplyHexText()
    {
        string raw = _hexInput.Text.Trim().TrimStart('#');
        if (raw.Length == 6
            && byte.TryParse(raw[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(raw.SliceOrSubstring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(raw.SliceOrSubstring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            EnsureCustomMode();
            var col = Color.FromArgb(r, g, b);
            ApplyColorToSelection(col);
            SyncPickerFromActiveZone();
        }
        else
        {
            SyncPickerFromActiveZone();
        }
    }

    private void RefreshStudioUi()
    {
        Color active = _selectedZone >= 0 ? _zoneColors[_selectedZone] : _zoneColors[0];
        string hex = FormatHex(active);
        if (!string.Equals(_hexInput.Text, hex, StringComparison.OrdinalIgnoreCase) && !_hexInput.Focused)
            _hexInput.Text = hex;

        foreach (var chip in _swatches)
        {
            bool match = chip.SwatchColor.R == active.R && chip.SwatchColor.G == active.G && chip.SwatchColor.B == active.B;
            if (chip.IsSelected != match)
            {
                chip.IsSelected = match;
                chip.Invalidate();
            }
        }

        _activeColorBadge.Invalidate();
        _previewBar.UpdateState(_state, _effect, _brightness, _zoneColors, _selectedZone);
    }

    private static string FormatHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ParseHexOrDefault(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;
        string s = hex.Trim().TrimStart('#');
        if (s.Length == 6
            && byte.TryParse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r)
            && byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g)
            && byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return Color.FromArgb(r, g, b);
        }
        return fallback;
    }
}

internal static class StringCompatExtensions
{
    public static string SliceOrSubstring(this string s, int start, int length) => s.Substring(start, length);
}
