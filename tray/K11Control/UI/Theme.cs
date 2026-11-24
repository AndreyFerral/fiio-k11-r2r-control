using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace K11Control.UI;

internal static class Theme
{
    // Deep matte audio dark palette
    public static readonly Color BgWindow = Color.FromArgb(16, 18, 24);
    public static readonly Color BgSidebar = Color.FromArgb(20, 23, 30);
    public static readonly Color BgCard = Color.FromArgb(26, 30, 39);
    public static readonly Color BgElevated = Color.FromArgb(35, 40, 52);
    public static readonly Color BgInput = Color.FromArgb(18, 21, 28);
    public static readonly Color BgMenuHover = Color.FromArgb(40, 48, 64);

    public static readonly Color BorderSubtle = Color.FromArgb(44, 51, 66);
    public static readonly Color BorderStrong = Color.FromArgb(66, 76, 98);

    public static readonly Color Accent = Color.FromArgb(0, 174, 239);
    public static readonly Color AccentHover = Color.FromArgb(36, 192, 250);
    public static readonly Color AccentSoft = Color.FromArgb(24, 0, 174, 239);
    public static readonly Color AccentAmber = Color.FromArgb(245, 158, 11);
    public static readonly Color AccentAmberHover = Color.FromArgb(251, 176, 45);
    public static readonly Color Success = Color.FromArgb(34, 197, 94);
    public static readonly Color Danger = Color.FromArgb(239, 68, 68);
    public static readonly Color DangerSoft = Color.FromArgb(45, 239, 68, 68);

    public static readonly Color TextPrimary = Color.FromArgb(243, 244, 246);
    public static readonly Color TextSecondary = Color.FromArgb(156, 163, 175);
    public static readonly Color TextMuted = Color.FromArgb(107, 114, 128);

    public static readonly Font FontBrand = new("Segoe UI", 12.5f, FontStyle.Bold);
    public static readonly Font FontTitle = new("Segoe UI", 14.5f, FontStyle.Bold);
    public static readonly Font FontSection = new("Segoe UI Semibold", 10f, FontStyle.Bold);
    public static readonly Font FontBody = new("Segoe UI", 9.2f, FontStyle.Regular);
    public static readonly Font FontBodyBold = new("Segoe UI Semibold", 9.2f, FontStyle.Bold);
    public static readonly Font FontSmall = new("Segoe UI", 8.2f, FontStyle.Regular);
    public static readonly Font FontMono = new("Consolas", 9.5f, FontStyle.Regular);
    public static readonly Font FontLcdBig = new("Consolas", 20f, FontStyle.Bold);

    public static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return path;

