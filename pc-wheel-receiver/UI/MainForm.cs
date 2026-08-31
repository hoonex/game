using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PCWheelReceiver.Networking;
using PCWheelReceiver.Output;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.UI;

public sealed class MainForm : Form
{
    private const string VigemReleaseUrl = "https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0";
    private readonly string _configPath;
    private readonly ProtocolConfig _config;
    private readonly UdpReceiverService _receiver;
    private readonly IControllerOutput _output;
    private readonly System.Windows.Forms.Timer _uiTimer;

    private ReceiverSnapshot? _latestSnapshot;
    private readonly Label _connection = NewValueLabel("WAITING FOR PHONE");
    private readonly Label _endpoint = NewValueLabel("-");
    private readonly Label _packetRate = NewValueLabel("0 Hz");
    private readonly Label _packetLoss = NewValueLabel("0.00 %");
    private readonly Label _packetAge = NewValueLabel("-");
    private readonly Label _endianness = NewValueLabel("auto");
    private readonly Label _outputStatus = NewValueLabel("-");
    private readonly Label _steeringText = NewValueLabel("0.0 %");
    private readonly Label _throttleText = NewValueLabel("0.0 %");
    private readonly Label _brakeText = NewValueLabel("0.0 %");
    private readonly Label _clutchText = NewValueLabel("0.0 %");
    private readonly Label _handbrakeText = NewValueLabel("0.0 %");
    private readonly Label _errorText = NewValueLabel("None");

    private readonly ProgressBar _steering = NewBar(2000, 1000);
    private readonly ProgressBar _throttle = NewBar(1000, 0);
    private readonly ProgressBar _brake = NewBar(1000, 0);
    private readonly ProgressBar _clutch = NewBar(1000, 0);
    private readonly ProgressBar _handbrake = NewBar(1000, 0);

    private readonly CheckBox _invertSteering = NewCheckBox();
    private readonly NumericUpDown _steeringSensitivity = NewNumber(0.10m, 3.00m, 1.00m, 2, 0.05m);
    private readonly NumericUpDown _steeringDeadzone = NewNumber(0.000m, 0.500m, 0.015m, 3, 0.005m);
    private readonly NumericUpDown _steeringCurve = NewNumber(0.25m, 4.00m, 1.00m, 2, 0.05m);
    private readonly NumericUpDown _steeringSmoothing = NewNumber(0.00m, 0.95m, 0.00m, 2, 0.05m);
    private readonly NumericUpDown _pedalDeadzone = NewNumber(0.000m, 0.500m, 0.010m, 3, 0.005m);
    private readonly NumericUpDown _handbrakeThreshold = NewNumber(0.00m, 1.00m, 0.50m, 2, 0.05m);
    private readonly NumericUpDown _outputRateCap = NewNumber(0m, 500m, 0m, 0, 10m);

