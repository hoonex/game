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

            // Start in WEB SAFE mode. No virtual Xbox device exists until the user
            // explicitly enables Xbox mode, so desktop gamepad-to-mouse mappings cannot
            // move the pointer while using browser games.
            using var output = new SwitchableControllerOutput(config.Output);
            using var receiver = new UdpReceiverService(config, output);
            using var discovery = new DiscoveryService(config.ListenPort);

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
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "PC Wheel Receiver failed to start",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void AddGameOutputHealthUi(Form form, SwitchableControllerOutput output)
    {
        var banner = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(14, 10, 14, 10),
        };

        var status = new Label
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 10.5f),
            Margin = new Padding(0, 7, 12, 0),
        };

        var webButton = new Button
        {
            Text = "WEB SAFE",
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
        };

        var xboxButton = new Button
        {
            Text = "XBOX + RUMBLE",
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
        };

        var testButton = new Button
        {
            Text = "Test Xbox (joy.cpl)",
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
        };
        testButton.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = "joy.cpl",
            UseShellExecute = true,
        });

        var installButton = new Button
        {
            Text = "Install ViGEmBus",
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
        };
        installButton.Click += (_, _) => InstallVigemBus();

        void RefreshBanner()
        {
            var webSafe = output.Mode == ControllerOutputMode.WebSafe;
            banner.BackColor = webSafe
                ? Color.FromArgb(18, 72, 45)
                : Color.FromArgb(27, 62, 96);
            status.Text = webSafe
                ? "WEB SAFE ACTIVE  •  Xbox device disconnected  •  Wheel=A/D  Throttle=W  Brake/Reverse=S  Handbrake=Space"
                : $"XBOX MODE ACTIVE  •  {output.Status}";
            webButton.Enabled = !webSafe;
            xboxButton.Enabled = webSafe;
            testButton.Enabled = !webSafe;
        }

        webButton.Click += (_, _) =>
        {
            if (!output.TrySetMode(ControllerOutputMode.WebSafe, out var error))
            {
                MessageBox.Show(
                    error ?? "Could not switch to WEB SAFE mode.",
                    "Output mode change failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            RefreshBanner();
        };

        xboxButton.Click += (_, _) =>
        {
            if (!output.TrySetMode(ControllerOutputMode.Xbox360, out var error))
            {
                MessageBox.Show(
                    "Could not create the virtual Xbox 360 controller.\n\n" +
                    "WEB SAFE remains active, so the mouse-pointer problem stays blocked.\n\n" +
                    $"Driver/API error: {error}",
                    "Xbox output unavailable",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            RefreshBanner();
        };

        output.ModeChanged += (_, _) => RefreshBanner();

        banner.Controls.Add(status);
        banner.Controls.Add(webButton);
        banner.Controls.Add(xboxButton);
        banner.Controls.Add(testButton);
        banner.Controls.Add(installButton);
        RefreshBanner();

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
