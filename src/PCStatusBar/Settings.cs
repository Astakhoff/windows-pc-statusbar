using System.Text.Json;

namespace PCStatusBar;

internal sealed class Settings
{
    public string Monitor { get; set; } = "";
    public int Height { get; set; } = 38;
    public bool TaskbarOverlay { get; set; }
    public bool OverlayAdaptive { get; set; } = true;
    public int OverlayReferenceWidth { get; set; }
    public int OverlayLeft { get; set; } = 560;
    public int OverlayRight { get; set; } = 240;
    public int PollSeconds { get; set; } = 1;
    public int DiskSeconds { get; set; } = 10;
    public bool ShowTemperature { get; set; } = true;
    public bool ShowLeaders { get; set; } = true;
    public bool ShowQueue { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGraph { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowDiskC { get; set; } = true;
    public bool ShowDiskD { get; set; } = true;
    public bool ShowQuotas { get; set; } = true;
    public QuotaVisibility QuotaVisibility { get; set; } = new();
    public void CopyVisibilityFrom(Settings other)
    {
        ShowLeaders = other.ShowLeaders; ShowQueue = other.ShowQueue; ShowCpu = other.ShowCpu;
        ShowGraph = other.ShowGraph; ShowRam = other.ShowRam; ShowDiskC = other.ShowDiskC;
        ShowDiskD = other.ShowDiskD; ShowTemperature = other.ShowTemperature; ShowQuotas = other.ShowQuotas;
        QuotaVisibility = other.QuotaVisibility;
    }
    public double? GpuYellow { get; set; }
    public double? GpuRed { get; set; }
    public Thresholds Cpu { get; set; } = new() { Yellow = 70, Red = 90, Margin = 5, RiseSeconds = 5, RecoverSeconds = 5 };
    public Thresholds Ram { get; set; } = new() { Yellow = 80, Red = 90, Margin = 3, RiseSeconds = 3, RecoverSeconds = 5 };
    public Thresholds Disk { get; set; } = new() { Yellow = 15, Red = 5, Margin = 2, Inverse = true };
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCStatusBar");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public bool Valid() => Monitor != null && Height is >= 32 and <= 60 && OverlayReferenceWidth is >= 0 and <= 30000 && OverlayLeft is >= 0 and <= 20000 && OverlayRight is >= 0 and <= 20000 && PollSeconds is >= 1 and <= 10 && DiskSeconds is >= 2 and <= 120 &&
        QuotaVisibility is not null && Cpu is not null && Ram is not null && Disk is not null && Cpu.Valid() && Ram.Valid() && Disk.Valid() && !Cpu.Inverse && !Ram.Inverse && Disk.Inverse &&
        ((!GpuYellow.HasValue && !GpuRed.HasValue) || (GpuYellow > 0 && GpuRed > GpuYellow && GpuRed < 125));
    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
            if (s?.Valid() == true) return s;
            throw new InvalidDataException("Некорректные настройки");
        }
        catch (Exception e) { Log.Write(e.Message); return new(); }
    }
    public void Save()
    {
        if (!Valid()) throw new InvalidDataException("Проверьте порядок порогов и границы восстановления");
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}

internal static class Log
{
    static string? previous;
    static DateTime at;
    public static void Write(string message)
    {
        try
        {
            if (message == previous && DateTime.UtcNow - at < TimeSpan.FromMinutes(1)) return;
            previous = message; at = DateTime.UtcNow;
            Directory.CreateDirectory(Settings.Folder);
            string file = Path.Combine(Settings.Folder, "events.log");
            if (File.Exists(file) && new FileInfo(file).Length > 131072) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{at:O} {message}{Environment.NewLine}");
        }
        catch { /* Diagnostics must never interrupt monitoring. */ }
    }
}

internal static class Startup
{
    public static string Link => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "PCStatusBar.lnk");
    public static bool Enabled => File.Exists(Link);
    public static void Set(bool enabled)
    {
        if (!enabled) { if (File.Exists(Link)) File.Delete(Link); return; }
        Type type = Type.GetTypeFromProgID("WScript.Shell")!;
        object shell = Activator.CreateInstance(type)!;
        object? shortcut = null;
        try
        {
            dynamic s = shell;
            shortcut = s.CreateShortcut(Link);
            dynamic link = shortcut;
            link.TargetPath = Environment.ProcessPath!;
            link.WorkingDirectory = AppContext.BaseDirectory;
            link.Save();
            ShortcutIdentity.Set(Link);
        }
        finally
        {
            if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
