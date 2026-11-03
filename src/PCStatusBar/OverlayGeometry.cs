using System.Drawing;

namespace PCStatusBar;

public static class OverlayGeometry
{
    public static (int Left, int Right, bool Valid) OccupiedInsets(Rectangle taskbar, IEnumerable<Rectangle> buttons)
    {
        var items = buttons.Select(r => Rectangle.Intersect(taskbar, r)).Where(r => r.Width > 0 && r.Height > 0 && r.Width < taskbar.Width * 0.8).OrderByDescending(r => r.Right).ToArray();
        if (items.Length == 0 || taskbar.Right - items[0].Right > taskbar.Height * 2) return (0, 0, false);
        int trayLeft = taskbar.Right;
        foreach (var r in items)
        {
            if (r.Right < trayLeft - taskbar.Height) break;
            trayLeft = Math.Min(trayLeft, r.Left);
        }
        int appsRight = items.Where(r => r.Right <= trayLeft).Select(r => r.Right).DefaultIfEmpty(taskbar.Left).Max();
        return (appsRight - taskbar.Left, taskbar.Right - trayLeft, true);
    }

    public static Rectangle Adaptive(Rectangle taskbar, int referenceWidth, int leftMargin, int rightMargin,
        int logicalHeight, double scale, int occupiedLeft, int occupiedRight)
    {
        double ratio = taskbar.Width / (double)Math.Max(1, referenceWidth);
        int left = Math.Clamp((int)Math.Round(leftMargin * ratio), 0, taskbar.Width);
        int right = Math.Clamp((int)Math.Round(rightMargin * ratio), 0, taskbar.Width);
        int guard = Math.Max(4, (int)Math.Round(10 * scale));
        left = Math.Clamp(Math.Max(left, occupiedLeft + guard), 0, taskbar.Width);
        int end = Math.Clamp(Math.Min(taskbar.Width - right, taskbar.Width - occupiedRight - guard), left, taskbar.Width);
        int height = Math.Clamp((int)Math.Round(logicalHeight * scale), 1, Math.Max(1, taskbar.Height));
        return new(taskbar.Left + left, taskbar.Top + (taskbar.Height - height) / 2, Math.Max(0, end - left), height);
    }

    public static Rectangle Calculate(Rectangle taskbar, int leftMargin, int rightMargin, int logicalHeight, double scale)
    {
        int left = Math.Clamp((int)(leftMargin * scale), 0, Math.Max(0, taskbar.Width - 200));
        int right = Math.Clamp((int)(rightMargin * scale), 0, Math.Max(0, taskbar.Width - left - 200));
        int height = Math.Min((int)Math.Round(logicalHeight * scale), taskbar.Height);
        return new(taskbar.Left + left, taskbar.Top + (taskbar.Height - height) / 2, taskbar.Width - left - right, height);
    }
}
