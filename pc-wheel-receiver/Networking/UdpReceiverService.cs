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
    private const int FeedbackPort = 26762;
    private const int FeedbackHeartbeatMs = 40;
    private const int FeedbackRemoteFreshnessMs = 1000;

    private readonly ProtocolConfig _config;
    private readonly IControllerOutput _output;
    private readonly IGameFeedbackSource? _feedbackSource;
    private readonly DynamicPacketParser _parser;
    private readonly object _sync = new();
    private readonly Stopwatch _rateWindow = Stopwatch.StartNew();
    private readonly Stopwatch _snapshotWindow = Stopwatch.StartNew();
    private readonly SemaphoreSlim _feedbackSignal = new(0, 1);

    private UdpClient? _udp;
    private UdpClient? _feedbackUdp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _feedbackTask;
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
    private byte _feedbackLargeMotor;
    private byte _feedbackSmallMotor;
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

        // Keep game feedback off the 26760 receive socket so rumble traffic can never
        // contend with the steering hot path. The destination remains Android UDP 26762.
        _feedbackUdp = new UdpClient();
        _feedbackUdp.Client.SendBufferSize = 64 * 1024;

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _feedbackTask = Task.Run(() => FeedbackLoopAsync(_cts.Token));
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
        if (_disposed) return;

        lock (_sync)
        {
            _feedbackLargeMotor = largeMotor;
            _feedbackSmallMotor = smallMotor;
        }

        // FeedbackReceived is change-driven. Wake the relay immediately for low latency;
        // the background loop also repeats non-zero state as a lightweight heartbeat.
        if (_feedbackSignal.CurrentCount == 0)
        {
            try
            {
                _feedbackSignal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task FeedbackLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool signaled;
                try
                {
                    signaled = await _feedbackSignal
                        .WaitAsync(FeedbackHeartbeatMs, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                UdpClient? udp;
                IPEndPoint? remote;
                DateTimeOffset? lastPacketAt;
                byte largeMotor;
                byte smallMotor;

                lock (_sync)
                {
                    udp = _feedbackUdp;
                    remote = _remote is null
                        ? null
                        : new IPEndPoint(_remote.Address, FeedbackPort);
                    lastPacketAt = _lastPacketAt;
                    largeMotor = _feedbackLargeMotor;
                    smallMotor = _feedbackSmallMotor;
                }

                if (udp is null || remote is null || lastPacketAt is null)
                {
                    continue;
                }

                // Do not keep relaying to a phone that has stopped sending controller
                // packets. Android independently stops stale feedback as an extra guard.
                if ((DateTimeOffset.Now - lastPacketAt.Value).TotalMilliseconds >
                    FeedbackRemoteFreshnessMs)
                {
                    continue;
                }

                // Zero feedback only needs one immediate packet. Non-zero feedback is
                // repeated every 40 ms so Android can distinguish a live sustained rumble
                // from a lost final packet and stop safely within its freshness window.
                if (!signaled && largeMotor == 0 && smallMotor == 0)
                {
                    continue;
                }

                await SendFeedbackAsync(
                        udp,
                        remote,
                        BuildFeedbackPacket(largeMotor, smallMotor))
                    .ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static byte[] BuildFeedbackPacket(byte largeMotor, byte smallMotor) =>
        new byte[FeedbackPacketSize]
        {
            (byte)'P', (byte)'C', (byte)'F', (byte)'B',
            1,
            largeMotor,
            smallMotor,
            0,
        };

    private static async Task SendFeedbackAsync(
        UdpClient udp,
        IPEndPoint remote,
        byte[] packet)
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

    private void SendFeedbackStopBestEffort()
    {
        UdpClient? udp;
        IPEndPoint? remote;
        lock (_sync)
        {
            udp = _feedbackUdp;
            remote = _remote is null
                ? null
                : new IPEndPoint(_remote.Address, FeedbackPort);
            _feedbackLargeMotor = 0;
            _feedbackSmallMotor = 0;
        }

        if (udp is null || remote is null) return;

        try
        {
            udp.Send(BuildFeedbackPacket(0, 0), FeedbackPacketSize, remote);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
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

                // Keep the latest phone IP available before submitting the virtual controller report,
                // so a game rumble notification can be relayed immediately on a separate UDP path.
                lock (_sync)
                {
                    _remote = result.RemoteEndPoint;
                    _lastPacketAt = DateTimeOffset.Now;
                }

                string? outputError = null;
                try
                {
                    _output.Apply(state);
                }
                catch (Exception ex)
                {
                    outputError = $"Virtual controller output failed: {ex.Message}";
                }

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

        // Stop the phone immediately on normal receiver shutdown rather than waiting for
        // Android's stale-feedback watchdog.
        SendFeedbackStopBestEffort();

        _cts?.Cancel();
        _udp?.Dispose();
        _feedbackUdp?.Dispose();
        try
        {
            _receiveTask?.Wait(TimeSpan.FromSeconds(1));
            _feedbackTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _cts?.Dispose();
        _feedbackSignal.Dispose();
    }
}
