using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PCWheelReceiver.Networking;
using PCWheelReceiver.Output;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.UI;

public sealed class MainForm : Form
{
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
        MinimumSize = new Size(760, 650);
        Size = new Size(860, 720);
        BackColor = Color.FromArgb(18, 18, 20);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);

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
            RowCount = 5,
            AutoScroll = true,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        root.Controls.Add(BuildActions());

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = Color.DarkGray,
            Text = "The network receiver processes packets independently from this UI. The dashboard refreshes at ~30 Hz while UDP input can remain at 100 Hz.",
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
        AddRow(grid, "Packet rate", _packetRate);
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
        AddAnalogRow(grid, "Steering", _steering, _steeringText);
        AddAnalogRow(grid, "Throttle", _throttle, _throttleText);
        AddAnalogRow(grid, "Brake", _brake, _brakeText);
        AddAnalogRow(grid, "Clutch", _clutch, _clutchText);
        AddAnalogRow(grid, "Handbrake", _handbrake, _handbrakeText);
        return WrapGroup("Live controller state", grid);
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

        panel.Controls.Add(openConfig);
        panel.Controls.Add(copyIp);
        return panel;
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
