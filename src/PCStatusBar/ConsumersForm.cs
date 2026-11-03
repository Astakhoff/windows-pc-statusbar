namespace PCStatusBar;

internal sealed class ConsumersForm : Form
{
    readonly Label explanation = new() { Dock = DockStyle.Top, Height = 70, Padding = new Padding(12) };
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
    CpuInsightsReading? lastReading;
    bool? wasFresh;
    public ConsumersForm()
    {
        Text = "PCStatusBar — потребители CPU"; Size = new Size(700, 650); MinimumSize = new Size(540, 400);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        list.Columns.Add("Процесс", 340); list.Columns.Add("PID", 100); list.Columns.Add("CPU, % компьютера", 180);
        Controls.Add(list); Controls.Add(explanation);
    }
    public void UpdateReading(CpuInsightsReading? reading, bool fresh)
    {
        if (ReferenceEquals(lastReading, reading) && wasFresh == fresh) return;
        lastReading = reading; wasFresh = fresh;
        explanation.Text = !fresh || reading == null ? "Нет свежего измерения. Ожидание данных…" :
            $"Доля всей мощности компьютера ({reading.LogicalProcessors} логических CPU). Обновление раз в 3 секунды.\nПоказаны доступные процессы; недоступно / завершилось при чтении: {reading.Skipped}.";
        int? selected = list.SelectedItems.Count > 0 && int.TryParse(list.SelectedItems[0].SubItems[1].Text, out int id) ? id : null;
        list.BeginUpdate(); list.Items.Clear();
        if (fresh && reading?.HasBaseline == true)
            foreach (var p in reading.Processes.Take(100))
            {
                var item = new ListViewItem([p.Name, p.Id.ToString(), p.CpuPercent.ToString("0.0")]);
                list.Items.Add(item); item.Selected = p.Id == selected;
            }
        list.EndUpdate();
    }
}