    public MainForm(
        string configPath,
        ProtocolConfig config,
        UdpReceiverService receiver,
        IControllerOutput output)
    {
        _configPath = configPath;
        _config = config;
        _receiver = receiver;
        _output = output;

        Text = "PC Wheel Receiver";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 700);
        Size = new Size(900, 900);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);

        LoadTuningControlsFromConfig();
        WireTuningEvents();
        Controls.Add(BuildLayout());

        _receiver.SnapshotUpdated += OnSnapshotUpdated;
        _uiTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _uiTimer.Tick += (_, _) => RenderSnapshot();
        _uiTimer.Start();

        Shown += (_, _) => StartReceiver();
        FormClosed += (_, _) =>
        {
            _uiTimer.Stop();
            _receiver.SnapshotUpdated -= OnSnapshotUpdated;
        };
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 6,
            AutoScroll = true,
        };
        for (var i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "PC Wheel Receiver",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 22f),
            Margin = new Padding(0, 0, 0, 4),
        };
        var subtitle = new Label
        {
            Text = $"Android target: this PC IPv4 : {_config.ListenPort}   |   Local IP: {GetLocalIpv4Text()}",
            AutoSize = true,
            ForeColor = Color.Silver,
            Margin = new Padding(0, 0, 0, 18),
        };
        var heading = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown };
        heading.Controls.Add(title);
        heading.Controls.Add(subtitle);
        root.Controls.Add(heading);

        root.Controls.Add(BuildStatusGrid());
        root.Controls.Add(BuildAnalogPanel());
        root.Controls.Add(BuildTuningPanel());
        root.Controls.Add(BuildActions());

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(810, 0),
            ForeColor = Color.DarkGray,
            Text = "Phone TX rate is measured from incoming UDP packets and shown above. Output rate cap only limits analog updates sent to the virtual Xbox controller; it does not change the Android sender frequency. Tuning is applied after UDP parsing, so the 36-byte protocol stays unchanged.",
            Margin = new Padding(0, 18, 0, 0),
        };
        root.Controls.Add(note);
        return root;
    }

    private Control BuildStatusGrid()
    {
        var grid = NewGrid();
        AddRow(grid, "Connection", _connection);
        AddRow(grid, "Phone endpoint", _endpoint);
        AddRow(grid, "Phone TX rate", _packetRate);
        AddRow(grid, "Packet loss", _packetLoss);
        AddRow(grid, "Last packet age", _packetAge);
        AddRow(grid, "Detected endian", _endianness);
        AddRow(grid, "Virtual controller", _outputStatus);
        AddRow(grid, "Last error", _errorText);
        return WrapGroup("Connection / Output", grid);
    }

    private Control BuildAnalogPanel()
    {
        var grid = NewGrid();
        AddAnalogRow(grid, "Steering (raw)", _steering, _steeringText);
        AddAnalogRow(grid, "Throttle (raw)", _throttle, _throttleText);
        AddAnalogRow(grid, "Brake (raw)", _brake, _brakeText);
        AddAnalogRow(grid, "Clutch (raw)", _clutch, _clutchText);
        AddAnalogRow(grid, "Handbrake (raw)", _handbrake, _handbrakeText);
        return WrapGroup("Live Android input", grid);
    }

    private Control BuildTuningPanel()
    {
        var grid = NewGrid();
        AddRow(grid, "Invert steering", _invertSteering);
        AddRow(grid, "Steering sensitivity", WithSuffix(_steeringSensitivity, "x"));
        AddRow(grid, "Steering deadzone", WithSuffix(_steeringDeadzone, "0–0.5"));
        AddRow(grid, "Steering curve", WithSuffix(_steeringCurve, "1 = linear, >1 softer center"));
        AddRow(grid, "Steering smoothing", WithSuffix(_steeringSmoothing, "0 = off, 0.95 = heavy"));
        AddRow(grid, "Pedal deadzone", WithSuffix(_pedalDeadzone, "throttle / brake / clutch"));
        AddRow(grid, "Handbrake A threshold", WithSuffix(_handbrakeThreshold, "0–1"));
        AddRow(grid, "Output rate cap", WithSuffix(_outputRateCap, "Hz   (0 = every packet)"));

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(3, 10, 3, 3),
        };
        var save = NewButton("Save tuning");
        save.Click += (_, _) => SaveTuning();
        var reset = NewButton("Reset tuning defaults");
        reset.Click += (_, _) => ResetTuningDefaults();
        actions.Controls.Add(save);
        actions.Controls.Add(reset);
        AddRow(grid, "", actions);

        return WrapGroup("Live tuning (applies immediately)", grid);
    }

    private Control BuildActions()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 0),
        };

        var openConfig = NewButton("Open protocol.json");
        openConfig.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = _configPath,
            UseShellExecute = true,
        });

        var copyIp = NewButton("Copy PC IPv4");
        copyIp.Click += (_, _) => Clipboard.SetText(GetFirstLocalIpv4() ?? "127.0.0.1");

        var installDriver = NewButton(_output.IsConnected ? "Xbox driver ready" : "Install Xbox driver");
        installDriver.Enabled = !_output.IsConnected;
        installDriver.Click += (_, _) => InstallVigemDriver();

        panel.Controls.Add(openConfig);
        panel.Controls.Add(copyIp);
        panel.Controls.Add(installDriver);
        return panel;
    }

    private void LoadTuningControlsFromConfig()
    {
        var output = _config.Output;
        _invertSteering.Checked = output.InvertSteering;
        SetValue(_steeringSensitivity, output.SteeringSensitivity);
        SetValue(_steeringDeadzone, output.SteeringDeadzone);
        SetValue(_steeringCurve, output.SteeringCurve);
        SetValue(_steeringSmoothing, output.SteeringSmoothing);
        SetValue(_pedalDeadzone, output.PedalDeadzone);
        SetValue(_handbrakeThreshold, output.HandbrakeButtonThreshold);
        SetValue(_outputRateCap, output.OutputRateCapHz);
    }

    private void WireTuningEvents()
    {
        _invertSteering.CheckedChanged += (_, _) => _config.Output.InvertSteering = _invertSteering.Checked;
        _steeringSensitivity.ValueChanged += (_, _) => _config.Output.SteeringSensitivity = (float)_steeringSensitivity.Value;
        _steeringDeadzone.ValueChanged += (_, _) => _config.Output.SteeringDeadzone = (float)_steeringDeadzone.Value;
        _steeringCurve.ValueChanged += (_, _) => _config.Output.SteeringCurve = (float)_steeringCurve.Value;
        _steeringSmoothing.ValueChanged += (_, _) => _config.Output.SteeringSmoothing = (float)_steeringSmoothing.Value;
        _pedalDeadzone.ValueChanged += (_, _) => _config.Output.PedalDeadzone = (float)_pedalDeadzone.Value;
        _handbrakeThreshold.ValueChanged += (_, _) => _config.Output.HandbrakeButtonThreshold = (float)_handbrakeThreshold.Value;
        _outputRateCap.ValueChanged += (_, _) => _config.Output.OutputRateCapHz = (int)_outputRateCap.Value;
    }

    private void SaveTuning()
    {
        try
        {
            _config.Save(_configPath);
            MessageBox.Show(
                "Current tuning values were saved to protocol.json.",
                "Tuning saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save tuning:\n{ex.Message}",
                "Save failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ResetTuningDefaults()
    {
        _config.Output.InvertSteering = false;
        _config.Output.SteeringSensitivity = 1.0f;
        _config.Output.SteeringDeadzone = 0.015f;
        _config.Output.SteeringCurve = 1.0f;
        _config.Output.SteeringSmoothing = 0.0f;
        _config.Output.PedalDeadzone = 0.01f;
        _config.Output.HandbrakeButtonThreshold = 0.5f;
        _config.Output.OutputRateCapHz = 0;
        LoadTuningControlsFromConfig();
    }

    private void InstallVigemDriver()
    {
        var result = MessageBox.Show(
            "PC Wheel currently uses the ViGEmBus compatibility backend to expose an Xbox 360 controller to games.\n\n" +
            "This will open an elevated PowerShell window and install ViGEmBus 1.22.0 through WinGet. " +
            "After installation, close and reopen PC Wheel Receiver.\n\nContinue?",
            "Install virtual controller driver",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result != DialogResult.Yes) return;

        try
        {
            const string command =
                "winget install --id ViGEm.ViGEmBus --exact --version 1.22.0 " +
                "--accept-package-agreements --accept-source-agreements";

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = true,
                Verb = "runas",
            });

            MessageBox.Show(
                "Finish the driver installation in the PowerShell window, then close and reopen PC Wheel Receiver. " +
                "The Virtual controller status should turn green after restart.",
                "Driver installation started",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var fallback = MessageBox.Show(
                $"Automatic installation could not be started:\n{ex.Message}\n\nOpen the official ViGEmBus 1.22.0 release page instead?",
                "Driver installation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (fallback == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = VigemReleaseUrl,
                    UseShellExecute = true,
                });
            }
        }
    }

    private void StartReceiver()
    {
        try
        {
            _receiver.Start();
            _outputStatus.Text = _output.Status;
        }
        catch (Exception ex)
        {
            _errorText.Text = ex.Message;
            MessageBox.Show(ex.Message, "UDP receiver could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSnapshotUpdated(object? sender, ReceiverSnapshot snapshot) => _latestSnapshot = snapshot;

    private void RenderSnapshot()
    {
        var snapshot = _latestSnapshot ?? _receiver.GetSnapshot();
        var ageMs = snapshot.LastPacketAt is null
            ? double.PositiveInfinity
            : (DateTimeOffset.Now - snapshot.LastPacketAt.Value).TotalMilliseconds;

        var connected = ageMs < 1000;
        _connection.Text = connected ? "PHONE CONNECTED" : "WAITING FOR PHONE";
        _connection.ForeColor = connected ? Color.LightGreen : Color.Gold;
        _endpoint.Text = snapshot.RemoteEndPoint?.ToString() ?? "-";
        _packetRate.Text = $"{snapshot.PacketRate:F1} Hz";
        _packetLoss.Text = $"{snapshot.LossPercent:F2} %   ({snapshot.LostPackets} lost)";
        _packetAge.Text = double.IsInfinity(ageMs) ? "-" : $"{ageMs:F0} ms";
        _endianness.Text = snapshot.Endianness;
        _outputStatus.Text = _output.Status;
        _outputStatus.ForeColor = _output.IsConnected ? Color.LightGreen : Color.Orange;
        _errorText.Text = snapshot.LastError ?? "None";

        var state = snapshot.State;
        if (state is null) return;

        _steering.Value = Math.Clamp((int)Math.Round((state.Steering + 1f) * 1000f), 0, 2000);
        _throttle.Value = ToBar(state.Throttle);
        _brake.Value = ToBar(state.Brake);
        _clutch.Value = ToBar(state.Clutch);
        _handbrake.Value = ToBar(state.Handbrake);

        _steeringText.Text = $"{state.Steering * 100f:+0.0;-0.0;0.0} %";
        _throttleText.Text = $"{state.Throttle * 100f:F1} %";
        _brakeText.Text = $"{state.Brake * 100f:F1} %";
        _clutchText.Text = $"{state.Clutch * 100f:F1} %";
        _handbrakeText.Text = $"{state.Handbrake * 100f:F1} %";
    }

    private static int ToBar(float value) => Math.Clamp((int)Math.Round(value * 1000f), 0, 1000);

    private static TableLayoutPanel NewGrid() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Padding = new Padding(8),
    };

    private static GroupBox WrapGroup(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.White,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 14),
        };
        group.Controls.Add(content);
        return group;
    }

    private static void AddRow(TableLayoutPanel grid, string name, Control value)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(NewNameLabel(name), 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static void AddAnalogRow(TableLayoutPanel grid, string name, ProgressBar bar, Label value)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        left.Controls.Add(NewNameLabel(name));
        left.Controls.Add(bar);
        grid.Controls.Add(left, 0, row);
        grid.Controls.Add(value, 1, row);
    }

    private static Control WithSuffix(Control control, string suffix)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        panel.Controls.Add(control);
        panel.Controls.Add(new Label
        {
            Text = suffix,
            AutoSize = true,
            ForeColor = Color.DarkGray,
            Margin = new Padding(8, 7, 3, 3),
        });
        return panel;
    }

    private static Label NewNameLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.Silver,
        Margin = new Padding(3, 7, 18, 7),
    };

    private static Label NewValueLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.White,
        Margin = new Padding(3, 7, 3, 7),
    };

    private static ProgressBar NewBar(int maximum, int value) => new()
    {
        Minimum = 0,
        Maximum = maximum,
        Value = value,
        Width = 430,
        Height = 14,
        Margin = new Padding(3, 2, 15, 8),
    };

    private static Button NewButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(10, 5, 10, 5),
        Margin = new Padding(0, 0, 10, 0),
    };

    private static CheckBox NewCheckBox() => new()
    {
        Text = "Enabled",
        AutoSize = true,
        ForeColor = Color.White,
        Margin = new Padding(3, 6, 3, 3),
    };

    private static NumericUpDown NewNumber(decimal minimum, decimal maximum, decimal value, int decimals, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        DecimalPlaces = decimals,
        Increment = increment,
        Width = 100,
        BackColor = Color.FromArgb(30, 30, 34),
        ForeColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(3, 3, 3, 3),
    };

    private static void SetValue(NumericUpDown control, float value)
    {
        var decimalValue = (decimal)value;
        control.Value = Math.Clamp(decimalValue, control.Minimum, control.Maximum);
    }

    private static void SetValue(NumericUpDown control, int value)
    {
        control.Value = Math.Clamp((decimal)value, control.Minimum, control.Maximum);
    }

    private static string GetLocalIpv4Text()
    {
        var addresses = GetLocalIpv4s();
        return addresses.Count == 0 ? "not found" : string.Join(", ", addresses);
    }

    private static string? GetFirstLocalIpv4() => GetLocalIpv4s().FirstOrDefault();

    private static List<string> GetLocalIpv4s()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .Distinct()
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
