using System.Diagnostics;
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
            using (var discovery = new DiscoveryService(config.ListenPort))
            {
                try
                {
                    discovery.Start();
                }
                catch
                {
                    // Discovery is optional. Manual IP connection and the 26760 controller path
                    // must remain available even if UDP 26761 is unavailable or blocked.
                }

                var form = new MainForm(configPath, config, receiver, output);
                EnableReliableVerticalScrolling(form);
                AddGameOutputHealthUi(form, output);
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

    private static void AddGameOutputHealthUi(Form form, IControllerOutput output)
    {
        var ready = output.IsConnected;
        var banner = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(14, 10, 14, 10),
            BackColor = ready ? Color.FromArgb(18, 72, 45) : Color.FromArgb(105, 48, 20),
        };

        banner.Controls.Add(new Label
        {
            AutoSize = true,
            Text = ready
                ? "GAME OUTPUT READY  •  Virtual Xbox 360 controller connected"
                : "GAME OUTPUT OFF  •  Phone input can move here, but games receive NOTHING until the Xbox virtual driver works",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(0, 7, 12, 0),
        });

        var testButton = new Button
        {
            Text = "Test in Windows (joy.cpl)",
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
        };
        testButton.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = "joy.cpl",
            UseShellExecute = true,
        });
        banner.Controls.Add(testButton);

        if (!ready)
        {
            var installButton = new Button
            {
                Text = "Install ViGEmBus",
                AutoSize = true,
                Padding = new Padding(8, 3, 8, 3),
            };
            installButton.Click += (_, _) => InstallVigemBus();
            banner.Controls.Add(installButton);

            form.Shown += (_, _) => MessageBox.Show(
                "Phone/UDP input may look completely normal even when game output is unavailable.\n\n" +
                "PC Wheel could not create the virtual Xbox 360 controller. Install ViGEmBus, restart PC Wheel Receiver, then open joy.cpl and verify that an Xbox 360 Controller appears and moves before starting the game.",
                "Game output is not active",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        form.Controls.Add(banner);
        banner.BringToFront();
    }

    private static void InstallVigemBus()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"winget install --id ViGEm.ViGEmBus --exact --version 1.22.0 --accept-package-agreements --accept-source-agreements\"",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not start the ViGEmBus installer: {ex.Message}",
                "Driver installation failed",
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
