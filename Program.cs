namespace UdpNdi;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { Environment.ExitCode = SelfTest.Run(); return; }
        if (args.Contains("--ten-stream-test")) { Environment.ExitCode = SelfTest.RunTen(); return; }
        if (args.Contains("--format-change-test")) { Environment.ExitCode = SelfTest.Run(true); return; }
        if (args.Contains("--recovery-test")) { Environment.ExitCode = RecoveryTest.Run(); return; }
        if (args.Contains("--render-ui"))
        {
            Directory.CreateDirectory("test-results");
            using var form = new MainForm();
            form.Show(); Application.DoEvents();
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(0, 0, form.Width, form.Height));
            image.Save("test-results/dashboard.png");
            using var dialog = new SlotDialog(new SlotSettings(), 1);
            dialog.Show(); Application.DoEvents();
            using var dialogImage = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(dialogImage, new Rectangle(0, 0, dialog.Width, dialog.Height));
            dialogImage.Save("test-results/configure.png");
            var fields = dialog.Controls.OfType<TableLayoutPanel>().Single().Controls;
            fields.OfType<CheckBox>().Single().Checked = true;
            Application.DoEvents();
            using var advancedImage = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(advancedImage, new Rectangle(0, 0, dialog.Width, dialog.Height));
            advancedImage.Save("test-results/configure-advanced.png");
            fields.OfType<ComboBox>().Single(c => c.Items.Contains("SDP file")).SelectedItem = "SDP file";
            Application.DoEvents();
            using var sdpImage = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(sdpImage, new Rectangle(0, 0, dialog.Width, dialog.Height));
            sdpImage.Save("test-results/configure-sdp.png");
            var activity = form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == "Activity");
            activity.PerformClick(); Application.DoEvents();
            using var activityImage = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(activityImage, new Rectangle(0, 0, form.Width, form.Height));
            activityImage.Save("test-results/dashboard-activity.png");
            activity.PerformClick();
            return;
        }
        Application.Run(new MainForm());
    }
}
