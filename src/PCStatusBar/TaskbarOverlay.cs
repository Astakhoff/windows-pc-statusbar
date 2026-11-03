using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace PCStatusBar;

internal record TaskbarFrame(long Handle, Rectangle Bounds, int Dpi);
internal record TaskbarMeasurement(long Handle, Rectangle Bounds, int Dpi, int OccupiedLeft, int OccupiedRight, DateTimeOffset At);

internal static class TaskbarOverlay
{
    delegate bool EnumWindowCallback(nint hwnd, nint data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowCallback callback, nint data);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int length);
    public static TaskbarFrame? FindForMonitor(Rectangle monitor)
    {
        var bars = new List<TaskbarFrame>();
        EnumWindows((hwnd, _) =>
        {
            var cls = new StringBuilder(128); GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" && Native.GetWindowRect(hwnd, out var rect))
                bars.Add(new((long)hwnd, rect.Rectangle, Math.Max(96, (int)GetDpiForWindow(hwnd))));
            return true;
        }, 0);
        return bars.Where(b => b.Bounds.IntersectsWith(monitor)).OrderByDescending(b => Rectangle.Intersect(b.Bounds, monitor).Width).FirstOrDefault()
            ?? bars.FirstOrDefault(b => (nint)b.Handle == Native.FindWindow("Shell_TrayWnd", null));
    }
    public static TaskbarMeasurement? ReadOccupied(TaskbarFrame frame)
    {
        try
        {
            var root = AutomationElement.FromHandle((nint)frame.Handle);
            var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var rectangles = new List<Rectangle>();
            foreach (AutomationElement button in buttons)
            {
                var r = button.Current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0 && r.Height > 0) rectangles.Add(new((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
            }
            if (!Native.GetWindowRect((nint)frame.Handle, out var actual) || actual.Rectangle != frame.Bounds) return null;
            var occupied = OverlayGeometry.OccupiedInsets(frame.Bounds, rectangles);
            return occupied.Valid ? new(frame.Handle, frame.Bounds, frame.Dpi, occupied.Left, occupied.Right, DateTimeOffset.UtcNow) : null;
        }
        catch { return null; }
    }
    public static bool FullscreenForeground(nint own, Rectangle screen)
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == own) return false;
        var cls = new StringBuilder(128); GetClassName(foreground, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (((long)Native.GetWindowLongPtr(foreground, -16) & 0x00c00000) != 0) return false;
        return Native.GetWindowRect(foreground, out var rect) && rect.Left <= screen.Left && rect.Top <= screen.Top && rect.Right >= screen.Right && rect.Bottom >= screen.Bottom;
    }
    public static object[] ReadShellRegions()
    {
        try
        {
            nint hwnd = Native.FindWindow("Shell_TrayWnd", null);
            if (hwnd == 0) return [];
            var root = AutomationElement.FromHandle(hwnd);
            var elements = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            var result = new List<object>();
            foreach (AutomationElement item in elements)
            {
                var current = item.Current; var r = current.BoundingRectangle;
                if (!r.IsEmpty && r.Width > 0 && r.Height > 0)
                    result.Add(new { current.Name, Type = current.ControlType.ProgrammaticName, Bounds = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height) });
            }
            return result.ToArray();
        }
        catch { return []; }
    }
}
