using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCStatusBar;

public record QuotaWindow(string Kind, double? UsedPercent, DateTimeOffset? ResetsAt, string? Reason = null);
public record QuotaSnapshot(string Provider, string Source, DateTimeOffset ObservedAt, QuotaWindow[] Windows)
{
    public static QuotaSnapshot Missing(string provider, string source, string reason, DateTimeOffset now) =>
        new(provider, source, now, [new("week", null, null, reason), new("fiveHour", null, null, reason)]);
}

public static class QuotaParsing
{
    static double? Number(JsonElement element, string key) => element.TryGetProperty(key, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetDouble(out double v) && double.IsFinite(v) && v is >= 0 and <= 100 ? v : null;
    static DateTimeOffset? Reset(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var n) || n.ValueKind != JsonValueKind.Number || !n.TryGetInt64(out long seconds)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { return null; }
    }
    public static QuotaSnapshot Codex(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("result", out var result)) root = result;
        JsonElement limits = default;
        if (root.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object && byId.TryGetProperty("codex", out var codex)) limits = codex;
        else if (root.TryGetProperty("rateLimits", out var rate)) limits = rate;
        var windows = new Dictionary<string, QuotaWindow>();
        if (limits.ValueKind == JsonValueKind.Object)
            foreach (string key in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object ||
                    !window.TryGetProperty("windowDurationMins", out var duration) || !duration.TryGetInt32(out int minutes)) continue;
                string? kind = minutes == 10080 ? "week" : minutes == 300 ? "fiveHour" : null;
                if (kind != null) windows[kind] = new(kind, Number(window, "usedPercent"), Reset(window, "resetsAt"));
            }
        return new("Codex", "Codex app-server: account/rateLimits/read", now,
            new[] { "week", "fiveHour" }.Select(k => windows.GetValueOrDefault(k) ?? new(k, null, null, "Сервис не передал это окно лимита")).ToArray());
    }
    public static QuotaSnapshot ClaudeBadge(IEnumerable<string> badges, DateTimeOffset now, TimeZoneInfo zone)
    {
        var windows = new Dictionary<string, QuotaWindow>();
        foreach (string badge in badges)
            foreach (Match match in Regex.Matches(badge, @"(?<window>Weekly\s*[·:]\s*all models|5[- ]hour|5h|Current session)\s*:\s*(?<used>\d+(?:[.,]\d+)?)%\s*,\s*Resets\s*(?<reset>.+?)(?=\s*[;|]\s*|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string kind = match.Groups["window"].Value.StartsWith("Weekly", StringComparison.OrdinalIgnoreCase) ? "week" : "fiveHour";
                if (!double.TryParse(match.Groups["used"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double used) || used is < 0 or > 100) continue;
                windows[kind] = new(kind, used, ClaudeReset(match.Groups["reset"].Value, now, zone));
            }
        return new("Claude", "Индикатор Usage в приложении Claude (чтение доступности Windows, без действий)", now,
            new[] { "week", "fiveHour" }.Select(k => windows.GetValueOrDefault(k) ?? new(k, null, null, "Приложение Claude не показывает это окно в индикаторе Usage")).ToArray());
    }
    public static DateTimeOffset? ClaudeReset(string text, DateTimeOffset now, TimeZoneInfo zone)
    {
        // The native Claude Usage badge displays weekday and local clock time, e.g. Fri 8:00 PM.
        var match = Regex.Match(text.Trim(), @"^(?<day>Mon|Tue|Wed|Thu|Fri|Sat|Sun)\s+(?<hour>\d{1,2}):(?<minute>\d{2})\s*(?<ampm>AM|PM)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        string[] days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        int day = Array.FindIndex(days, d => d.Equals(match.Groups["day"].Value, StringComparison.OrdinalIgnoreCase));
        int hour = int.Parse(match.Groups["hour"].Value), minute = int.Parse(match.Groups["minute"].Value);
        string ampm = match.Groups["ampm"].Value;
        if (minute > 59 || hour > 23 || (ampm.Length > 0 && hour is < 1 or > 12)) return null;
        if (ampm.Length > 0) hour = hour % 12 + (ampm.Equals("PM", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        int delta = (day - (int)localNow.DayOfWeek + 7) % 7;
        var localReset = DateTime.SpecifyKind(localNow.Date.AddDays(delta).AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
        if (new DateTimeOffset(localReset, zone.GetUtcOffset(localReset)) <= now) localReset = localReset.AddDays(7);
        if (zone.IsInvalidTime(localReset)) return null;
        return new(localReset, zone.GetUtcOffset(localReset));
    }
}

public record QuotaTagGroup(string Provider, string Period, string Value, string? Reset, string State);
public record QuotaVisibility(bool ClaudeFiveHour = true, bool ClaudeWeek = true, bool CodexFiveHour = false, bool CodexWeek = true)
{
    public bool AnyEnabled() => ClaudeFiveHour || ClaudeWeek || CodexFiveHour || CodexWeek;
    public bool Includes(string provider, string kind) => (QuotaText.ProviderName(provider), kind) switch
    {
        ("Claude", "fiveHour") => ClaudeFiveHour, ("Claude", "week") => ClaudeWeek,
        ("Codex", "fiveHour") => CodexFiveHour, ("Codex", "week") => CodexWeek,
        _ => true
    };
}

public static class QuotaText
{
    public static string ProviderName(string name) => name switch { "Кодекс" => "Codex", "Клод" => "Claude", _ => name };
    public static IEnumerable<QuotaWindow> VisibleWindows(QuotaSnapshot snapshot, QuotaVisibility? visibility = null) => snapshot.Windows.Where(w => (visibility ?? new()).Includes(snapshot.Provider, w.Kind));
    public static string Countdown(DateTimeOffset reset, DateTimeOffset now)
    {
        if (reset <= now) return "ожидаем обновления после сброса";
        long minutes = (long)Math.Ceiling((reset - now).TotalMinutes);
        long days = minutes / 1440, hours = minutes % 1440 / 60, mins = minutes % 60;
        return days > 0 ? $"{days} д {hours} ч" : hours > 0 ? $"{hours} ч {mins} мин" : $"{mins} мин";
    }
    public static string Window(QuotaSnapshot snapshot, QuotaWindow window, DateTimeOffset now)
    {
        string label = $"{ProviderName(snapshot.Provider)}: {(window.Kind == "week" ? "week" : "5 часов")}";
        if (now - snapshot.ObservedAt > TimeSpan.FromMinutes(3)) return label + " — данные устарели";
        if (window.ResetsAt <= now) return label + " — ожидаем обновления после сброса";
        if (!window.UsedPercent.HasValue) return label + " — None";
        string remaining = (100 - window.UsedPercent.Value).ToString("0.#", CultureInfo.CurrentCulture);
        return $"{label} — осталось {remaining}% · " + (window.ResetsAt.HasValue ? "до сброса " + Countdown(window.ResetsAt.Value, now) : "время сброса недоступно");
    }
    public static QuotaTagGroup[] Tags(IEnumerable<QuotaSnapshot> snapshots, DateTimeOffset now, QuotaVisibility? visibility = null) => snapshots.SelectMany(snapshot => VisibleWindows(snapshot, visibility).Select(window =>
    {
        bool available = window.UsedPercent.HasValue && now - snapshot.ObservedAt <= TimeSpan.FromMinutes(3) && !(window.ResetsAt.HasValue && window.ResetsAt <= now);
        double remaining = 100 - (window.UsedPercent ?? 0);
        string value = available ? remaining.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "None";
        string? reset = available ? (window.ResetsAt.HasValue ? Countdown(window.ResetsAt.Value, now) : "None") : null;
        string state = !available ? "unknown" : remaining <= 10 ? "low" : remaining <= 25 ? "attention" : "available";
        return new QuotaTagGroup(ProviderName(snapshot.Provider), window.Kind == "week" ? "week" : "5 часов", value, reset, state);
    })).ToArray();
    public static string Ticker(IEnumerable<QuotaSnapshot> snapshots, DateTimeOffset now, QuotaVisibility? visibility = null) => string.Join("   ·   ", Tags(snapshots, now, visibility).Select(g => $"[{g.Provider}] [{g.Period}] [{g.Value}]" + (g.Reset == null ? "" : $" [{g.Reset}]")));
}
