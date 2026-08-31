using PCWheelReceiver.Networking;
using PCWheelReceiver.Output;
using PCWheelReceiver.Protocol;
using PCWheelReceiver.UI;

namespace PCWheelReceiver;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "protocol.json");
            var config = ProtocolConfig.Load(configPath);

            IControllerOutput output;
            try
            {
                output = new Xbox360Output(config.Output);
            }
            catch (Exception ex)
            {
                output = new DiagnosticOutput(
                    "Virtual Xbox output unavailable. Install ViGEmBus, then restart. " +
                    $"Driver/API error: {ex.Message}");
            }

            using (output)
            using (var receiver = new UdpReceiverService(config, output))
            {
                var form = new MainForm(configPath, config, receiver, output);
                EnableReliableVerticalScrolling(form);
                Application.Run(form);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "PC Wheel Receiver failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void EnableReliableVerticalScrolling(Form form)
    {
        // MainForm's root TableLayoutPanel used Dock=Fill + AutoScroll. That combination
        // can keep the layout constrained to the viewport, so WinForms never sees a
        // content height larger than the window and no useful vertical scrollbar appears.
        // Let the form own scrolling and let the root measure its natural content height.
        form.AutoScroll = true;
        form.AutoScrollMinSize = new Size(0, 1);

        if (form.Controls.Count == 0 || form.Controls[0] is not TableLayoutPanel root)
            return;

        root.AutoScroll = false;
        root.Dock = DockStyle.Top;
        root.AutoSize = true;
        root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        root.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        foreach (RowStyle rowStyle in root.RowStyles)
        {
            rowStyle.SizeType = SizeType.AutoSize;
            rowStyle.Height = 0;
        }

        void RefreshScrollExtent()
        {
            var scrollWidth = form.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            root.Width = Math.Max(form.ClientSize.Width - scrollWidth, form.MinimumSize.Width - scrollWidth);
            root.PerformLayout();
            form.AutoScrollMinSize = new Size(0, root.PreferredSize.Height);
        }

        form.Shown += (_, _) => RefreshScrollExtent();
        form.Resize += (_, _) => RefreshScrollExtent();
        root.Layout += (_, _) =>
        {
            var preferredHeight = root.PreferredSize.Height;
            if (form.AutoScrollMinSize.Height != preferredHeight)
                form.AutoScrollMinSize = new Size(0, preferredHeight);
        };
    }
}
