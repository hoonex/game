using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PCWheelReceiver.Models;
using PCWheelReceiver.Output;
using PCWheelReceiver.Protocol;

namespace PCWheelReceiver.Networking;

public sealed class UdpReceiverService : IDisposable
{
    private readonly ProtocolConfig _config;
    private readonly IControllerOutput _output;
    private readonly DynamicPacketParser _parser;
    private readonly object _sync = new();
    private readonly Stopwatch _rateWindow = Stopwatch.StartNew();

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private long _received;
    private long _lost;
    private long _invalid;
    private long _windowPackets;
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
                        PublishLocked();
                    }
                    continue;
                }

                // Android PC Wheel latency protocol: every valid-size 36-byte controller datagram
                // is acknowledged by echoing its first 12 bytes (sequence + timestamp) verbatim.
                // Echo before parsing/output so RTT measurement is not inflated by controller work.
                if (_config.EchoPingPackets && _config.PingPacketSize > 0 &&
                    _config.PingPacketSize <= result.Buffer.Length)
                {
                    await _udp.SendAsync(
                        result.Buffer.AsMemory(0, _config.PingPacketSize),
                        result.RemoteEndPoint,
                        cancellationToken);
                }

                if (!_parser.TryParse(result.Buffer, out var state, out var parseError))
                {
                    lock (_sync)
                    {
                        _invalid++;
                        _lastError = parseError;
                        PublishLocked();
                    }
                    continue;
                }

                try
                {
                    _output.Apply(state);
                }
                catch (Exception ex)
                {
                    lock (_sync)
                    {
                        _lastError = $"Virtual controller output failed: {ex.Message}";
                        PublishLocked();
                    }
                    continue;
                }

                lock (_sync)
                {
                    UpdateSequenceLoss(state.Sequence);
                    _received++;
                    _windowPackets++;
                    _state = state;
                    _remote = result.RemoteEndPoint;
                    _lastPacketAt = DateTimeOffset.Now;
                    _lastError = null;
                    UpdateRate();
                    PublishLocked();
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
                PublishLocked();
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

    private void PublishLocked()
    {
        var snapshot = BuildSnapshot();
        SnapshotUpdated?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

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
