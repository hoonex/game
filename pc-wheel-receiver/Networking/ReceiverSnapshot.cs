using System.Net;
using PCWheelReceiver.Models;

namespace PCWheelReceiver.Networking;

public sealed record ReceiverSnapshot(
    ControllerState? State,
    IPEndPoint? RemoteEndPoint,
    double PacketRate,
    long ReceivedPackets,
    long LostPackets,
    long InvalidPackets,
    DateTimeOffset? LastPacketAt,
    string Endianness,
    string? LastError)
{
    public double LossPercent => ReceivedPackets + LostPackets == 0
        ? 0
        : 100.0 * LostPackets / (ReceivedPackets + LostPackets);
}
