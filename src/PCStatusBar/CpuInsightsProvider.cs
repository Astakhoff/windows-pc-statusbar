using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCStatusBar;

internal record CpuInsightsReading(int? Queue, ProcessUsage[] Processes, ProcessGroup[] Groups, bool HasBaseline, int Skipped, int LogicalProcessors, double At);

internal sealed class CpuInsightsProvider : IDisposable
{
    nint query, counter;
    bool initialized;
    double? previousAt;
    Dictionary<int, ProcessSample> previous = [];
    [StructLayout(LayoutKind.Explicit, Size = 16)] struct CounterValue
    { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public int Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(nint query);
    [DllImport("kernel32.dll")] static extern uint GetActiveProcessorCount(ushort group);
    public void Reset() { previous.Clear(); previousAt = null; }
    int? ReadQueue()
    {
        try
        {
            if (!initialized)
            {
                initialized = true;
                uint status = PdhOpenQuery(null, 0, out query);
                if (status == 0) status = PdhAddEnglishCounter(query, @"\System\Processor Queue Length", 0, out counter);
                if (status != 0) { Log.Write($"CPU queue initialization: 0x{status:X}"); return null; }
            }
            if (query == 0 || counter == 0) return null;
            uint result = PdhCollectQueryData(query);
            if (result != 0) { Log.Write($"CPU queue collection: 0x{result:X}"); return null; }
            result = PdhGetFormattedCounterValue(counter, 0x8100, out _, out var value); // LONG | NOCAP100: queue can exceed 100.
            if (result == 0 && value.Status <= 1 && value.Value >= 0) return value.Value;
            Log.Write($"CPU queue value: 0x{result:X}, status 0x{value.Status:X}");
        }
        catch (Exception e) { Log.Write("CPU queue: " + e.Message); }
        return null;
    }
    public CpuInsightsReading Read(double now)
    {
        int? queue = ReadQueue();
        int logical = (int)GetActiveProcessorCount(0xffff);
        if (logical <= 0) logical = Environment.ProcessorCount;
        var current = new Dictionary<int, ProcessSample>();
        int skipped = 0;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (process.Id == 0) continue;
                    try { current[process.Id] = new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.ProcessName, process.TotalProcessorTime.TotalSeconds); }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { skipped++; }
                }
            }
        }
        catch (Exception e) { Log.Write("CPU processes: " + e.Message); Reset(); return new(queue, [], [], false, skipped, logical, now); }
        bool baseline = previousAt.HasValue && now > previousAt && now - previousAt <= 10;
        var usages = baseline ? ProcessAccounting.Compare(previous, current.Values, now - previousAt!.Value, logical) : [];
        previous = current; previousAt = now;
        return new(queue, usages, ProcessAccounting.Group(usages), baseline, skipped, logical, now);
    }
    public void Dispose() { if (query != 0) { PdhCloseQuery(query); query = counter = 0; } }
}
