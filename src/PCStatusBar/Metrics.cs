using System.Runtime.InteropServices;

namespace PCStatusBar;

internal record Reading(double? Percent, string Text, string Detail, double At);
internal sealed class Metrics
{
    readonly CpuDelta cpu = new();
    public void Reset() => cpu.Reset();
    public Reading Cpu(double now)
    {
        if (Native.GetActiveProcessorGroupCount() > 1) return new(null, "CPU  —", "Больше одной processor group: общий CPU этим провайдером не поддерживается", now);
        bool success = Native.GetSystemTimes(out var i, out var k, out var u);
        if (!success) cpu.Reset();
        double? value = success ? cpu.Sample(i, k, u) : null;
        return new(value, value.HasValue ? $"CPU  {value:0}%" : "CPU  —", "Общий CPU Windows: GetSystemTimes, разность busy / total за интервал; kernel включает idle. " + (value.HasValue ? $"Точно: {value:0.00}%" : "Нет отсчёта; ожидается новая база"), now);
    }
    public Reading Ram(double now)
    {
        var memory = new Native.Memory { Length = (uint)Marshal.SizeOf<Native.Memory>() };
        if (!Native.GlobalMemoryStatusEx(ref memory)) return new(null, "RAM  —", "GlobalMemoryStatusEx: ошибка чтения", now);
        ulong used = memory.TotalPhysical - memory.AvailablePhysical;
        var percent = Units.Percent(used, memory.TotalPhysical);
        return new(percent, $"RAM  {percent:0}%   {Units.GiB(used):0.0} / {Units.GiB(memory.TotalPhysical):0.0} GiB", $"Физическая память Windows, GlobalMemoryStatusEx; занято = всего − доступно; {percent:0.00}%", now);
    }
    public Reading Disk(double now)
    {
        if (!Native.GetDiskFreeSpaceEx(@"C:\", out var available, out var total, out _)) return new(null, "C: свободно  —", "GetDiskFreeSpaceExW: C:\\ недоступен", now);
        var percent = Units.Percent(available, total);
        return new(percent, $"C: свободно  {Units.GiB(available):0.0} GiB   {percent:0}%", $"C:\\, доступно текущему пользователю {Units.GiB(available):0.00} из {Units.GiB(total):0.00} GiB (с учётом квот). GetDiskFreeSpaceExW; {percent:0.00}%", now);
    }
}
