using System.Diagnostics;

namespace PCStatusBar;

internal sealed class QuotaTicker : Control
{
    readonly System.Windows.Forms.Timer animation = new() { Interval = 33 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    string message = "Кодекс: проверка лимитов…     ◆     Клод: проверка лимитов…";
    double offset, lastTime;
    bool paused;
    int textWidth;
    public QuotaTicker()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(23, 26, 32); ForeColor = Color.FromArgb(196, 205, 220);
        AccessibleName = "Остатки лимитов Codex и Claude";
        animation.Tick += (_, _) =>
        {
            double now = clock.Elapsed.TotalSeconds, delta = Math.Min(0.2, now - lastTime); lastTime = now;
            if (!Visible || paused || Width <= 0 || textWidth <= 0) return;
            offset = (offset + delta * 42 * DeviceDpi / 96.0) % (textWidth + 70 * DeviceDpi / 96.0);
            Invalidate();
        };
        MouseEnter += (_, _) => paused = true; MouseLeave += (_, _) => paused = false;
        animation.Start();
    }
    public void SetMessage(string value)
    {
        if (message == value) return;
        message = value; AccessibleDescription = value;
        textWidth = TextRenderer.MeasureText(message, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        Invalidate();
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); textWidth = 0; if (message != null) textWidth = TextRenderer.MeasureText(message, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (textWidth <= 0) textWidth = TextRenderer.MeasureText(message, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        int period = Math.Max(1, textWidth + (int)(70 * DeviceDpi / 96F));
        for (int x = -(int)offset; x < Width; x += period)
            TextRenderer.DrawText(e.Graphics, message, Font, new Rectangle(x, 0, textWidth + 2, Height), ForeColor,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.PreserveGraphicsClipping);
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
