using PCWheelReceiver.Models;

namespace PCWheelReceiver.Output;

public interface IControllerOutput : IDisposable
{
    bool IsConnected { get; }
    string Status { get; }
    void Apply(ControllerState state);
}
