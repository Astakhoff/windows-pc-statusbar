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
    readonly QuotaTicker quotaTicker = new();
    readonly IQuotaProvider[] quotaProviders = [new CodexQuotaProvider(), new ClaudeQuotaProvider()];
    readonly CancellationTokenSource quotaCancellation = new();
    Task<QuotaSnapshot[]>? quotaRequest;
    QuotaSnapshot[] quotaSnapshots = [QuotaSnapshot.Missing("Codex", "Codex app-server", "Ожидание первого чтения", DateTimeOffset.UtcNow), QuotaSnapshot.Missing("Claude", "Claude desktop", "Ожидание первого чтения", DateTimeOffset.UtcNow)];
    double lastQuotaAttempt = -1000;
    QuotaDetailsForm? quotaDetails;
    readonly Label queueBlock = new() { Text = "Очередь  —", AccessibleName = "Очередь CPU" };
    readonly Label leadersBlock = new() { Text = "Лидеры CPU  —", AccessibleName = "Основные потребители CPU" };
    readonly Label moreBlock = new() { Text = "…", AccessibleName = "Все показатели", TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, ForeColor = Color.Silver };
    readonly Label gear = new() { Text = "⚙", AccessibleName = "Видимость блоков", TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, ForeColor = Color.Silver };
    readonly ContextMenuStrip visibilityMenu = new();
    readonly Temperature temperature = new();
    readonly NotifyIcon tray;
    readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    readonly ContextMenuStrip menu = new();
    readonly Label[] blocks = new Label[5];
    readonly FlowLayoutPanel flow = new() { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = false, Padding = new Padding(0) };
    Settings settings = Settings.Load();
    StatusEvaluator[] evaluators = [];
    Reading?[] readings = new Reading?[4];
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
    bool overlayBehindFullscreen;
    Task<TaskbarMeasurement?>? shellRequest;
    TaskbarMeasurement? shellMeasurement;
    TaskbarFrame? shellFrame;
    double lastShellAttempt = -100;
    string overlayStatus = "";
    bool compactLayout;
    string[] hiddenBlocks = [];
    int previewWidth;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }

    public BarForm()
    {
        Text = Program.WindowTitle; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(23, 26, 32); ForeColor = Color.White; DoubleBuffered = true;
        Font = new Font("Segoe UI", 10F);
        flow.Dock = DockStyle.None;
        Controls.Add(flow); Controls.Add(quotaTicker); Controls.Add(gear);
        ConfigureVisibilityMenu();
        gear.Click += (_, _) => visibilityMenu.Show(gear, new Point(gear.Width, 0), ToolStripDropDownDirection.AboveLeft);
        tips.SetToolTip(gear, "Показать или скрыть блоки панели");
        quotaTicker.Cursor = Cursors.Hand;
        quotaTicker.Click += (_, _) => OpenQuotaDetails();
        for (int i = 0; i < blocks.Length; i++)
        {
            blocks[i] = new Label { Text = new[] { "CPU  —", "RAM  —", "C: свободно  —", "D: свободно  —", "GPU  —" }[i], TextAlign = ContentAlignment.MiddleLeft, AutoSize = false, AutoEllipsis = true, Margin = new Padding(4, 3, 4, 3), Padding = new Padding(10, 0, 4, 0), BackColor = Color.FromArgb(32, 36, 43), AccessibleName = new[] { "CPU", "RAM", "C: свободно", "D: свободно", "Температуры" }[i] };
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
        flow.Controls.AddRange([leadersBlock, queueBlock, blocks[0], cpuGraph, blocks[1], blocks[2], blocks[3], blocks[4], moreBlock]);
        moreBlock.Click += (_, _) => ShowAllMetrics();
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        menu.Items.Add("Видимость блоков…", null, (_, _) => visibilityMenu.Show(Cursor.Position));
        menu.Items.Add("Потребители CPU…", null, (_, _) => OpenConsumers());
        menu.Items.Add("Все показатели…", null, (_, _) => ShowAllMetrics());
        menu.Items.Add("Лимиты Codex и Claude…", null, (_, _) => OpenQuotaDetails());
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
        quotaTicker.ContextMenuStrip = menu;
        moreBlock.ContextMenuStrip = menu;
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
        evaluators = [new(settings.Cpu, Math.Max(5, 3 * settings.PollSeconds)), new(settings.Ram, Math.Max(5, 3 * settings.PollSeconds)), new(settings.Disk, Math.Max(5, 3 * settings.DiskSeconds)), new(settings.Disk, Math.Max(5, 3 * settings.DiskSeconds))];
        lastPoll = lastDisk = -100; metrics.Reset(); readings = new Reading?[4];
        cpuHistory.Clear(); insightsProvider.Reset(); insights = null; lastInsights = -100;
    }
    void LayoutBlocks()
    {
        float scale = DeviceDpi / 96F;
        Control[] layout = [leadersBlock, queueBlock, blocks[0], cpuGraph, blocks[1], blocks[2], blocks[3], blocks[4]];
        int gearWidth = Math.Min(ClientSize.Width, Math.Max(28, (int)(32 * scale)));
        int available = Math.Max(0, ClientSize.Width - gearWidth);
        int gap = Math.Max(2, (int)(3 * scale)), endPadding = (int)(8 * scale);
        SuspendLayout(); flow.SuspendLayout();
        try
        {
            double now = clock.Elapsed.TotalSeconds;
            bool Fresh(int i) => readings[i]?.Percent != null && now - readings[i]!.At <= Math.Max(5, 3 * (i >= 2 ? settings.DiskSeconds : settings.PollSeconds));
            string[] fallback = ["CPU  —", "RAM  —", "C: свободно  —", "D: свободно  —"];
            for (int i = 0; i < readings.Length; i++) blocks[i].Text = (readings[i]?.Text ?? fallback[i]) + (readings[i]?.Percent != null && !Fresh(i) ? " (устарело)" : "");
            blocks[4].Text = gpu?.Percent != null && now - gpu.At <= 6 ? gpu.Text : "GPU  —";
            queueBlock.Text = insights?.Queue != null && now - insights.At <= 9 ? $"Очередь  {insights.Queue}" : "Очередь  —";
            var fullText = layout.Select(c => c.Text).ToArray();
            int Measure(Control c) => c is Label label ? label.GetPreferredSize(new Size(int.MaxValue, int.MaxValue)).Width + (int)Math.Ceiling(4 * scale) : (int)(144 * scale);
            var natural = layout.Select(Measure).ToArray();
            blocks[0].Text = Fresh(0) ? $"CPU {readings[0]!.Percent:0}%" : "CPU —";
            blocks[1].Text = Fresh(1) ? $"RAM {readings[1]!.Percent:0}%" : "RAM —";
            for (int i = 2; i <= 3; i++) blocks[i].Text = Fresh(i) && readings[i]!.AvailableBytes.HasValue ? $"{(i == 2 ? "C:" : "D:")} {Units.GiB(readings[i]!.AvailableBytes!.Value):0} GiB" : (i == 2 ? "C: —" : "D: —");
            queueBlock.Text = insights?.Queue != null && now - insights.At <= 9 ? $"Оч. {insights.Queue}" : "Оч. —";
            var compact = layout.Select(Measure).ToArray(); compact[0] = Math.Min(compact[0], (int)(160 * scale)); compact[3] = (int)(90 * scale);
            bool[] enabled = [settings.ShowLeaders, settings.ShowQueue, settings.ShowCpu, settings.ShowGraph, settings.ShowRam, settings.ShowDiskC, settings.ShowDiskD, settings.ShowTemperature];
            var plan = ResponsiveLayout.Calculate(available, natural, compact, enabled, [3, 0, 7, 1, 6, 5, 4, 2], gap, endPadding, settings.ShowQuotas ? (int)(240 * scale) : 0, (int)(24 * scale), (int)(16 * scale));
            compactLayout = plan.Compact;
            hiddenBlocks = layout.Where((c, i) => enabled[i] && !plan.Visible[i]).Select(c => c.AccessibleName ?? "").ToArray();
            int height = Math.Max(1, Math.Min((int)(settings.Height * scale) - 6, ClientSize.Height - 6));
            for (int i = 0; i < layout.Length; i++)
            {
                var control = layout[i];
                if (!plan.Compact) control.Text = fullText[i];
                control.Visible = plan.Visible[i];
                control.Margin = new Padding(gap, 3, gap, 3);
                control.Size = new Size(Math.Max(1, plan.Widths[i]), height);
            }
            moreBlock.Visible = plan.OverflowWidth > 0; moreBlock.Margin = new Padding(gap, 3, gap, 3); moreBlock.Size = new Size(Math.Max(1, plan.OverflowWidth), height);
            tips.SetToolTip(moreBlock, "Все показатели. Не поместились: " + string.Join(", ", hiddenBlocks));
            gear.Bounds = new Rectangle(ClientSize.Width - gearWidth, 0, gearWidth, ClientSize.Height);
            flow.Bounds = new Rectangle(available - plan.ContentWidth, 0, plan.ContentWidth, ClientSize.Height);
            int left = (int)(8 * scale);
            quotaTicker.Visible = settings.ShowQuotas;
            quotaTicker.Bounds = new Rectangle(left, 0, Math.Max(0, available - flow.Width - left * 2), ClientSize.Height);
        }
        finally { flow.ResumeLayout(true); ResumeLayout(true); }
    }
    (string Name, Func<bool> Get, Action<bool> Set)[] VisibilityItems() =>
    [
        ("Лидеры", () => settings.ShowLeaders, v => settings.ShowLeaders = v),
        ("Очередь CPU", () => settings.ShowQueue, v => settings.ShowQueue = v),
        ("CPU", () => settings.ShowCpu, v => settings.ShowCpu = v),
        ("График CPU", () => settings.ShowGraph, v => settings.ShowGraph = v),
        ("RAM", () => settings.ShowRam, v => settings.ShowRam = v),
        ("C: свободно", () => settings.ShowDiskC, v => settings.ShowDiskC = v),
        ("D: свободно", () => settings.ShowDiskD, v => settings.ShowDiskD = v),
        ("GPU", () => settings.ShowTemperature, v => settings.ShowTemperature = v),
        ("Лента Codex / Claude", () => settings.ShowQuotas, v => settings.ShowQuotas = v)
    ];
    void ConfigureVisibilityMenu()
    {
        foreach (var entry in VisibilityItems())
        {
            var item = new ToolStripMenuItem(entry.Name) { CheckOnClick = true, Checked = entry.Get() };
            visibilityMenu.Items.Add(item);
            visibilityMenu.Opening += (_, _) => item.Checked = entry.Get();
            item.Click += (_, _) =>
            {
                bool previous = entry.Get(); entry.Set(item.Checked);
                try { settings.Save(); LayoutBlocks(); }
                catch (Exception e) { entry.Set(previous); item.Checked = previous; MessageBox.Show(this, "Не удалось сохранить: " + e.Message); }
            };
        }
        visibilityMenu.Items.Add(new ToolStripSeparator());
        visibilityMenu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
    }
    void Register()
    {
        if (settings.TaskbarOverlay && Visible) { Unregister(); PositionBar(); return; }
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
        if ((!registered && !settings.TaskbarOverlay) || positioning || !Visible) return;
        positioning = true;
        try
        {
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Screen.PrimaryScreen!;
            int h = (int)Math.Round(settings.Height * DeviceDpi / 96.0);
            var b = screen.Bounds;
            if (settings.TaskbarOverlay)
            {
                var frame = TaskbarOverlay.FindForMonitor(screen.Bounds);
                if (frame == null || frame.Bounds.Width <= frame.Bounds.Height) return;
                Rectangle rect;
                if (settings.OverlayAdaptive)
                {
                    RefreshShellMeasurement(frame);
                    if (settings.OverlayReferenceWidth == 0)
                    {
                        settings.OverlayReferenceWidth = Math.Max(1, (int)Math.Round(frame.Bounds.Width / (frame.Dpi / 96.0)));
                        try { settings.Save(); } catch (Exception e) { Log.Write(e.Message); }
                    }
                    if (shellMeasurement?.Handle == frame.Handle && DateTimeOffset.UtcNow - shellMeasurement.At <= TimeSpan.FromSeconds(15))
                    {
                        double ratio = frame.Dpi / (double)shellMeasurement.Dpi;
                        rect = OverlayGeometry.Adaptive(frame.Bounds, settings.OverlayReferenceWidth, settings.OverlayLeft, settings.OverlayRight, settings.Height, frame.Dpi / 96.0,
                            (int)Math.Ceiling(shellMeasurement.OccupiedLeft * ratio), (int)Math.Ceiling(shellMeasurement.OccupiedRight * ratio));
                        overlayStatus = rect.Width > 0 ? "adaptive" : "no-free-space";
                    }
                    else { rect = new(frame.Bounds.Left, frame.Bounds.Top, 0, Math.Min(h, frame.Bounds.Height)); overlayStatus = "awaiting-taskbar-layout"; }
                }
                else { rect = OverlayGeometry.Calculate(frame.Bounds, settings.OverlayLeft, settings.OverlayRight, settings.Height, frame.Dpi / 96.0); overlayStatus = "fixed"; }
                if (previewWidth > 0 && rect.Width > previewWidth) rect = new(rect.Right - previewWidth, rect.Top, previewWidth, rect.Height);
                if (Bounds != rect) Native.SetWindowPos(Handle, overlayBehindFullscreen ? 1 : -1, rect.X, rect.Y, rect.Width, rect.Height, 0x10);
                LayoutBlocks(); return;
            }
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
            RefreshQuotas();
            if (settings.TaskbarOverlay && Visible)
            {
                PositionBar();
                var screen = Screen.FromHandle(Handle);
                bool fullscreen = TaskbarOverlay.FullscreenForeground(Handle, screen.Bounds);
                if (!fullscreen || !overlayBehindFullscreen) Native.SetWindowPos(Handle, fullscreen ? 1 : -1, 0, 0, 0, 0, 0x13);
                overlayBehindFullscreen = fullscreen;
            }
            double now = clock.Elapsed.TotalSeconds;
            bool poll = now - lastPoll >= settings.PollSeconds, disk = now - lastDisk >= settings.DiskSeconds;
            if (poll || disk)
            {
                var result = await Task.Run(() => (cpu: poll ? Safe(() => metrics.Cpu(now), "CPU", now) : null,
                    ram: poll ? Safe(() => metrics.Ram(now), "RAM", now) : null,
                    disk: disk ? Safe(() => metrics.Disk(@"C:\", now), "C: свободно", now) : null,
                    diskD: disk ? Safe(() => metrics.Disk(@"D:\", now), "D: свободно", now) : null));
                if (closing) return;
                if (poll) { readings[0] = result.cpu; readings[1] = result.ram; lastPoll = now; cpuHistory.Add(now, result.cpu?.Percent); }
                if (disk) { readings[2] = result.disk; readings[3] = result.diskD; lastDisk = now; }
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
                blocks[4].Text = fresh ? gpu!.Text : "GPU  —";
                blocks[4].ForeColor = !fresh || !settings.GpuYellow.HasValue ? Color.Silver :
                    gpu!.Percent >= settings.GpuRed ? Color.FromArgb(255, 119, 125) :
                    gpu.Percent >= settings.GpuYellow ? Color.FromArgb(250, 209, 94) : Color.FromArgb(115, 214, 158);
                tips.SetToolTip(blocks[4], gpu?.Detail + (settings.GpuYellow.HasValue ? $"\nВаши пороги внимания: {settings.GpuYellow} / {settings.GpuRed} °C." : "\nПороги не настроены: число показано нейтрально."));
            }
            for (int i = 0; i < readings.Length; i++)
            {
                var reading = readings[i];
                double stale = Math.Max(5, 3 * (i >= 2 ? settings.DiskSeconds : settings.PollSeconds));
                double? value = reading != null && now - reading.At <= stale ? reading.Percent : null;
                var state = evaluators[i].Update(value, now);
                blocks[i].Text = (reading?.Text ?? new[] { "CPU  —", "RAM  —", "C: свободно  —", "D: свободно  —" }[i]) + (reading != null && now - reading.At > stale ? " (устарело)" : "");
                blocks[i].ForeColor = state switch { Severity.Green => Color.FromArgb(115, 214, 158), Severity.Yellow => Color.FromArgb(250, 209, 94), Severity.Red => Color.FromArgb(255, 119, 125), _ => Color.Silver };
                var rule = new[] { settings.Cpu, settings.Ram, settings.Disk, settings.Disk }[i];
                string status = state switch { Severity.Green => "нормальный уровень", Severity.Yellow => "внимание", Severity.Red => "высокий уровень внимания", _ => "нет достоверных свежих данных" };
                tips.SetToolTip(blocks[i], $"{reading?.Text}\n{reading?.Detail}\nСостояние: {status}.\nЖёлтый {rule.Yellow}%, красный {rule.Red}%; ухудшение {rule.RiseSeconds} с, восстановление {rule.RecoverSeconds} с, запас {rule.Margin} п.п. Цвет учитывает устойчивое превышение и восстановление.");
            }
            cpuGraph.LineColor = blocks[0].ForeColor;
            cpuGraph.UpdateHistory(cpuHistory.Window(now), now, Math.Max(5, 3 * settings.PollSeconds));
            tips.SetToolTip(cpuGraph, cpuGraph.AccessibleDescription + ".\nСлева −60 секунд, справа сейчас; вертикальная шкала 0–100%. История только в памяти. Разрывы означают отсутствие отсчётов.");
            LayoutBlocks();
            var quotaNow = DateTimeOffset.UtcNow;
            quotaTicker.SetQuotas(quotaSnapshots, quotaNow);
            tips.SetToolTip(quotaTicker, QuotaText.Ticker(quotaSnapshots, quotaNow) + "\nНажмите для источников, дат сброса и причин отсутствия данных. Наведение останавливает прокрутку.");
            quotaDetails?.UpdateSnapshots(quotaSnapshots, quotaNow);
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
            if (!closing && completed.DialogResult == DialogResult.OK) { Unregister(); settings = completed.Value; ResetRules(); Register(); LayoutBlocks(); }
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
    void RefreshShellMeasurement(TaskbarFrame frame)
    {
        if (shellFrame != frame) { shellFrame = frame; lastShellAttempt = -100; }
        if (shellRequest?.IsCompleted == true)
        {
            if (shellRequest.IsCompletedSuccessfully && shellRequest.Result is { } measured && measured.Handle == frame.Handle) shellMeasurement = measured;
            shellRequest = null;
        }
        if (shellRequest == null && clock.Elapsed.TotalSeconds - lastShellAttempt >= 3)
        {
            lastShellAttempt = clock.Elapsed.TotalSeconds;
            shellRequest = Task.Run(() => TaskbarOverlay.ReadOccupied(frame));
        }
    }
    void ShowAllMetrics()
    {
        var lines = readings.Select((r, i) => r?.Text ?? new[] { "CPU —", "RAM —", "C: —", "D: —" }[i]).ToList();
        lines.Add(gpu?.Text ?? "GPU —"); lines.Add(insights?.Queue != null ? $"Очередь CPU: {insights.Queue}" : "Очередь CPU: —");
        lines.AddRange(insights?.Groups.Take(5).Select(g => $"{g.Name}: {g.CpuPercent:0.0}%") ?? []);
        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "Снимок всех показателей", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    void RefreshQuotas()
    {
        if (quotaRequest?.IsCompleted == true)
        {
            if (quotaRequest.IsCompletedSuccessfully) quotaSnapshots = quotaRequest.Result;
            quotaRequest = null;
        }
        if (quotaRequest == null && clock.Elapsed.TotalSeconds - lastQuotaAttempt >= 60 && !quotaCancellation.IsCancellationRequested)
        {
            lastQuotaAttempt = clock.Elapsed.TotalSeconds;
            quotaRequest = Task.Run(() => Task.WhenAll(quotaProviders.Select(p => p.ReadAsync(quotaCancellation.Token))), quotaCancellation.Token);
        }
    }
    void OpenQuotaDetails()
    {
        if (quotaDetails != null) { quotaDetails.Activate(); return; }
        quotaDetails = new QuotaDetailsForm();
        quotaDetails.RefreshRequested += (_, _) => { lastQuotaAttempt = -1000; RefreshQuotas(); };
        quotaDetails.FormClosed += (_, _) => quotaDetails = null;
        quotaDetails.UpdateSnapshots(quotaSnapshots, DateTimeOffset.UtcNow);
        quotaDetails.Show(); quotaDetails.Activate();
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
            Visible, registered, Bounds, DeviceDpi, settings.TaskbarOverlay, settings.OverlayLeft, settings.OverlayRight, settings.OverlayAdaptive, settings.OverlayReferenceWidth,
            OverlayStatus = overlayStatus, ShellMeasurement = shellMeasurement, ShellFrame = shellFrame, CompactLayout = compactLayout, HiddenBlocks = hiddenBlocks, PreviewWidth = previewWidth,
            Monitors = Screen.AllScreens.Select(s => new { s.DeviceName, s.Primary, s.Bounds, s.WorkingArea }),
            Taskbar = taskbar.Rect.Rectangle, TaskbarEdge = taskbar.Edge, TaskbarState = (ulong)Native.SHAppBarMessage(4, ref state),
            Metrics = readings.Select(r => new { r?.Text, r?.Percent, r?.Detail, AgeSeconds = r == null ? (double?)null : clock.Elapsed.TotalSeconds - r.At }),
            Gpu = gpu, Startup = Startup.Enabled, States = evaluators.Select(e => e.State.ToString()), MaximizedBounds = maximizedBounds,
            CpuInsights = insights, CpuHistory = cpuHistory.Window(clock.Elapsed.TotalSeconds),
            ContentWidth = flow.Width, GearBounds = gear.Bounds, EnabledBlocks = VisibilityItems().Where(i => i.Get()).Select(i => i.Name), Blocks = flow.Controls.Cast<Control>().Where(c => c.Visible).Select(c => new { c.AccessibleName, c.Text, c.Bounds }),
            Quotas = quotaSnapshots, TickerBounds = quotaTicker.Bounds, TickerText = QuotaText.Ticker(quotaSnapshots, DateTimeOffset.UtcNow), TickerTags = QuotaText.Tags(quotaSnapshots, DateTimeOffset.UtcNow),
            Windows = Application.OpenForms.Cast<Form>().Select(f => new { f.Text, f.Visible, f.Bounds })
        };
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(Path.Combine(Settings.Folder, "diagnostics.local.json"), System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    void SetOverlay(bool enabled)
    {
        Unregister(); settings.TaskbarOverlay = enabled; settings.Save(); Register();
    }
    void WriteTaskbarRegions()
    {
        Directory.CreateDirectory(Settings.Folder);
        File.WriteAllText(Path.Combine(Settings.Folder, "taskbar.local.json"), System.Text.Json.JsonSerializer.Serialize(TaskbarOverlay.ReadShellRegions()));
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
            int width = (int)m.LParam;
            BeginInvoke(() => { if (cmd == 1) Close(); else if (cmd == 2) SetVisible(false); else if (cmd == 3) SetVisible(true); else if (cmd == 4) WriteDiagnostics(); else if (cmd == 5) Inspect(true); else if (cmd == 6) Inspect(false); else if (cmd == 7) CheckMaximized(); else if (cmd == 8) SetOverlay(true); else if (cmd == 9) SetOverlay(false); else if (cmd == 10) WriteTaskbarRegions(); else if (cmd == 11) { previewWidth = Math.Clamp(width, 0, 30000); PositionBar(); } else OpenSettings(); });
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
        closing = true; timer.Stop(); Unregister(); quotaCancellation.Cancel();
        try { quotaRequest?.Wait(750); } catch (AggregateException) { }
        SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        dialog?.Close(); consumers?.Close(); quotaDetails?.Close(); tray.Visible = false;
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tray.Dispose(); tips.Dispose(); menu.Dispose(); visibilityMenu.Dispose(); temperature.Dispose(); insightsProvider.Dispose(); Font.Dispose(); }
        base.Dispose(disposing);
    }
}
