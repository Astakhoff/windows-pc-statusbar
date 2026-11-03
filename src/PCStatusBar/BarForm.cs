using System.Diagnostics;
using Microsoft.Win32;

namespace PCStatusBar;

internal sealed class BarForm : Form
{
    const uint Callback = 0x8001;
    readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Metrics metrics = new();
    readonly CpuInsightsProvider insightsProvider = new();
    readonly CpuHistory cpuHistory = new();
    readonly CpuGraph cpuGraph = new();
    readonly Label queueBlock = new() { Text = "Очередь  —", AccessibleName = "Очередь CPU" };
    readonly Label leadersBlock = new() { Text = "Лидеры CPU  —", AccessibleName = "Основные потребители CPU" };
    readonly Temperature temperature = new();
    readonly NotifyIcon tray;
    readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    readonly ContextMenuStrip menu = new();
    readonly Label[] blocks = new Label[4];
    readonly FlowLayoutPanel flow = new() { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = false, Padding = new Padding(0) };
    Settings settings = Settings.Load();
    StatusEvaluator[] evaluators = [];
    Reading?[] readings = new Reading?[3];
    bool registered, positioning, busy, closing, positionQueued, resetPending;
    double lastInsights = -100;
    CpuInsightsReading? insights;
    ConsumersForm? consumers;
    double lastPoll = -100, lastDisk = -100;
    double lastTemperature = -100;
    Reading? gpu;
    SettingsForm? dialog;
    nint? inspectionStyle;
    Rectangle? maximizedBounds;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }

    public BarForm()
    {
        Text = Program.WindowTitle; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(23, 26, 32); ForeColor = Color.White; DoubleBuffered = true;
        Font = new Font("Segoe UI", 10F);
        Controls.Add(flow);
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new Label { Text = new[] { "CPU  —", "RAM  —", "C: свободно  —", "GPU  —" }[i], TextAlign = ContentAlignment.MiddleLeft, AutoSize = false, AutoEllipsis = true, Margin = new Padding(4, 3, 4, 3), Padding = new Padding(10, 0, 4, 0), BackColor = Color.FromArgb(32, 36, 43), AccessibleName = new[] { "CPU", "RAM", "C: свободно", "Температуры" }[i] };
            flow.Controls.Add(blocks[i]);
            blocks[i].Paint += (sender, e) => { var label = (Label)sender!; using var brush = new SolidBrush(label.ForeColor); e.Graphics.FillRectangle(brush, 0, 4, 3, Math.Max(1, label.Height - 8)); };
            blocks[i].DoubleClick += (_, _) => OpenSettings();
        }
        foreach (var block in new[] { queueBlock, leadersBlock })
        {
            block.TextAlign = ContentAlignment.MiddleLeft; block.AutoEllipsis = true;
            block.Padding = new Padding(10, 0, 4, 0); block.BackColor = Color.FromArgb(32, 36, 43); block.ForeColor = Color.Silver;
        }
        leadersBlock.Cursor = Cursors.Hand;
        leadersBlock.Click += (_, _) => OpenConsumers();
        flow.Controls.Clear();
        flow.Controls.AddRange([blocks[0], cpuGraph, queueBlock, leadersBlock, blocks[1], blocks[2], blocks[3]]);
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        menu.Items.Add("Потребители CPU…", null, (_, _) => OpenConsumers());
        menu.Items.Add("Показать / скрыть панель", null, (_, _) => SetVisible(!Visible));
        var startup = new ToolStripMenuItem("Автозапуск") { CheckOnClick = true };
        startup.Click += (_, _) => { try { Startup.Set(startup.Checked); } catch (Exception e) { MessageBox.Show(e.Message); } };
        menu.Items.Add(startup);
        menu.Opening += (_, _) => startup.Checked = Startup.Enabled;
        menu.Items.Add("Открыть Диспетчер задач", null, (_, _) => Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }));
        menu.Items.Add("Ярлык в меню «Пуск»…", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", Environment.GetFolderPath(Environment.SpecialFolder.Programs)) { UseShellExecute = true }));
        menu.Items.Add("Выход", null, (_, _) => Close());
        ContextMenuStrip = menu; flow.ContextMenuStrip = menu;
        foreach (var block in blocks) block.ContextMenuStrip = menu;
        queueBlock.ContextMenuStrip = leadersBlock.ContextMenuStrip = cpuGraph.ContextMenuStrip = menu;
        tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "PCStatusBar — CPU / RAM / C:", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => OpenSettings();
        ResetRules(); LayoutBlocks();
        timer.Tick += async (_, _) => await TickAsync();
        Shown += async (_, _) => { Register(); timer.Start(); await TickAsync(); };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
    }
    void ResetRules()
    {
        if (busy) { resetPending = true; return; }
        resetPending = false;
        evaluators = [new(settings.Cpu, Math.Max(5, 3 * settings.PollSeconds)), new(settings.Ram, Math.Max(5, 3 * settings.PollSeconds)), new(settings.Disk, Math.Max(5, 3 * settings.DiskSeconds))];
        lastPoll = lastDisk = -100; metrics.Reset(); readings = new Reading?[3];
        cpuHistory.Clear(); insightsProvider.Reset(); insights = null; lastInsights = -100;
    }
    void LayoutBlocks()
    {
        float scale = DeviceDpi / 96F;
        Control[] layout = [blocks[0], cpuGraph, queueBlock, leadersBlock, blocks[1], blocks[2], blocks[3]];
        int available = Screen.FromHandle(Handle).Bounds.Width;
        int gap = Math.Max(2, (int)(3 * scale)), endPadding = (int)(8 * scale);
        var visible = layout.Where(c => c != blocks[3] || settings.ShowTemperature).ToArray();
        var widths = visible.Select(control => control is Label label
            ? label.GetPreferredSize(new Size(int.MaxValue, int.MaxValue)).Width + (int)Math.Ceiling(4 * scale)
            : (int)(144 * scale)).ToArray();
        int budget = Math.Max(1, available - visible.Length * gap * 2 - endPadding);
        // Normal layout follows the actual text. Constrain only when the entire row cannot fit the monitor.
        int excess = widths.Sum() - budget;
        int leadersIndex = Array.IndexOf(visible, leadersBlock);
        if (excess > 0 && leadersIndex >= 0)
        {
            int reduction = Math.Min(excess, Math.Max(0, widths[leadersIndex] - (int)(100 * scale)));
            widths[leadersIndex] -= reduction;
        }
        float fit = Math.Min(1, budget / (float)Math.Max(1, widths.Sum()));
        SuspendLayout(); flow.SuspendLayout();
        try
        {
            blocks[3].Visible = settings.ShowTemperature;
            int total = endPadding;
            for (int i = 0; i < visible.Length; i++)
            {
                var control = visible[i];
                control.Margin = new Padding(gap, 3, gap, 3);
                control.Size = new Size(Math.Max(1, (int)(widths[i] * fit)), Math.Max(24, (int)(settings.Height * scale) - 6));
                total += control.Width + control.Margin.Horizontal;
            }
            flow.Width = Math.Min(available, total);
        }
        finally { flow.ResumeLayout(true); ResumeLayout(true); }
    }
    void Register()
    {
        if (registered || !Visible) return;
        var data = Native.AppBarData.For(Handle); data.Callback = Callback;
        registered = Native.SHAppBarMessage(0, ref data) != 0;
        if (!registered) { Log.Write("AppBar registration failed"); return; }
        PositionBar();
    }
    void Unregister()
    {
        if (!registered) return;
        registered = false;
        var data = Native.AppBarData.For(Handle); Native.SHAppBarMessage(1, ref data);
    }
    void QueuePosition()
    {
        if (positionQueued || closing || !IsHandleCreated) return;
        positionQueued = true;
        BeginInvoke(() => { positionQueued = false; if (!closing) PositionBar(); });
    }
    void PositionBar()
    {
        if (!registered || positioning || !Visible) return;
        positioning = true;
        try
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Screen.PrimaryScreen!;
            int h = (int)Math.Round(settings.Height * DeviceDpi / 96.0);
            var b = screen.Bounds;
            var data = Native.AppBarData.For(Handle); data.Edge = 3;
            data.Rect = new Native.Rect { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
            Native.SHAppBarMessage(2, ref data);
            data.Rect.Top = data.Rect.Bottom - h;
            Native.SHAppBarMessage(3, ref data);
            var r = data.Rect;
            Native.SetWindowPos(Handle, -1, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x10);
            LayoutBlocks();
        }
        finally { positioning = false; }
    }
    async Task TickAsync()
    {
        if (busy || closing) return;
        busy = true;
        try
        {
            double now = clock.Elapsed.TotalSeconds;
            bool poll = now - lastPoll >= settings.PollSeconds, disk = now - lastDisk >= settings.DiskSeconds;
            if (poll || disk)
            {
                var result = await Task.Run(() => (cpu: poll ? Safe(() => metrics.Cpu(now), "CPU", now) : null,
                    ram: poll ? Safe(() => metrics.Ram(now), "RAM", now) : null,
                    disk: disk ? Safe(() => metrics.Disk(now), "C: свободно", now) : null));
                if (closing) return;
                if (poll) { readings[0] = result.cpu; readings[1] = result.ram; lastPoll = now; cpuHistory.Add(now, result.cpu?.Percent); }
                if (disk) { readings[2] = result.disk; lastDisk = now; }
            }
            now = clock.Elapsed.TotalSeconds;
            if (now - lastInsights >= 3)
            {
                insights = await Task.Run(() => insightsProvider.Read(now)); lastInsights = now;
                if (closing) return;
            }
            now = clock.Elapsed.TotalSeconds;
            bool freshInsights = insights != null && now - insights.At <= 9;
            queueBlock.Text = freshInsights && insights!.Queue.HasValue ? $"Очередь  {insights.Queue}" : "Очередь  —";
            tips.SetToolTip(queueBlock, $"Число готовых потоков, ожидающих CPU; это не число агентов.\nSystem\\Processor Queue Length (PDH), мгновенный отсчёт раз в 3 секунды.\nЛогических процессоров: {insights?.LogicalProcessors}. Оценивайте очередь вместе с загрузкой CPU; универсальные цветовые пороги не назначены." + (!freshInsights || insights?.Queue == null ? "\nНет свежего достоверного значения." : ""));
            bool readyProcesses = freshInsights && insights!.HasBaseline;
            leadersBlock.Text = readyProcesses && insights!.Groups.Length > 0 ? "Лидеры:  " + string.Join("   ·   ", insights.Groups.Take(3).Select(g => $"{g.Name} {g.CpuPercent:0.0}%")) : "Лидеры CPU  —";
            tips.SetToolTip(leadersBlock, (readyProcesses ? string.Join("\n", insights!.Groups.Take(5).Select(g => $"{g.Name}: {g.CpuPercent:0.00}%, процессов {g.Count}")) : "Ожидание свежего интервала измерения") +
                $"\nПроценты от всей мощности Windows, группы по имени программы. Интервал 3 с.\nНедоступно / завершилось при чтении: {insights?.Skipped}. Нажмите, чтобы увидеть отдельные PID.");
            consumers?.UpdateReading(insights, freshInsights);
            if (settings.ShowTemperature && now - lastTemperature >= 2)
            {
                gpu = await Task.Run(() => temperature.Read(now)); lastTemperature = now;
                if (closing) return;
            }
            if (settings.ShowTemperature)
            {
                bool fresh = gpu != null && now - gpu.At <= 6 && gpu.Percent.HasValue;
                blocks[3].Text = fresh ? gpu!.Text : "GPU  —";
                blocks[3].ForeColor = !fresh || !settings.GpuYellow.HasValue ? Color.Silver :
                    gpu!.Percent >= settings.GpuRed ? Color.FromArgb(255, 119, 125) :
                    gpu.Percent >= settings.GpuYellow ? Color.FromArgb(250, 209, 94) : Color.FromArgb(115, 214, 158);
                tips.SetToolTip(blocks[3], gpu?.Detail + (settings.GpuYellow.HasValue ? $"\nВаши пороги внимания: {settings.GpuYellow} / {settings.GpuRed} °C." : "\nПороги не настроены: число показано нейтрально."));
            }
            for (int i = 0; i < 3; i++)
            {
                var reading = readings[i];
                double stale = Math.Max(5, 3 * (i == 2 ? settings.DiskSeconds : settings.PollSeconds));
                double? value = reading != null && now - reading.At <= stale ? reading.Percent : null;
                var state = evaluators[i].Update(value, now);
                blocks[i].Text = (reading?.Text ?? new[] { "CPU  —", "RAM  —", "C: свободно  —" }[i]) + (reading != null && now - reading.At > stale ? " (устарело)" : "");
                blocks[i].ForeColor = state switch { Severity.Green => Color.FromArgb(115, 214, 158), Severity.Yellow => Color.FromArgb(250, 209, 94), Severity.Red => Color.FromArgb(255, 119, 125), _ => Color.Silver };
                var rule = new[] { settings.Cpu, settings.Ram, settings.Disk }[i];
                string status = state switch { Severity.Green => "нормальный уровень", Severity.Yellow => "внимание", Severity.Red => "высокий уровень внимания", _ => "нет достоверных свежих данных" };
                tips.SetToolTip(blocks[i], $"{reading?.Detail}\nСостояние: {status}.\nЖёлтый {rule.Yellow}%, красный {rule.Red}%; ухудшение {rule.RiseSeconds} с, восстановление {rule.RecoverSeconds} с, запас {rule.Margin} п.п. Цвет учитывает устойчивое превышение и восстановление.");
            }
            cpuGraph.LineColor = blocks[0].ForeColor;
            cpuGraph.UpdateHistory(cpuHistory.Window(now), now, Math.Max(5, 3 * settings.PollSeconds));
            tips.SetToolTip(cpuGraph, cpuGraph.AccessibleDescription + ".\nСлева −60 секунд, справа сейчас; вертикальная шкала 0–100%. История только в памяти. Разрывы означают отсутствие отсчётов.");
            LayoutBlocks();
        }
        catch (Exception e) { Log.Write(e.ToString()); }
        finally { busy = false; if (resetPending && !closing) ResetRules(); if (closing && !IsDisposed && IsHandleCreated) BeginInvoke(Close); }
    }
    static Reading Safe(Func<Reading> read, string name, double now)
    {
        try { return read(); } catch (Exception e) { Log.Write(name + ": " + e.Message); return new(null, name + "  —", e.Message, now); }
    }
    void OpenSettings()
    {
        if (dialog != null) { dialog.Activate(); return; }
        dialog = new SettingsForm(settings);
        dialog.FormClosed += (_, _) =>
        {
            var completed = dialog!; dialog = null;
            if (!closing && completed.DialogResult == DialogResult.OK) { settings = completed.Value; ResetRules(); LayoutBlocks(); PositionBar(); }
            completed.Dispose();
        };
        dialog.Show(); dialog.Activate();
    }
    void OpenConsumers()
    {
        if (consumers != null) { consumers.Activate(); return; }
        consumers = new ConsumersForm();
        consumers.FormClosed += (_, _) => { consumers = null; };
        consumers.UpdateReading(insights, insights != null && clock.Elapsed.TotalSeconds - insights.At <= 9);
        consumers.Show(); consumers.Activate();
    }
    void SetVisible(bool visible)
    {
        if (visible) { Show(); Register(); } else { Unregister(); Hide(); }
    }
    void WriteDiagnostics()
    {
        var taskbar = Native.AppBarData.For(0); Native.SHAppBarMessage(5, ref taskbar);
        var state = Native.AppBarData.For(0);
        var snapshot = new {
            Visible, registered, Bounds, DeviceDpi,
            Monitors = Screen.AllScreens.Select(s => new { s.DeviceName, s.Primary, s.Bounds, s.WorkingArea }),
            Taskbar = taskbar.Rect.Rectangle, TaskbarEdge = taskbar.Edge, TaskbarState = (ulong)Native.SHAppBarMessage(4, ref state),
            Metrics = readings.Select(r => new { r?.Text, r?.Percent, r?.Detail, AgeSeconds = r == null ? (double?)null : clock.Elapsed.TotalSeconds - r.At }),
            Gpu = gpu, Startup = Startup.Enabled, States = evaluators.Select(e => e.State.ToString()), MaximizedBounds = maximizedBounds,
            CpuInsights = insights, CpuHistory = cpuHistory.Window(clock.Elapsed.TotalSeconds),
            ContentWidth = flow.Width, Blocks = flow.Controls.Cast<Control>().Where(c => c.Visible).Select(c => new { c.AccessibleName, c.Text, c.Bounds }),
            Windows = Application.OpenForms.Cast<Form>().Select(f => new { f.Text, f.Visible, f.Bounds })
        };
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(Path.Combine(Settings.Folder, "diagnostics.local.json"), System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    void Inspect(bool enabled)
    {
        Pinning.SetCurrentProcessExplicitAppUserModelID(enabled ? null : Pinning.AppId);
        // Temporary opt-in visibility to window inspection tools; does not change saved preferences.
        if (enabled && !inspectionStyle.HasValue)
        {
            inspectionStyle = Native.GetWindowLongPtr(Handle, -20);
            Native.SetWindowLongPtr(Handle, -20, (nint)(((long)inspectionStyle.Value & ~0x08000080L) | 0x00040000L));
        }
        else if (!enabled && inspectionStyle.HasValue)
        { Native.SetWindowLongPtr(Handle, -20, inspectionStyle.Value); inspectionStyle = null; }
        Native.SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x37);
    }
    void CheckMaximized()
    {
        var check = new Form { Text = "PCStatusBar — проверка рабочей области", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(Bounds.Left + 100, Bounds.Top - 300, 400, 200), WindowState = FormWindowState.Maximized };
        var stop = new System.Windows.Forms.Timer { Interval = 600 };
        stop.Tick += (_, _) => { maximizedBounds = check.Bounds; stop.Stop(); stop.Dispose(); check.Close(); check.Dispose(); WriteDiagnostics(); };
        check.Shown += (_, _) => stop.Start(); check.Show();
    }
    void DisplayChanged(object? sender, EventArgs e) { if (!closing && IsHandleCreated) BeginInvoke(QueuePosition); }
    void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !closing && IsHandleCreated) BeginInvoke(() => { ResetRules(); QueuePosition(); });
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Callback)
        {
            if (m.WParam == 1 && !positioning) QueuePosition();
            if (m.WParam == 2) Native.SetWindowPos(Handle, m.LParam != 0 ? 1 : -1, 0, 0, 0, 0, 0x13);
        }
        else if (m.Msg == taskbarCreated)
        {
            registered = false; tray.Visible = false; tray.Visible = true; Register();
        }
        else if (m.Msg == Program.ControlMessage)
        {
            int cmd = (int)m.WParam;
            BeginInvoke(() => { if (cmd == 1) Close(); else if (cmd == 2) SetVisible(false); else if (cmd == 3) SetVisible(true); else if (cmd == 4) WriteDiagnostics(); else if (cmd == 5) Inspect(true); else if (cmd == 6) Inspect(false); else if (cmd == 7) CheckMaximized(); else OpenSettings(); });
        }
        else if (m.Msg is 0x02E0 or 0x007E) QueuePosition();
        base.WndProc(ref m);
        if (registered && !positioning && m.Msg is 0x0006 or 0x0047)
        {
            var data = Native.AppBarData.For(Handle); Native.SHAppBarMessage(m.Msg == 6 ? 6u : 9u, ref data);
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy) { closing = true; timer.Stop(); Unregister(); e.Cancel = true; return; }
        closing = true; timer.Stop(); Unregister();
        SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        dialog?.Close(); consumers?.Close(); tray.Visible = false;
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tray.Dispose(); tips.Dispose(); menu.Dispose(); temperature.Dispose(); insightsProvider.Dispose(); Font.Dispose(); }
        base.Dispose(disposing);
    }
}
