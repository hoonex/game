namespace PCWheelReceiver.Output;

public interface IGameFeedbackSource
{
    event Action<byte, byte>? GameFeedbackReceived;
}
