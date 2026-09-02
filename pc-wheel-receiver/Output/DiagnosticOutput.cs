using PCWheelReceiver.Models;

namespace PCWheelReceiver.Output;

public sealed class DiagnosticOutput : IControllerOutput
{
    public DiagnosticOutput(string reason)
    {
        Status = reason;
    }

    public bool IsConnected => false;
    public string Status { get; }

    public void Apply(ControllerState state)
    {
        // Intentionally no-op. This mode lets networking/protocol diagnostics run
        // even when the ViGEmBus driver is not installed yet.
    }

    public void Dispose()
    {
    }
}
