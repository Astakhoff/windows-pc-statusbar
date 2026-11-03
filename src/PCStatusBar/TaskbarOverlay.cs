using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace PCStatusBar;

internal static class TaskbarOverlay
{
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(nint hwnd, StringBuilder name, int length);
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
