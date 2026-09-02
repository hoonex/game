using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PCWheelReceiver.Networking;

public sealed class DiscoveryService : IDisposable
{
    public const int DiscoveryPort = 26761;
    public const string DiscoverRequest = "PCWHEEL_DISCOVER_V1";
    public const string ReceiverPrefix = "PCWHEEL_RECEIVER_V1";

    private readonly int _controllerPort;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;

    public DiscoveryService(int controllerPort)
    {
        _controllerPort = controllerPort;
    }

    public bool IsRunning => _receiveTask is { IsCompleted: false };

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DiscoveryService));
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _udp.Client.EnableBroadcast = true;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
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

                if (result.Buffer.Length == 0 || result.Buffer.Length > 128)
                    continue;

                var request = Encoding.UTF8.GetString(result.Buffer).Trim();
                if (!string.Equals(request, DiscoverRequest, StringComparison.Ordinal))
                    continue;

                var machineName = Sanitize(Environment.MachineName);
                var payload = Encoding.UTF8.GetBytes(
                    $"{ReceiverPrefix}|{machineName}|{_controllerPort}");

                try
                {
                    await _udp.SendAsync(payload, result.RemoteEndPoint, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Discovery is best-effort and must never interrupt the controller hot path.
                }
            }
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "PC";
        return value.Replace('|', '-').Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _udp?.Dispose();
        try
        {
            _receiveTask?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Socket cancellation exceptions are expected during shutdown.
        }
        _cts?.Dispose();
    }
}
