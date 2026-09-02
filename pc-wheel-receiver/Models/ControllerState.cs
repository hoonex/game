namespace PCWheelReceiver.Models;

public sealed record ControllerState(
    uint Sequence,
    long Timestamp,
    float Steering,
    float Throttle,
    float Brake,
    float Clutch,
    float Handbrake,
    uint Buttons)
{
    public static ControllerState Clamp(ControllerState value) => value with
    {
        Steering = Math.Clamp(value.Steering, -1f, 1f),
        Throttle = Math.Clamp(value.Throttle, 0f, 1f),
        Brake = Math.Clamp(value.Brake, 0f, 1f),
        Clutch = Math.Clamp(value.Clutch, 0f, 1f),
        Handbrake = Math.Clamp(value.Handbrake, 0f, 1f),
    };
}
