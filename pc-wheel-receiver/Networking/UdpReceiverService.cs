using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PCWheelReceiver.Models;
using PCWheelReceiver.Output;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Networking;

public sealed class UdpReceiverService : IDisposable
{
    private const int FeedbackPacketSize = 8;

    private readonly ProtocolConfig _config;
    private readonly IControllerOutput _output;
    private readonly IGameFeedbackSource? _feedbackSource;
    private readonly DynamicPacketParser _parser;
    private readonly object _sync = new();
    private readonly Stopwatch _rateWindow = Stopwatch.StartNew();
    private readonly Stopwatch _snapshotWindow = Stopwatch.StartNew();

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private long _received;
    private long _lost;
    private long _invalid;
    private long _windowPackets;
    private long _lastSnapshotPublishTicks;
    private double _packetRate;
    private uint? _lastSequence;
    private ControllerState? _state;
    private IPEndPoint? _remote;
    private DateTimeOffset? _lastPacketAt;
    private string? _lastError;
    private bool _disposed;

    public UdpReceiverService(ProtocolConfig config, IControllerOutput output)
    {
        _config = config;
        _output = output;
        _parser = new DynamicPacketParser(config);
        _feedbackSource = output as IGameFeedbackSource;
        if (_feedbackSource is not null)
        {
            _feedbackSource.GameFeedbackReceived += OnGameFeedbackReceived;
        }
    }

    public event EventHandler<ReceiverSnapshot>? SnapshotUpdated;

    public bool IsRunning => _receiveTask is { IsCompleted: false };

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UdpReceiverService));
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _config.ListenPort));
        _udp.Client.ReceiveBufferSize = 1 << 20;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public ReceiverSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return BuildSnapshot();
        }
    }

    private void OnGameFeedbackReceived(byte largeMotor, byte smallMotor)
    {
        UdpClient? udp;
        IPEndPoint? remote;
        lock (_sync)
        {
            udp = _udp;
            remote = _remote;
        }

        if (_disposed || udp is null || remote is null) return;

        // Separate 8-byte PCFB packet. Existing 36-byte controller packets and 12-byte RTT
        // echoes remain byte-for-byte compatible with older Android/receiver versions.
        var packet = new byte[FeedbackPacketSize]
        {
            (byte)'P', (byte)'C', (byte)'F', (byte)'B',
            1,
            largeMotor,
            smallMotor,
            0,
        };
        _ = SendFeedbackAsync(udp, remote, packet);
    }

    private static async Task SendFeedbackAsync(UdpClient udp, IPEndPoint remote, byte[] packet)
    {
        try
        {
            await udp.SendAsync(packet, packet.Length, remote).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Receiver is shutting down.
        }
        catch (SocketException)
        {
            // Rumble is best-effort and must never stall or fail the steering hot path.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udp!.ReceiveAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Backward-compatible support for a standalone 12-byte ping datagram.
                if (result.Buffer.Length == _config.PingPacketSize && _config.EchoPingPackets)
                {
                    await _udp.SendAsync(result.Buffer, result.Buffer.Length, result.RemoteEndPoint);
                    continue;
                }

                if (result.Buffer.Length != _config.ControllerPacketSize)
                {
                    lock (_sync)
                    {
                        _invalid++;
                        _lastError = $"Ignored UDP packet with unexpected size {result.Buffer.Length}.";
                        PublishLocked(force: true);
                    }
                    continue;
                }

                if (!_parser.TryParse(result.Buffer, out var state, out var parseError))
                {
                    lock (_sync)
                    {
                        _invalid++;
                        _lastError = parseError;
                        PublishLocked(force: true);
                    }
                    continue;
                }

                // Keep the latest phone endpoint available to the ViGEm feedback callback before
                // applying output, so a game rumble arriving immediately can be relayed back.
                lock (_sync)
                {
                    _remote = result.RemoteEndPoint;
                }

                // Real-time control gets first priority. Previously the receive loop awaited
                // the RTT header echo before applying the controller state, which put telemetry
                // work directly in front of the steering hot path.
                string? outputError = null;
                try
                {
                    _output.Apply(state);
                }
                catch (Exception ex)
                {
                    outputError = $"Virtual controller output failed: {ex.Message}";
                }

                // Echo the Android sequence + timestamp only after the virtual-controller update.
                // RTT now includes the tiny receiver processing cost, while steering is no longer
                // delayed by an awaited UDP send.
                if (_config.EchoPingPackets && _config.PingPacketSize > 0 &&
                    _config.PingPacketSize <= result.Buffer.Length)
                {
                    try
                    {
                        await _udp.SendAsync(
                            result.Buffer.AsMemory(0, _config.PingPacketSize),
                            result.RemoteEndPoint,
                            cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        outputError ??= $"RTT echo failed: {ex.Message}";
                    }
                }

                lock (_sync)
                {
                    UpdateSequenceLoss(state.Sequence);
                    _received++;
                    _windowPackets++;
                    _state = state;
                    _remote = result.RemoteEndPoint;
                    _lastPacketAt = DateTimeOffset.Now;
                    _lastError = outputError;
                    UpdateRate();

                    // The WinForms UI renders at roughly 30 Hz. Publishing a new snapshot for
                    // every ~100 Hz Android packet only creates extra allocations/event work and
                    // slows the loop that should be receiving the next steering sample.
                    PublishLocked(force: outputError is not null);
                }
            }
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastError = $"UDP receiver stopped: {ex.Message}";
                PublishLocked(force: true);
            }
        }
    }

    private void UpdateSequenceLoss(uint sequence)
    {
        if (_lastSequence is uint previous)
        {
            // The Android sequence is a signed Int, but the on-wire representation is exactly
            // 32 bits. Treating it as uint here preserves modulo-2^32 progression across the
            // Int.MAX_VALUE -> Int.MIN_VALUE wrap without a special case.
            var delta = unchecked(sequence - previous);
            if (delta > 1 && delta < 1_000_000)
            {
                _lost += delta - 1;
            }
        }

        _lastSequence = sequence;
    }

    private void UpdateRate()
    {
        var elapsed = _rateWindow.Elapsed.TotalSeconds;
        if (elapsed < 1.0) return;

        _packetRate = _windowPackets / elapsed;
        _windowPackets = 0;
        _rateWindow.Restart();
    }

    private ReceiverSnapshot BuildSnapshot() => new(
        _state,
        _remote,
        _packetRate,
        _received,
        _lost,
        _invalid,
        _lastPacketAt,
        _parser.DetectedEndianness,
        _lastError);

    private void PublishLocked(bool force = false)
    {
        var handler = SnapshotUpdated;
        if (handler is null) return;

        var now = _snapshotWindow.ElapsedTicks;
        var minTicks = Math.Max(1L, Stopwatch.Frequency / 30);
        if (!force && now - _lastSnapshotPublishTicks < minTicks) return;

        _lastSnapshotPublishTicks = now;
        handler.Invoke(this, BuildSnapshot());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_feedbackSource is not null)
        {
            _feedbackSource.GameFeedbackReceived -= OnGameFeedbackReceived;
        }
        _cts?.Cancel();
        _udp?.Dispose();
        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown path: socket cancellation exceptions are expected.
        }
        _cts?.Dispose();
    }
}
