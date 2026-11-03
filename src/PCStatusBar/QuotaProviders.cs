using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;

namespace PCStatusBar;

internal interface IQuotaProvider
{
    string Name { get; }
    Task<QuotaSnapshot> ReadAsync(CancellationToken cancellation);
}

internal sealed class CodexQuotaProvider : IQuotaProvider
{
    public string Name => "Кодекс";
    static string? FindExecutable()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root).Select(d => Path.Combine(d, "codex.exe")).Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }
    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellation)
    {
        Process? process = null;
        const string source = "Codex app-server: account/rateLimits/read";
        try
        {
            string? path = FindExecutable();
            if (path == null) return QuotaSnapshot.Missing(Name, source, "Не найден Codex desktop CLI", DateTimeOffset.UtcNow);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var token = deadline.Token;
            process = new Process { StartInfo = new ProcessStartInfo(path)
            {
                Arguments = "app-server --stdio", UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory
            } };
            process.Start();
            // Drain and discard diagnostics; do not log server output or any account credentials.
            _ = process.StandardError.ReadToEndAsync(token);
            async Task Send(object value) { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), token); await process.StandardInput.FlushAsync(token); }
            async Task<string> Receive(int id)
            {
                while (true)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(token);
                    if (line == null) throw new IOException("Codex закрыл соединение");
                    using var doc = JsonDocument.Parse(line);
                    if (!doc.RootElement.TryGetProperty("id", out var messageId) || messageId.ValueKind != JsonValueKind.Number || !messageId.TryGetInt32(out int number) || number != id) continue;
                    if (doc.RootElement.TryGetProperty("error", out _)) throw new IOException("Codex не предоставил лимиты текущего аккаунта");
                    return line;
                }
            }
            await Send(new { id = 1, method = "initialize", @params = new { clientInfo = new { name = "pc_status_bar", version = "1.2.0" } } });
            await Receive(1);
            await Send(new { method = "initialized", @params = new { } });
            await Send(new { id = 2, method = "account/rateLimits/read", @params = new { } });
            string json = await Receive(2);
            return QuotaParsing.Codex(json, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) { return QuotaSnapshot.Missing(Name, source, "Тайм-аут чтения лимитов Codex", DateTimeOffset.UtcNow); }
        catch (Exception) { return QuotaSnapshot.Missing(Name, source, "Не удалось прочитать лимиты Codex; проверьте вход в приложение", DateTimeOffset.UtcNow); }
        finally
        {
            if (process != null)
            {
                try { if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(500)) process.Kill(entireProcessTree: true); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
        }
    }
}

internal sealed class ClaudeQuotaProvider : IQuotaProvider
{
    public string Name => "Клод";
    Task<string[]>? pendingRead;
    string[] previousBadges = [];
    QuotaSnapshot? previous;
    static string[] ReadBadges()
    {
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            using (process)
            {
                try
                {
                    nint hwnd = process.MainWindowHandle;
                    if (hwnd == 0) continue;
                    var root = AutomationElement.FromHandle(hwnd);
                    var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                    var badges = new List<string>();
                    foreach (AutomationElement button in buttons)
                    {
                        string name = button.Current.Name;
                        if (name.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase)) badges.Add(name);
                    }
                    if (badges.Count > 0) return badges.ToArray();
                }
                catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException) { }
            }
        }
        return [];
    }
    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellation)
    {
        const string source = "Индикатор Usage в Claude desktop";
        try
        {
            // If an accessibility provider is unresponsive, keep at most one pending read.
            if (pendingRead == null || pendingRead.IsCompleted) pendingRead = Task.Run(ReadBadges);
            var badges = await pendingRead.WaitAsync(TimeSpan.FromSeconds(12), cancellation);
            if (badges.Length == 0) return QuotaSnapshot.Missing(Name, source, "Не найден индикатор Usage. Откройте Claude и его вкладку Code", DateTimeOffset.UtcNow);
            var snapshot = QuotaParsing.ClaudeBadge(badges, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            if (badges.SequenceEqual(previousBadges) && previous != null)
                snapshot = snapshot with { Windows = snapshot.Windows.Select(w => previous.Windows.FirstOrDefault(old => old.Kind == w.Kind) is { ResetsAt: { } reset } && reset <= DateTimeOffset.UtcNow ? new QuotaWindow(w.Kind, null, reset, "Индикатор Claude ещё не обновился после сброса") : w).ToArray() };
            previousBadges = badges; previous = snapshot;
            return snapshot;
        }
        catch (Exception) { return QuotaSnapshot.Missing(Name, source, "Индикатор лимитов Claude недоступен", DateTimeOffset.UtcNow); }
    }
}
