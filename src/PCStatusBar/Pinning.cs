using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.UI.Shell;

namespace PCStatusBar;

internal static class Pinning
{
    internal const string AppId = "Astakhoff.PCStatusBar";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string? id);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    public static bool DesktopSupported()
    {
        const string name = "Windows.UI.Shell.TaskbarManager";
        nint value = 0, factory = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(name, name.Length, out value));
            // ITaskbarManagerDesktopAppSupportStatics, Windows SDK / windows-rs projection.
            var iid = new Guid("cdfefd63-e879-4134-b9a7-8283f05f9480");
            return RoGetActivationFactory(value, ref iid, out factory) >= 0;
        }
        catch (Exception e) { Log.Write("TaskbarManager: " + e.Message); return false; }
        finally { if (factory != 0) Marshal.Release(factory); if (value != 0) WindowsDeleteString(value); }
    }
    static bool TokenRequired()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\LimitedAccessFeatures\com.microsoft.windows.taskbar.pin");
        return key?.GetValue("4096B239A7295B635C090E647E867B5707DA6AB6CB78340B01FE4E0C8F4953D4") is int value && value != 0;
    }
    public static async Task<string> Availability()
    {
        try
        {
            if (!DesktopSupported()) return "TaskbarManager для desktop недоступен";
            if (TokenRequired()) return "Эта Windows требует токен Limited Access Feature";
            var manager = TaskbarManager.GetDefault();
            if (!manager.IsPinningAllowed) return "Windows сейчас не разрешает запрос закрепления";
            return await manager.IsCurrentAppPinnedAsync() ? "Ярлык уже закреплён" : "Доступно";
        }
        catch (Exception e) { Log.Write("TaskbarManager: " + e.Message); return "API закрепления недоступен для этого приложения"; }
    }
    public static async Task<string> Request()
    {
        var state = await Availability();
        if (state != "Доступно") return state;
        // Called only by the settings button on the foreground UI thread.
        return await TaskbarManager.GetDefault().RequestPinCurrentAppAsync() ? "Ярлык закреплён" : "Windows не закрепила ярлык (отказ или неподходящие условия)";
    }
}

internal static class ShortcutIdentity
{
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out Variant value);
        void SetValue(ref PropertyKey key, ref Variant value); void Commit();
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetPropertyStoreFromParsingName(string path, nint bind, uint flags, ref Guid iid, out IPropertyStore store);
    public static void Set(string path)
    {
        if (!File.Exists(path)) return;
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, 0, 2, ref iid, out var store));
        var key = new PropertyKey { Format = new Guid("9f4c2855-9f79-4b39-a8d0-e1d42de1d5f3"), Id = 5 };
        var value = new Variant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(Pinning.AppId) };
        try { store.SetValue(ref key, ref value); store.Commit(); }
        finally { Marshal.FreeCoTaskMem(value.Pointer); Marshal.FinalReleaseComObject(store); }
    }
    public static void Register()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.Startup })
            Set(Path.Combine(Environment.GetFolderPath(folder), "PCStatusBar.lnk"));
    }
}
