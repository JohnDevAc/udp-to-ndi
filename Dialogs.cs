using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace UdpNdi;

public sealed class SlotDialog : Form
{
    public SlotSettings Result { get; private set; }
    public SlotDialog(SlotSettings original, int number)
    {
        Result = original with { };
        Text = $"Configure slot {number:00}"; Width = 660;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Font = new("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(18, 24, 34); ForeColor = Color.WhiteSmoke;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 2, AutoScroll = true };
        form.ColumnStyles.Add(new(SizeType.Absolute, 140)); form.ColumnStyles.Add(new(SizeType.Percent, 100));
        int row = 0;
        void Add(string label, Control control) { form.RowStyles.Add(new(SizeType.Absolute, 42)); form.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.LightSteelBlue }, 0, row); control.Dock = DockStyle.Fill; control.BackColor = Color.FromArgb(35, 48, 65); control.ForeColor = Color.WhiteSmoke; form.Controls.Add(control, 1, row++); }
        TextBox TextField(string title, string value) { var box = new TextBox { Text = value }; Add(title, box); return box; }
        NumericUpDown Number(string title, int value, int min, int max) { var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max) }; Add(title, box); return box; }
        var name = TextField("NDI name", original.Name);
        var nameLabel = form.GetControlFromPosition(0, 0)!;
        form.Controls.Remove(nameLabel); nameLabel.Dispose();
        var ndiLabel = LegalUi.NdiLink(Point.Empty); ndiLabel.Text = "NDI® name"; ndiLabel.Anchor = AnchorStyles.Left;
        form.Controls.Add(ndiLabel, 0, 0);
        var input = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; input.Items.AddRange(["RTP H264", "RTP H265", "RTP MPEG-TS", "UDP MPEG-TS", "SDP file"]); input.SelectedItem = original.Input; Add("Input format", input);
        var address = TextField("Listen address", original.Address);
        var port = Number("Port", original.Port, 1024, 65534);
        var advanced = new CheckBox { Text = "Advanced" }; Add("", advanced); advanced.BackColor = BackColor;
        var payload = Number("RTP payload type", original.Payload, 0, 127);
        var nic = new ComboBox { Text = original.Interface, DropDownStyle = ComboBoxStyle.DropDown };
        nic.Items.Add("");
        foreach (var ip in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()).Distinct()) nic.Items.Add(ip);
        Add("Local interface", nic);
        var sdpPanel = new TableLayoutPanel { ColumnCount = 2 }; sdpPanel.ColumnStyles.Add(new(SizeType.Percent, 100)); sdpPanel.ColumnStyles.Add(new(SizeType.Absolute, 75));
        var sdp = new TextBox { Text = original.SdpPath, Dock = DockStyle.Fill }; var browse = new Button { Text = "Browse", Dock = DockStyle.Fill };
        browse.Click += (_, _) => { using var file = new OpenFileDialog { Filter = "SDP files|*.sdp|All files|*.*" }; if (file.ShowDialog(this) == DialogResult.OK) sdp.Text = file.FileName; };
        sdpPanel.Controls.Add(sdp, 0, 0); sdpPanel.Controls.Add(browse, 1, 0); Add("Encoder SDP file", sdpPanel);
        var fmtp = TextField("FMTP", original.Fmtp);
        void EnableInputs()
        {
            string type = input.Text; bool isSdp = type == "SDP file";
            void Show(Control control, bool visible)
            {
                int rowIndex = form.GetPositionFromControl(control).Row;
                control.Visible = visible; form.GetControlFromPosition(0, rowIndex)!.Visible = visible;
                form.RowStyles[rowIndex].Height = visible ? 42 : 0;
            }
            Show(address, !isSdp); Show(port, !isSdp); Show(sdpPanel, isSdp);
            Show(payload, advanced.Checked && type.StartsWith("RTP")); Show(fmtp, advanced.Checked && type.StartsWith("RTP"));
            Show(nic, advanced.Checked);
            ClientSize = new(620, 40 + (int)form.RowStyles.Cast<RowStyle>().Sum(style => style.Height));
        }
        advanced.CheckedChanged += (_, _) => EnableInputs();
        input.SelectedIndexChanged += (_, _) => { if (input.Text == "RTP MPEG-TS") payload.Value = 33; else if (payload.Value == 33) payload.Value = 96; EnableInputs(); }; EnableInputs();
        var info = new Label { Text = "Listen on 0.0.0.0, a local IP or a multicast group.\nFor separate RTP audio and video, use the encoder’s SDP file.", Dock = DockStyle.Fill, ForeColor = Color.LightSteelBlue, Padding = new(0, 8, 0, 0) };
        form.RowStyles.Add(new(SizeType.Absolute, 68)); form.Controls.Add(info, 0, row); form.SetColumnSpan(info, 2); row++;
        var actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var save = new Button { Text = "Save", Width = 90, Height = 34 }; var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 34 };
        foreach (var button in new[] { save, cancel, browse }) { button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(35, 48, 65); button.ForeColor = Color.WhiteSmoke; button.FlatAppearance.BorderColor = Color.FromArgb(65, 80, 98); }
        save.Click += (_, _) =>
        {
            try
            {
                var result = original with { Name = name.Text.Trim(), Input = input.Text, Address = address.Text.Trim(), Port = (int)port.Value, Payload = (int)payload.Value, Interface = nic.Text.Trim(), SdpPath = sdp.Text.Trim(), Fmtp = fmtp.Text.Trim() };
                result.Validate(); Result = result; DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Check settings"); }
        };
        actions.Controls.Add(save); actions.Controls.Add(cancel); form.RowStyles.Add(new(SizeType.Absolute, 42)); form.Controls.Add(actions, 0, row); form.SetColumnSpan(actions, 2);
        Controls.Add(form); AcceptButton = save; CancelButton = cancel; EnableInputs();
    }
}
