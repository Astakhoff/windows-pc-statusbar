using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace PCStatusBar;

internal sealed class QuotaTicker : Control
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 33 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    QuotaTagGroup[] groups = [];
    readonly List<(string Text, int X, int Width, Color Background, Color Foreground, bool Bold)> tags = [];
    string fingerprint = "";
    Font? boldFont;
    double offset, lastTime;
    bool paused;
    int period;
    const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping;
    public QuotaTicker()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(23, 26, 32); ForeColor = Color.FromArgb(196, 205, 220);
        AccessibleName = "Остатки лимитов Codex и Claude";
        animation.Tick += (_, _) =>
        {
            double now = clock.Elapsed.TotalSeconds, delta = Math.Min(0.2, now - lastTime); lastTime = now;
            if (!Visible || paused || Width <= 0 || period <= 0) return;
            offset = (offset + delta * 42 * DeviceDpi / 96.0) % period;
            Invalidate();
        };
        MouseEnter += (_, _) => paused = true; MouseLeave += (_, _) => paused = false;
        animation.Start();
    }
    public void SetQuotas(QuotaSnapshot[] snapshots, DateTimeOffset now, QuotaVisibility visibility)
    {
        string value = QuotaText.Ticker(snapshots, now, visibility);
        if (fingerprint == value) return;
        fingerprint = value; AccessibleDescription = value;
        groups = QuotaText.Tags(snapshots, now, visibility); period = 0;
        Invalidate();
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); boldFont?.Dispose(); boldFont = null; period = 0; }
    void MeasureTags()
    {
        tags.Clear(); boldFont ??= new Font(Font, FontStyle.Bold);
        float scale = DeviceDpi / 96F;
        int x = 0, gap = (int)(4 * scale), padding = (int)(9 * scale);
        void Add(string text, Color background, Color foreground, bool bold = false)
        {
            int width = TextRenderer.MeasureText(text, bold ? boldFont : Font, Size.Empty, TextFlags).Width + padding * 2;
            tags.Add((text, x, width, background, foreground, bold)); x += width + gap;
        }
        foreach (var group in groups)
        {
            bool codex = group.Provider == "Codex";
            Add(group.Provider, codex ? Color.FromArgb(19, 83, 67) : Color.FromArgb(106, 60, 42), codex ? Color.FromArgb(175, 255, 221) : Color.FromArgb(255, 214, 185), true);
            Add(group.Period, Color.FromArgb(41, 58, 82), Color.FromArgb(190, 216, 255));
            (Color bg, Color fg) = group.State switch
            {
                "available" => (Color.FromArgb(32, 69, 52), Color.FromArgb(149, 239, 180)),
                "attention" => (Color.FromArgb(85, 64, 26), Color.FromArgb(255, 217, 127)),
                "low" => (Color.FromArgb(89, 39, 49), Color.FromArgb(255, 163, 177)),
                _ => (Color.FromArgb(47, 53, 64), Color.FromArgb(179, 190, 208))
            };
            Add(group.Value, bg, fg, true);
            if (group.Reset != null) Add(group.Reset, Color.FromArgb(32, 39, 51), Color.FromArgb(193, 204, 222));
            x += (int)(24 * scale);
        }
        period = Math.Max(1, x + (int)(22 * scale)); offset %= period;
    }
    static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath(); float diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (groups.Length == 0) return;
        if (period <= 0) MeasureTags();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96F;
        int height = Math.Min((int)(27 * scale), Math.Max(1, Height - (int)(6 * scale))), y = (Height - height) / 2;
        for (int start = -(int)offset; start < Width; start += period)
            foreach (var tag in tags)
            {
                int x = start + tag.X;
                if (x + tag.Width < 0 || x >= Width) continue;
                var rect = new Rectangle(x, y, tag.Width, height);
                using var path = Rounded(rect, Math.Min(5 * scale, height / 2F));
                using var brush = new SolidBrush(tag.Background); e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(e.Graphics, tag.Text, tag.Bold ? boldFont : Font, rect, tag.Foreground, TextFlags | TextFormatFlags.HorizontalCenter);
            }
    }
    protected override void Dispose(bool disposing) { if (disposing) { animation.Dispose(); boldFont?.Dispose(); } base.Dispose(disposing); }
}
