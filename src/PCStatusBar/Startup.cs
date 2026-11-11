using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace PCStatusBar;

internal static class Startup
{
    public static string Link => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "PCStatusBar.lnk");
    static string UserSid { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; } }
    public static string TaskName => "PCStatusBar-" + UserSid;
    public static bool Enabled
    {
        get
        {
            try { return WithFolder(folder => { dynamic? task = GetTask(folder); try { return task != null ? (bool)task.Enabled : File.Exists(Link); } finally { Release(task); } }); }
            catch (Exception e) { Log.Write("Startup status: " + e.Message); return File.Exists(Link); }
        }
    }
    public static void Set(bool enabled)
    {
        WithFolder(folder =>
        {
            if (!enabled)
            {
                dynamic? existing = GetTask(folder);
                try { if (existing != null) folder.DeleteTask(TaskName, 0); }
                finally { Release(existing); }
            }
            else
            {
                string exe = SecurityElement.Escape(Environment.ProcessPath!)!;
                string directory = SecurityElement.Escape(AppContext.BaseDirectory)!;
                string sid = UserSid;
                string xml = $"""
                    <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                      <RegistrationInfo><Description>PCStatusBar: запуск панели после входа в Windows.</Description></RegistrationInfo>
                      <Triggers><LogonTrigger><Enabled>true</Enabled><Delay>PT15S</Delay><UserId>{sid}</UserId></LogonTrigger></Triggers>
                      <Principals><Principal id="User"><UserId>{sid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>
                      <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>false</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure></Settings>
                      <Actions Context="User"><Exec><Command>{exe}</Command><Arguments>--startup</Arguments><WorkingDirectory>{directory}</WorkingDirectory></Exec></Actions>
                    </Task>
                    """;
                object registered = folder.RegisterTask(TaskName, xml, 6, sid, null, 3, null);
                Release(registered);
            }
            return true;
        });
        // Replace the legacy entry only after task registration succeeds.
        if (File.Exists(Link)) File.Delete(Link);
    }
    static dynamic? GetTask(dynamic folder)
    {
        try { return folder.GetTask(TaskName); }
        catch (Exception e) when ((uint)e.HResult == 0x80070002) { return null; }
    }
    static T WithFolder<T>(Func<dynamic, T> action)
    {
        object service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        object? folder = null;
        try { dynamic scheduler = service; scheduler.Connect(); folder = scheduler.GetFolder("\\"); return action(folder); }
        finally { Release(folder); Release(service); }
    }
    static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
