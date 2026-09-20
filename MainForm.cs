using System.Collections.Concurrent;
using System.Diagnostics;

namespace UdpNdi;

public sealed class MainForm : Form
{
    AppSettings settings;
    readonly SlotRunner[] runners = Enumerable.Range(0, 10).Select(_ => new SlotRunner()).ToArray();
    readonly DataGridView grid = new();
    readonly TextBox log = new();
    readonly Label summary = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    readonly ConcurrentQueue<string> messages = new();
    readonly FlowLayoutPanel buttons = new();
    bool closing;
    public MainForm()
    {
        Text = "UDP to NDI"; ClientSize = new(1180, 634); MinimumSize = new(1060, 674);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new("Segoe UI", 10); BackColor = Color.FromArgb(18, 24, 34); ForeColor = Color.WhiteSmoke;
        try { settings = AppSettings.Load(); }
        catch (Exception ex) { settings = new(); MessageBox.Show($"Could not load saved settings. Defaults are shown; the original file is retained until you save.\n\n{ex.Message}", "Settings"); }
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.Absolute, 64)); layout.RowStyles.Add(new(SizeType.Absolute, 52));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 0));
        layout.RowStyles.Add(new(SizeType.Absolute, 24));
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new PictureBox { Image = Icon?.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Bounds = new(0, 4, 36, 36) });
        header.Controls.Add(new Label { Text = "UDP to NDI", Font = new("Segoe UI Semibold", 22), AutoSize = true, Location = new(48, 0) });
        summary.Dock = DockStyle.Right; summary.Width = 330; summary.TextAlign = ContentAlignment.MiddleRight; summary.Padding = new(0, 0, 0, 16); summary.ForeColor = Color.LightSteelBlue;
        header.Controls.Add(summary); layout.Controls.Add(header, 0, 0);
        buttons.Dock = DockStyle.Fill; buttons.WrapContents = false;
        Button AddButton(string title, Action action)
        {
            var button = new Button { Text = title, AutoSize = true, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 48, 65), Margin = new(0, 0, 10, 0), Padding = new(9, 3, 9, 3) };
            button.FlatAppearance.BorderColor = Color.FromArgb(65, 80, 98);
            button.Click += (_, _) => Guard(action); buttons.Controls.Add(button); return button;
        }
        var startAll = AddButton("Start all", () => { for (int i = 0; i < 10; i++) Start(i); });
        startAll.BackColor = Color.FromArgb(100, 230, 183); startAll.ForeColor = Color.FromArgb(18, 35, 29);
        var stopAll = AddButton("Stop all", () => { }); stopAll.Click += async (_, _) => await StopAll();
        var activity = AddButton("Activity", () => { });
        activity.Click += (_, _) => {
            bool show = !log.Visible;
            log.Visible = show; layout.RowStyles[3].Height = show ? 150 : 0;
            Height += show ? 150 : -150;
            activity.Text = show ? "Hide activity" : "Activity";
        };
        var more = AddButton("More…", () => { });
        var menu = new ContextMenuStrip();
        menu.Items.Add("Choose FFmpeg…", null, (_, _) => Guard(SelectFfmpeg));
        menu.Items.Add("Download FFmpeg", null, (_, _) => Guard(() => Process.Start(new ProcessStartInfo("https://www.gyan.dev/ffmpeg/builds/") { UseShellExecute = true })));
        menu.Items.Add("Open guide", null, (_, _) => Guard(() => Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "README.md")) { UseShellExecute = true })));
        menu.Items.Add("Licences and credits", null, (_, _) => LegalUi.Show(this));
        more.Click += (_, _) => menu.Show(more, new Point(0, more.Height));
        FormClosed += (_, _) => menu.Dispose();
        layout.Controls.Add(buttons, 0, 1);
        grid.Dock = DockStyle.Fill; grid.BackgroundColor = BackColor; grid.BorderStyle = BorderStyle.None;
        grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true; grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(28, 38, 52), ForeColor = Color.Silver, Padding = new(4), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        grid.DefaultCellStyle = new() { BackColor = Color.FromArgb(24, 32, 44), ForeColor = Color.WhiteSmoke, SelectionBackColor = Color.FromArgb(42, 58, 78), SelectionForeColor = Color.White, Padding = new(4) };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(28, 37, 50);
        grid.GridColor = Color.FromArgb(43, 54, 70); grid.RowTemplate.Height = 40; grid.ColumnHeadersHeight = 36;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        foreach (var (name, weight) in new[] { ("Slot", 30), ("NDI name", 110), ("Input", 180), ("Status", 65), ("Viewers", 40) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Configure", HeaderText = "", Text = "Edit", UseColumnTextForButtonValue = true, FillWeight = 40, FlatStyle = FlatStyle.Flat });
        grid.Columns.Add(new DataGridViewButtonColumn { Name = "Action", HeaderText = "", FillWeight = 40, FlatStyle = FlatStyle.Flat });
        for (int i = 0; i < 10; i++) { grid.Rows.Add(); runners[i].Log += message => { if (messages.Count < 500) messages.Enqueue(message); }; }
        grid.CellContentClick += async (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || closing) return;
            int i = e.RowIndex;
            if (grid.Columns[e.ColumnIndex].Name == "Configure") Guard(() => Configure(i));
            if (grid.Columns[e.ColumnIndex].Name == "Action")
            {
                if (runners[i].Running) { grid.Enabled = false; await runners[i].StopAsync(); grid.Enabled = true; }
                else Guard(() => Start(i));
                RefreshRows();
            }
        };
        layout.Controls.Add(grid, 0, 2);
        log.Dock = DockStyle.Fill; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
        log.BackColor = Color.FromArgb(12, 18, 26); log.ForeColor = Color.LightSteelBlue; log.Font = new("Consolas", 9); log.BorderStyle = BorderStyle.FixedSingle;
        log.Visible = false; log.Margin = new(3, 12, 3, 3);
        layout.Controls.Add(log, 0, 3);
        var ndiLink = LegalUi.NdiLink(Point.Empty);
        ndiLink.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        ndiLink.Margin = new(3, 6, 3, 0);
        layout.Controls.Add(ndiLink, 0, 4); Controls.Add(layout);
        timer.Tick += (_, _) =>
        {
            RefreshRows();
            int count = 0;
            while (count++ < 30 && messages.TryDequeue(out var message)) log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
            if (log.TextLength > 40000) log.Text = log.Text[^25000..];
        };
        timer.Start(); RefreshRows();
        messages.Enqueue("Ready.");
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            e.Cancel = true; closing = true; timer.Stop(); Enabled = false;
            await Task.WhenAll(runners.Select(r => r.StopAsync()));
            Close();
        };
        FormClosed += (_, _) => timer.Dispose();
    }
    void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "UDP to NDI", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    void RefreshRows()
    {
        for (int i = 0; i < 10; i++)
        {
            var s = settings.Slots[i]; var r = runners[i]; var row = grid.Rows[i];
            string status = r.State;
            bool live = status == "Live";
            bool waiting = r.Running && status is not ("Live" or "Stopped" or "Error");
            row.DefaultCellStyle.BackColor = live ? Color.FromArgb(22, 75, 48) : waiting ? Color.FromArgb(235, 193, 61) : i % 2 == 0 ? Color.FromArgb(24, 32, 44) : Color.FromArgb(28, 37, 50);
            row.DefaultCellStyle.ForeColor = live ? Color.FromArgb(220, 255, 230) : waiting ? Color.FromArgb(40, 32, 10) : Color.WhiteSmoke;
            row.DefaultCellStyle.SelectionBackColor = live ? Color.FromArgb(30, 100, 62) : waiting ? Color.FromArgb(255, 214, 85) : Color.FromArgb(42, 58, 78);
            row.DefaultCellStyle.SelectionForeColor = waiting ? Color.FromArgb(40, 32, 10) : Color.White;
            string displayStatus = waiting ? (status.Contains("retry", StringComparison.OrdinalIgnoreCase) ? "Retrying" : "Waiting") : status;
            row.SetValues($"{i + 1:00}", s.Name, s.Input == "SDP file" ? Path.GetFileName(s.SdpPath) : $"{s.Input} · {s.Address}:{s.Port}", displayStatus, r.Receivers, "Edit", r.Running ? "Stop" : "Start");
            row.Cells["Status"].Style.ForeColor = live ? Color.MediumAquamarine : waiting ? Color.FromArgb(40, 32, 10) : status == "Error" ? Color.Salmon : Color.LightSteelBlue;
            row.Cells["Status"].ToolTipText = $"Retries: {r.Retries} (unlimited)\n{r.Detail}";
            row.Cells["Input"].ToolTipText = (r.DetectedFormat?.Description ?? "Automatically mirrors the source format") + "\n" + r.SourceDescription;
        }
        int liveCount = runners.Count(r => r.State == "Live");
        int waitingCount = runners.Count(r => r.Running && r.State != "Live");
        summary.Text = liveCount + waitingCount == 0 ? (File.Exists(settings.Ffmpeg) ? "10 slots · Ready" : "FFmpeg needed · see More") : $"{liveCount} live · {waitingCount} waiting";
    }
    void Start(int i)
    {
        if (runners[i].Running) return;
        var s = settings.Slots[i];
        for (int j = 0; j < 10; j++)
        {
            if (i == j || !runners[j].Running) continue;
            var other = settings.Slots[j];
            if (s.Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Slot {i + 1}: NDI name is already running in slot {j + 1}.");
            if (s.Input != "SDP file" && other.Input != "SDP file" && (s.Address == other.Address || s.Address == "0.0.0.0" || other.Address == "0.0.0.0") && Math.Abs(s.Port - other.Port) <= (s.Input.StartsWith("RTP") || other.Input.StartsWith("RTP") ? 1 : 0)) throw new ArgumentException($"Slot {i + 1}: input port overlaps slot {j + 1}.");
        }
        runners[i].Start(s, settings.Ffmpeg); RefreshRows();
    }
    async Task StopAll() { buttons.Enabled = false; grid.Enabled = false; await Task.WhenAll(runners.Select(r => r.StopAsync())); buttons.Enabled = true; grid.Enabled = true; RefreshRows(); }
    void Save() { settings.Save(); messages.Enqueue("All 10 slot configurations saved."); }
    void Configure(int i)
    {
        if (runners[i].Running) throw new InvalidOperationException("Stop this slot before changing its configuration.");
        using var editor = new SlotDialog(settings.Slots[i], i + 1);
        if (editor.ShowDialog(this) == DialogResult.OK) { settings.Slots[i] = editor.Result; Save(); RefreshRows(); }
    }
    void SelectFfmpeg()
    {
        using var dialog = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe", Title = "Select ffmpeg.exe", FileName = settings.Ffmpeg };
        if (dialog.ShowDialog(this) == DialogResult.OK) { settings.Ffmpeg = dialog.FileName; Save(); }
    }
}
