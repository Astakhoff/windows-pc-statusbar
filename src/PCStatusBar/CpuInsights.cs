namespace PCStatusBar;

public record ProcessSample(int Id, long Started, string Name, double CpuSeconds);
public record ProcessUsage(int Id, string Name, double CpuPercent);
public record ProcessGroup(string Name, double CpuPercent, int Count);

public static class ProcessAccounting
{
    // CPU is normalized to the entire Windows machine, not one logical processor.
    public static ProcessUsage[] Compare(IReadOnlyDictionary<int, ProcessSample> previous,
        IEnumerable<ProcessSample> current, double elapsedSeconds, int logicalProcessors)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 || logicalProcessors <= 0) return [];
        return current.Where(p => p.Id != 0 && previous.TryGetValue(p.Id, out var old) && old.Started == p.Started &&
                double.IsFinite(p.CpuSeconds) && double.IsFinite(old.CpuSeconds) && p.CpuSeconds >= old.CpuSeconds)
            .Select(p => new ProcessUsage(p.Id, p.Name,
                Math.Clamp(100 * (p.CpuSeconds - previous[p.Id].CpuSeconds) / elapsedSeconds / logicalProcessors, 0, 100)))
            .OrderByDescending(p => p.CpuPercent).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id).ToArray();
    }
    public static ProcessGroup[] Group(IEnumerable<ProcessUsage> processes) => processes
        .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .Select(g => new ProcessGroup(g.First().Name, g.Sum(p => p.CpuPercent), g.Count()))
        .OrderByDescending(g => g.CpuPercent).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}

public record CpuPoint(double At, double? Value);
public sealed class CpuHistory
{
    readonly Queue<CpuPoint> samples = new();
    public void Clear() => samples.Clear();
    public void Add(double now, double? value)
    {
        if (!double.IsFinite(now)) return;
        if (samples.Count > 0 && now <= samples.Last().At) Clear();
        if (value.HasValue && (!double.IsFinite(value.Value) || value < 0 || value > 100)) value = null;
        samples.Enqueue(new(now, value));
        while (samples.Count > 0 && samples.Peek().At < now - 60) samples.Dequeue();
        // Bound memory even if a caller supplies excessive sample frequency.
        while (samples.Count > 600) samples.Dequeue();
    }
    public CpuPoint[] Window(double now) => samples.Where(p => p.At >= now - 60 && p.At <= now).ToArray();
    public static bool CanConnect(CpuPoint left, CpuPoint right, double maximumGap) =>
        left.Value.HasValue && right.Value.HasValue && right.At > left.At && right.At - left.At <= maximumGap;
}
