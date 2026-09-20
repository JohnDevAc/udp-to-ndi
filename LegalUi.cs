using System.Diagnostics;

namespace UdpNdi;

internal static class LegalUi
{
    public static LinkLabel NdiLink(Point location)
    {
        var link = new LinkLabel { Text = "NDI® · ndi.video", AutoSize = true, Location = location,
            Font = new Font("Segoe UI", 9), LinkColor = Color.LightSteelBlue, ActiveLinkColor = Color.MediumAquamarine };
        link.LinkClicked += (_, _) => Open(link.FindForm(), "https://ndi.video/");
        return link;
    }

    static void Open(IWin32Window? owner, string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(owner, ex.Message, "Unable to open link"); }
    }

    public static void Show(Form owner)
    {
        using var dialog = new Form { Text = "Licences and credits", StartPosition = FormStartPosition.CenterParent,
            Size = new(680, 480), MinimumSize = new(500, 350), Font = owner.Font, Icon = owner.Icon };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        var notices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        text.Text = "UDP to NDI — created by John Lightfoot\r\nCopyright (c) 2026 John Lightfoot. MIT License.\r\n\r\n";
        text.AppendText(File.Exists(notices) ? File.ReadAllText(notices) : "NDI® is a registered trademark of Vizrt NDI AB. This independent application is not sponsored or endorsed by NDI.\r\nFFmpeg is a separate program with its own licence. See the packaged notices and installer terms.");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new(6) };
        foreach (var (label, target) in new[] { ("NDI® website", "https://ndi.video/"), ("FFmpeg", "https://ffmpeg.org/"),
            ("Installer terms", Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-TERMS.txt")),
            ("MIT licence", Path.Combine(AppContext.BaseDirectory, "LICENSE")) })
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) => Open(dialog, target); actions.Controls.Add(button);
        }
        dialog.Controls.Add(text); dialog.Controls.Add(actions); dialog.ShowDialog(owner);
    }
}
