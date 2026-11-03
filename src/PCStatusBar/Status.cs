namespace PCStatusBar;

public enum Severity { Unknown, Green, Yellow, Red }

public sealed class Thresholds
{
    public double Yellow { get; set; }
    public double Red { get; set; }
    public double Margin { get; set; }
    public double RiseSeconds { get; set; }
    public double RecoverSeconds { get; set; }
    public bool Inverse { get; set; }
    public bool Valid() => double.IsFinite(Yellow) && double.IsFinite(Red) &&
        double.IsFinite(Margin) && double.IsFinite(RiseSeconds) && double.IsFinite(RecoverSeconds) &&
        Yellow >= 0 && Yellow <= 100 && Red >= 0 && Red <= 100 && Margin >= 0 &&
        RiseSeconds >= 0 && RecoverSeconds >= 0 &&
        (Inverse ? Red < Yellow && Yellow + Margin <= 100 && Red + Margin < Yellow
                 : Yellow < Red && Yellow - Margin >= 0 && Red - Margin > Yellow);
}

// Time is supplied by Stopwatch, allowing deterministic boundary and continuity tests.
public sealed class StatusEvaluator(Thresholds thresholds, double staleSeconds)
{
    public Severity State { get; private set; }
    double? yellowSince, redSince, greenSince, belowRedSince, last;
    public void Reset() { State = Severity.Unknown; yellowSince = redSince = greenSince = belowRedSince = last = null; }
    static void Track(ref double? since, bool condition, double now) { since = condition ? since ?? now : null; }
    static bool Held(double? since, double now, double duration) => since.HasValue && now - since.Value >= duration;
    public Severity Update(double? value, double now)
    {
        if (!value.HasValue || !double.IsFinite(value.Value) || value < 0 || value > 100) { Reset(); return State; }
        if (last.HasValue && (now - last > staleSeconds || now < last)) Reset();
        last = now;
        double v = thresholds.Inverse ? -value.Value : value.Value;
        double y = thresholds.Inverse ? -thresholds.Yellow : thresholds.Yellow;
        double r = thresholds.Inverse ? -thresholds.Red : thresholds.Red;
        Track(ref yellowSince, v >= y, now);
        Track(ref redSince, v >= r, now);
        Track(ref greenSince, v <= y - thresholds.Margin, now);
        Track(ref belowRedSince, v <= r - thresholds.Margin, now);
        if (State == Severity.Unknown) State = Severity.Green;
        if (Held(redSince, now, thresholds.RiseSeconds)) State = Severity.Red;
        else if (State != Severity.Red && Held(yellowSince, now, thresholds.RiseSeconds)) State = Severity.Yellow;
        else if (State == Severity.Red && Held(belowRedSince, now, thresholds.RecoverSeconds))
            State = Held(greenSince, now, thresholds.RecoverSeconds) ? Severity.Green : Severity.Yellow;
        else if (State == Severity.Yellow && Held(greenSince, now, thresholds.RecoverSeconds)) State = Severity.Green;
        return State;
    }
}

public sealed class CpuDelta
{
    (ulong idle, ulong kernel, ulong user)? previous;
    public void Reset() => previous = null;
    public double? Sample(ulong idle, ulong kernel, ulong user)
    {
        var old = previous; previous = (idle, kernel, user);
        if (old is not { } p || idle < p.idle || kernel < p.kernel || user < p.user) return null;
        double total = (double)(kernel - p.kernel) + (user - p.user);
        double idleDelta = idle - p.idle;
        return total <= 0 || idleDelta > total ? null : 100 * (total - idleDelta) / total;
    }
}

public static class Units
{
    public static double GiB(ulong bytes) => bytes / 1073741824.0;
    public static double? Percent(ulong part, ulong total) => total == 0 || part > total ? null : 100.0 * part / total;
}
