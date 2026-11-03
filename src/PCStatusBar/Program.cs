namespace PCStatusBar;

internal static class Program
{
    internal static readonly uint ControlMessage = Native.RegisterWindowMessage("PCStatusBar.Control.v1");
    internal const string WindowTitle = "PCStatusBar — состояние Windows";
    [STAThread]
    static void Main(string[] args)
    {
        Pinning.SetCurrentProcessExplicitAppUserModelID(Pinning.AppId);
        if (args.Contains("--register-shortcuts")) { ShortcutIdentity.Register(); return; }
        using var mutex = new Mutex(true, @"Local\PCStatusBar.v1", out bool first);
        if (!first)
        {
            var hwnd = Native.FindWindow(null, WindowTitle);
            int command = args.Contains("--exit") ? 1 : args.Contains("--hide") ? 2 : args.Contains("--show") ? 3 : args.Contains("--diagnostics") ? 4 : args.Contains("--inspect") ? 5 : args.Contains("--end-inspect") ? 6 : args.Contains("--check-maximized") ? 7 : 0;
            if (hwnd != 0) Native.PostMessage(hwnd, ControlMessage, command, 0);
            return;
        }
        if (args.Contains("--exit")) return;
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => Log.Write(e.Exception.ToString());
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write(e.ExceptionObject.ToString() ?? "Unhandled error");
        Application.Run(new BarForm());
    }
}
