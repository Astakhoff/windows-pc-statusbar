using System.Drawing.Drawing2D;

namespace PCStatusBar;

internal sealed class CpuGraph : Control
{
    CpuPoint[] points = [];
    double now, maximumGap;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color LineColor { get; set; } = Color.Silver;
    public CpuGraph()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(32, 36, 43); ForeColor = Color.Silver;
        AccessibleName = "График CPU за последние 60 секунд";
    }
    public void UpdateHistory(CpuPoint[] samples, double time, double gap)
    {
        points = samples; now = time; maximumGap = gap;
        var valid = points.Where(p => p.Value.HasValue).ToArray();
        AccessibleDescription = valid.Length == 0 ? "Нет данных" : $"CPU: среднее {valid.Average(p => p.Value!.Value):0}%, максимум {valid.Max(p => p.Value):0}%; окно 60 секунд";
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96F;
        int labelWidth = (int)(52 * scale), left = labelWidth + 3, right = Width - 5, top = 5, bottom = Height - 5;
        TextRenderer.DrawText(e.Graphics, "60 с", Font, new Rectangle(4, 0, labelWidth, Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (right <= left || bottom <= top) return;
        using var grid = new Pen(Color.FromArgb(60, 66, 77));
        e.Graphics.DrawLine(grid, left, bottom, right, bottom); e.Graphics.DrawLine(grid, left, top, right, top);
        using var pen = new Pen(LineColor, Math.Max(1.5F, scale));
        PointF Project(CpuPoint point) => new(left + (float)((point.At - (now - 60)) / 60) * (right - left), bottom - (float)point.Value!.Value / 100 * (bottom - top));
        for (int i = 1; i < points.Length; i++)
            if (CpuHistory.CanConnect(points[i - 1], points[i], maximumGap)) e.Graphics.DrawLine(pen, Project(points[i - 1]), Project(points[i]));
        if (points.Length > 0 && points[^1].Value.HasValue)
        { using var dot = new SolidBrush(LineColor); var p = Project(points[^1]); e.Graphics.FillEllipse(dot, p.X - 2, p.Y - 2, 4, 4); }
    }
}
