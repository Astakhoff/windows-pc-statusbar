using System.Drawing;

namespace PCStatusBar;

public static class OverlayGeometry
{
    public static Rectangle Calculate(Rectangle taskbar, int leftMargin, int rightMargin, int logicalHeight, double scale)
    {
        int left = Math.Clamp((int)(leftMargin * scale), 0, Math.Max(0, taskbar.Width - 200));
        int right = Math.Clamp((int)(rightMargin * scale), 0, Math.Max(0, taskbar.Width - left - 200));
        int height = Math.Min((int)Math.Round(logicalHeight * scale), taskbar.Height);
        return new(taskbar.Left + left, taskbar.Top + (taskbar.Height - height) / 2, taskbar.Width - left - right, height);
    }
}