        float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.A + (b.A - a.A) * t),
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static void ColorToHsv(Color color, out float hue, out float saturation, out float value)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));
        int delta = max - min;

        hue = 0f;
        if (delta > 0)
        {
            if (max == color.R)
                hue = 60f * (((color.G - color.B) / (float)delta + 6f) % 6f);
            else if (max == color.G)
                hue = 60f * ((color.B - color.R) / (float)delta + 2f);
            else
                hue = 60f * ((color.R - color.G) / (float)delta + 4f);
        }

        saturation = max == 0 ? 0f : delta / (float)max;
        value = max / 255f;
    }

    public static Color ColorFromHsv(float hue, float saturation, float value)
    {
        hue = ((hue % 360f) + 360f) % 360f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);

        int hi = (int)MathF.Floor(hue / 60f) % 6;
        float f = hue / 60f - MathF.Floor(hue / 60f);

        float v = value * 255f;
        int vInt = (int)Math.Round(v);
        int p = (int)Math.Round(v * (1f - saturation));
        int q = (int)Math.Round(v * (1f - f * saturation));
        int t = (int)Math.Round(v * (1f - (1f - f) * saturation));

        return hi switch
        {
            0 => Color.FromArgb(255, vInt, t, p),
            1 => Color.FromArgb(255, q, vInt, p),
            2 => Color.FromArgb(255, p, vInt, t),
            3 => Color.FromArgb(255, p, q, vInt),
            4 => Color.FromArgb(255, t, p, vInt),
            _ => Color.FromArgb(255, vInt, p, q)
        };
    }

    private static readonly Lazy<Icon> CachedAppIcon = new(() => LoadAppIconCore(SystemInformation.IconSize));
    public static Icon AppIcon => CachedAppIcon.Value;

    public static Icon CreateTrayIcon(bool muted = false)
    {
        Icon baseIcon = LoadAppIconCore(SystemInformation.SmallIconSize);
        if (!muted)
            return baseIcon;

        try
        {
            using Icon source = baseIcon;
            Size sz = SystemInformation.SmallIconSize;
            using var bmp = new Bitmap(sz.Width, sz.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawIcon(source, new Rectangle(0, 0, sz.Width, sz.Height));

                float badge = Math.Max(6f, sz.Width * 0.44f);
                float bx = sz.Width - badge - 0.5f;
                float by = sz.Height - badge - 0.5f;
                using var rim = new SolidBrush(BgSidebar);
                using var fill = new SolidBrush(Danger);
                g.FillEllipse(rim, bx - 1f, by - 1f, badge + 2f, badge + 2f);
                g.FillEllipse(fill, bx, by, badge, badge);
            }

            IntPtr hIcon = bmp.GetHicon();
            try
            {
                using Icon temp = Icon.FromHandle(hIcon);
                return (Icon)temp.Clone();
            }
            finally
            {
                _ = DestroyIcon(hIcon);
            }
        }
        catch
        {
            return LoadAppIconCore(SystemInformation.SmallIconSize);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon LoadAppIconCore(Size desiredSize)
    {
        try
        {
            var asm = typeof(Theme).Assembly;
            string? resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("K11Control.ico", StringComparison.OrdinalIgnoreCase));
            if (resName is not null)
            {
                using Stream? stream = asm.GetManifestResourceStream(resName);
                if (stream is not null)
                    return new Icon(stream, desiredSize);
            }

            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                return Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
        }
        catch
        {
            // Fallback below
        }
        return SystemIcons.Application;
    }

    public static void ApplyDarkWindowChrome(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            int enabled = 1;
            const int DwmwaUseImmersiveDarkMode = 20;
            const int DwmwaUseImmersiveDarkModeLegacy = 19;
            const int DwmwaCaptionColor = 35;

            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));

            // COLORREF is 0x00BBGGRR matching BgWindow (16, 18, 24)
            int captionRef = (BgWindow.B << 16) | (BgWindow.G << 8) | BgWindow.R;
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionRef, sizeof(int));
        }
        catch
        {
            // Safe no-op on older Windows versions without DWM attributes
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Theme.BgSidebar);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.BorderSubtle);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Suppress bright left image column cleanly.
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.BorderSubtle);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled)
            return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(6, 1, e.Item.Width - 12, e.Item.Height - 2);
        if (e.Item.Selected)
        {
            using var path = Theme.CreateRoundedPath(rect, 5f);
            using var brush = new SolidBrush(Theme.BgMenuHover);
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var dot = new RectangleF(11f, e.Item.Height / 2f - 3.5f, 7f, 7f);
        using var brush = new SolidBrush(Theme.Accent);
        e.Graphics.FillEllipse(brush, dot);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item is ToolStripMenuItem mi)
        {
            bool isShortcut = !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString)
                && string.Equals(e.Text, mi.ShortcutKeyDisplayString, StringComparison.Ordinal);

            if (isShortcut)
            {
                e.TextColor = Theme.TextMuted;
                e.TextFont = Theme.FontSmall;
            }
            else if (!mi.Enabled)
            {
                e.TextColor = Theme.TextSecondary;
                e.TextFont = Theme.FontSmall;
            }
            else
            {
                e.TextColor = mi.Checked ? Theme.Accent : Theme.TextPrimary;
                e.TextFont = mi.Checked ? Theme.FontBodyBold : Theme.FontBody;
            }
        }
        else
        {
            e.TextColor = Theme.TextPrimary;
        }

        base.OnRenderItemText(e);
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.BgSidebar;
        public override Color MenuBorder => Theme.BorderSubtle;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.BgMenuHover;
        public override Color ImageMarginGradientBegin => Theme.BgSidebar;
        public override Color ImageMarginGradientMiddle => Theme.BgSidebar;
        public override Color ImageMarginGradientEnd => Theme.BgSidebar;
        public override Color SeparatorDark => Theme.BorderSubtle;
        public override Color SeparatorLight => Theme.BorderSubtle;
    }
}
