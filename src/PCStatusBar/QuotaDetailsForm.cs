namespace PCStatusBar;

internal sealed class QuotaDetailsForm : Form
{
    readonly Font detailsFont = new("Segoe UI", 10F);
    readonly TextBox content = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.None };
    public event EventHandler? RefreshRequested;
    public QuotaDetailsForm()
    {
        Text = "PCStatusBar — лимиты Codex и Claude"; Icon = AppIcon.Image; Size = new Size(800, 560); MinimumSize = new Size(600, 400);
        Font = detailsFont;
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var refresh = new Button { Text = "Обновить", Dock = DockStyle.Bottom, Height = 36 };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(content); Controls.Add(refresh); Padding = new Padding(16);
        Shown += (_, _) => { content.Select(0, 0); refresh.Focus(); };
    }
    public void UpdateSnapshots(QuotaSnapshot[] snapshots, DateTimeOffset now, QuotaVisibility visibility)
    {
        var lines = new List<string> { "Остатки лимитов аккаунтов. Автообновление раз в минуту.", "Проценты показывают остаток лимита; количество токенов не вычисляется.", "" };
        foreach (var snapshot in snapshots)
        {
            lines.Add(QuotaText.ProviderName(snapshot.Provider) + ":");
            foreach (var window in QuotaText.VisibleWindows(snapshot, visibility))
            {
                string prefix = QuotaText.ProviderName(snapshot.Provider) + ": ";
                string windowText = QuotaText.Window(snapshot, window, now);
                lines.Add(windowText.StartsWith(prefix, StringComparison.Ordinal) ? windowText[prefix.Length..] : windowText);
                if (window.ResetsAt.HasValue) lines.Add("  Дата сброса: " + window.ResetsAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm zzz"));
                if (!string.IsNullOrEmpty(window.Reason)) lines.Add("  " + window.Reason);
            }
            lines.Add("Источник: " + snapshot.Source);
            lines.Add("Прочитано: " + snapshot.ObservedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"));
            lines.Add("");
        }
        lines.Add("Наведение на бегущую строку приостанавливает её. Недоступное окно не считается полным или неограниченным.");
        string text = string.Join(Environment.NewLine, lines);
        if (content.Text != text) content.Text = text;
    }
    protected override void Dispose(bool disposing) { if (disposing) detailsFont.Dispose(); base.Dispose(disposing); }
}
