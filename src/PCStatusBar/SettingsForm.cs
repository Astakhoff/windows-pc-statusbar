namespace PCStatusBar;

internal sealed class SettingsForm : Form
{
    public Settings Value { get; }
    readonly TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(16) };
    readonly List<Action> apply = [];
    public SettingsForm(Settings current)
    {
        Value = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(current))!;
        Text = "PCStatusBar — настройки"; Icon = AppIcon.Image; Size = new Size(650, 750); MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        Controls.Add(table);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        Controls.Add(footer);
        var monitor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        monitor.Items.Add("Основной монитор");
        foreach (var screen in Screen.AllScreens) monitor.Items.Add(screen.DeviceName);
        monitor.SelectedIndex = Math.Max(0, monitor.Items.IndexOf(Value.Monitor));
        Row("Монитор", monitor); apply.Add(() => Value.Monitor = monitor.SelectedIndex == 0 ? "" : monitor.Text);
        Number("Высота панели (логические px)", Value.Height, 32, 60, v => Value.Height = (int)v);
        var overlay = new CheckBox { Checked = Value.TaskbarOverlay, AutoSize = true };
        Row("Наложение внутри taskbar", overlay); apply.Add(() => Value.TaskbarOverlay = overlay.Checked);
        var adaptive = new CheckBox { Checked = Value.OverlayAdaptive, AutoSize = true };
        Row("Автосжатие по размеру и занятым областям taskbar", adaptive); apply.Add(() => Value.OverlayAdaptive = adaptive.Checked);
        Number("Базовая ширина taskbar для пропорций (0 = авто)", Value.OverlayReferenceWidth, 0, 30000, v => Value.OverlayReferenceWidth = (int)v);
        Number("Отступ слева от края taskbar (логические px)", Value.OverlayLeft, 0, 20000, v => Value.OverlayLeft = (int)v);
        Number("Отступ справа от края taskbar (логические px)", Value.OverlayRight, 0, 20000, v => Value.OverlayRight = (int)v);
        Number("CPU / RAM: интервал (сек.)", Value.PollSeconds, 1, 10, v => Value.PollSeconds = (int)v);
        Number("C: / D: интервал (сек.)", Value.DiskSeconds, 2, 120, v => Value.DiskSeconds = (int)v);
        foreach (var (label, rule) in new[] { ("CPU", Value.Cpu), ("RAM", Value.Ram), ("C: и D: свободно", Value.Disk) })
        {
            Row(label + (rule.Inverse ? " — ниже хуже" : " — выше хуже"), new Label());
            Number("Жёлтый порог (%)", rule.Yellow, 0, 100, v => rule.Yellow = v);
            Number("Красный порог (%)", rule.Red, 0, 100, v => rule.Red = v);
            Number("Запас восстановления (п.п.)", rule.Margin, 0, 30, v => rule.Margin = v);
            if (!rule.Inverse)
            {
                Number("Выдержка ухудшения (сек.)", rule.RiseSeconds, 0, 60, v => rule.RiseSeconds = v);
                Number("Выдержка восстановления (сек.)", rule.RecoverSeconds, 0, 60, v => rule.RecoverSeconds = v);
            }
        }
        Row("Видимость блоков — шестерёнка справа на панели.", new Label());
        Row("GPU: NVML, NVIDIA GPU #0. CPU Package недоступен.", new Label());
        var gpuYellow = new TextBox { Text = Value.GpuYellow?.ToString() ?? "", Dock = DockStyle.Fill };
        var gpuRed = new TextBox { Text = Value.GpuRed?.ToString() ?? "", Dock = DockStyle.Fill };
        Row("Ваш жёлтый порог GPU (°C; пусто — нейтрально)", gpuYellow);
        Row("Ваш красный порог GPU (°C; пусто — нейтрально)", gpuRed);
        var startup = new CheckBox { Checked = Startup.Enabled, AutoSize = true };
        Row("Запускать после входа в Windows", startup);
        var save = new Button { Text = "Сохранить", AutoSize = true };
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        cancel.Click += (_, _) => Close();
        Row("Пороги — уровни внимания, а не диагноз неисправности.", new Label());
        footer.Controls.Add(cancel); footer.Controls.Add(save);
        var pin = new Button { Text = "Закрепить на панели задач", AutoSize = true, Enabled = false };
        var pinState = new Label { AutoSize = true, MaximumSize = new Size(400, 0), Text = "Проверка API закрепления…" };
        Row("Закрепление ярлыка (дополнительно)", pin);
        Row("", pinState);
        Shown += async (_, _) => { var state = await Pinning.Availability(); if (!IsDisposed) { pinState.Text = state == "Доступно" ? "Windows запросит ваше подтверждение." : state + ". Ручной способ: «Пуск» → PCStatusBar → закрепить на панели задач."; pin.Enabled = state == "Доступно"; } };
        pin.Click += async (_, _) =>
        {
            pin.Enabled = false;
            try { pinState.Text = await Pinning.Request(); }
            catch (Exception e) { pinState.Text = e.Message; }
        };
        save.Click += (_, _) =>
        {
            if ((!string.IsNullOrWhiteSpace(gpuYellow.Text) && !double.TryParse(gpuYellow.Text, out _)) ||
                (!string.IsNullOrWhiteSpace(gpuRed.Text) && !double.TryParse(gpuRed.Text, out _)))
            { MessageBox.Show(this, "Температурные пороги должны быть числами либо пустыми."); return; }
            Value.GpuYellow = double.TryParse(gpuYellow.Text, out double gy) ? gy : null;
            Value.GpuRed = double.TryParse(gpuRed.Text, out double gr) ? gr : null;
            foreach (var action in apply) action();
            if (!Value.Valid()) { MessageBox.Show(this, "Проверьте порядок порогов. Границы восстановления должны оставаться в 0–100% и не пересекать соседний порог."); return; }
            try { Value.CopyVisibilityFrom(current); Value.Save(); Startup.Set(startup.Checked); DialogResult = DialogResult.OK; Close(); }
            catch (Exception e) { MessageBox.Show(this, "Не удалось сохранить: " + e.Message); }
        };
        AcceptButton = save; CancelButton = cancel;
    }
    void Row(string text, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(410, 0), Margin = new Padding(3, 7, 3, 7) }, 0, row);
        control.Margin = new Padding(3, 5, 3, 5); table.Controls.Add(control, 1, row);
    }
    void Number(string text, double value, int min, int max, Action<double> setter)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = (decimal)value, DecimalPlaces = 1, Dock = DockStyle.Fill };
        Row(text, n); apply.Add(() => setter((double)n.Value));
    }
}
