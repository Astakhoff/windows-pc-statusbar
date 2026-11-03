using System.Runtime.InteropServices;
using System.Text;

namespace PCStatusBar;

// Uses only the NVML DLL supplied by an existing NVIDIA driver in System32.
// No driver installation, privilege elevation, command processes, or sensor guessing.
internal sealed class Temperature : IDisposable
{
    bool initialized, attempted;
    nint device;
    string name = "NVIDIA GPU #0";
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] static extern int nvmlInit_v2();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] static extern int nvmlShutdown();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] static extern int nvmlDeviceGetTemperature(nint device, uint sensor, out uint value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int nvmlDeviceGetName(nint device, StringBuilder name, uint length);
    public Reading Read(double now)
    {
        try
        {
            if (!attempted)
            {
                attempted = true; initialized = nvmlInit_v2() == 0;
                if (initialized && nvmlDeviceGetHandleByIndex_v2(0, out device) == 0)
                {
                    var buffer = new StringBuilder(128);
                    if (nvmlDeviceGetName(device, buffer, 128) == 0) name = buffer.ToString();
                }
            }
            if (initialized && device != 0 && nvmlDeviceGetTemperature(device, 0, out uint value) == 0 && value is > 0 and < 125)
                return new(value, $"GPU  {value} °C", $"{name}, GPU core, NVML nvmlDeviceGetTemperature, датчик #0. CPU Package недоступен.", now);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { Log.Write("NVML недоступен: " + e.Message); }
        return new(null, "GPU  —", "Достоверный датчик GPU core недоступен. CPU Package недоступен. Драйверы не устанавливаются.", now);
    }
    public void Dispose() { if (initialized) { nvmlShutdown(); initialized = false; } }
}
