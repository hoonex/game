using System.Buffers.Binary;
using PCWheelReceiver.Models;

namespace PCWheelReceiver.Protocol;

public sealed class DynamicPacketParser
{
    private readonly ProtocolConfig _config;
    private bool? _lockedLittleEndian;

    public DynamicPacketParser(ProtocolConfig config)
    {
        _config = config;
        var configured = config.Endianness.Trim().ToLowerInvariant();
        if (configured == "little") _lockedLittleEndian = true;
        if (configured == "big") _lockedLittleEndian = false;
    }

    public string DetectedEndianness => _lockedLittleEndian switch
    {
        true => "little",
        false => "big",
        null => "auto (undetermined)",
    };

    public bool TryParse(ReadOnlySpan<byte> packet, out ControllerState state, out string? error)
    {
        state = default!;
        error = null;

        if (packet.Length != _config.ControllerPacketSize)
        {
            error = $"Expected {_config.ControllerPacketSize} bytes, received {packet.Length}.";
            return false;
        }

        if (_lockedLittleEndian is bool locked)
        {
            return TryParseCandidate(packet, locked, out state, out error);
        }

        var littleOk = TryParseCandidate(packet, true, out var little, out var littleError);
        var bigOk = TryParseCandidate(packet, false, out var big, out var bigError);

        if (littleOk && !bigOk)
        {
            _lockedLittleEndian = true;
            state = little;
            return true;
        }

        if (bigOk && !littleOk)
        {
            _lockedLittleEndian = false;
            state = big;
            return true;
        }

        if (littleOk && bigOk)
        {
            var preferLittle = _config.AutoPreferredEndianness.Equals("little", StringComparison.OrdinalIgnoreCase);
            state = preferLittle ? little : big;

            // Zero-valued packets can be valid in both byte orders. Keep auto mode unlocked
            // until a later packet contains enough information to distinguish the order.
            if (HasMeaningfulAnalogValue(state))
            {
                _lockedLittleEndian = preferLittle;
            }

            return true;
        }

        error = $"Neither endian interpretation was valid. little={littleError}; big={bigError}";
        return false;
    }

    private bool TryParseCandidate(
        ReadOnlySpan<byte> packet,
        bool littleEndian,
        out ControllerState state,
        out string? error)
    {
        state = default!;
        error = null;

        try
        {
            var f = _config.Fields;
            var candidate = new ControllerState(
                Sequence: checked((uint)ReadNumber(packet, f.Sequence, littleEndian)),
                Timestamp: checked((long)ReadNumber(packet, f.Timestamp, littleEndian)),
                Steering: (float)ReadNumber(packet, f.Steering, littleEndian),
                Throttle: (float)ReadNumber(packet, f.Throttle, littleEndian),
                Brake: (float)ReadNumber(packet, f.Brake, littleEndian),
                Clutch: (float)ReadNumber(packet, f.Clutch, littleEndian),
                Handbrake: (float)ReadNumber(packet, f.Handbrake, littleEndian),
                Buttons: checked((uint)ReadNumber(packet, f.Buttons, littleEndian)));

            if (!IsPlausible(candidate))
            {
                error = "Decoded analog values were outside plausible controller ranges.";
                return false;
            }

            state = ControllerState.Clamp(candidate);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException or InvalidDataException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool HasMeaningfulAnalogValue(ControllerState state) =>
        Math.Abs(state.Steering) > 0.0001f ||
        state.Throttle > 0.0001f ||
        state.Brake > 0.0001f ||
        state.Clutch > 0.0001f ||
        state.Handbrake > 0.0001f;

    private static bool IsPlausible(ControllerState state)
    {
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        return Finite(state.Steering) && state.Steering is >= -1.5f and <= 1.5f &&
               Finite(state.Throttle) && state.Throttle is >= -0.25f and <= 1.25f &&
               Finite(state.Brake) && state.Brake is >= -0.25f and <= 1.25f &&
               Finite(state.Clutch) && state.Clutch is >= -0.25f and <= 1.25f &&
               Finite(state.Handbrake) && state.Handbrake is >= -0.25f and <= 1.25f;
    }

    private static double ReadNumber(ReadOnlySpan<byte> packet, FieldSpec spec, bool littleEndian)
    {
        var type = spec.Type.Trim().ToLowerInvariant();
        var size = type switch
        {
            "int16" or "uint16" => 2,
            "int32" or "uint32" or "float32" => 4,
            "int64" or "uint64" or "float64" => 8,
            _ => throw new InvalidDataException($"Unsupported field type '{spec.Type}'."),
        };

        if (spec.Offset < 0 || spec.Offset + size > packet.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(spec.Offset),
                $"Field {spec.Type} at offset {spec.Offset} exceeds packet length {packet.Length}.");
        }

        var span = packet.Slice(spec.Offset, size);
        return type switch
        {
            "int16" => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span),
            "uint16" => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span),
            "int32" => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span),
            "uint32" => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span),
            "int64" => littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(span) : BinaryPrimitives.ReadInt64BigEndian(span),
            "uint64" => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(span) : BinaryPrimitives.ReadUInt64BigEndian(span),
            "float32" => BitConverter.Int32BitsToSingle(littleEndian
                ? BinaryPrimitives.ReadInt32LittleEndian(span)
                : BinaryPrimitives.ReadInt32BigEndian(span)),
            "float64" => BitConverter.Int64BitsToDouble(littleEndian
                ? BinaryPrimitives.ReadInt64LittleEndian(span)
                : BinaryPrimitives.ReadInt64BigEndian(span)),
            _ => throw new InvalidDataException($"Unsupported field type '{spec.Type}'."),
        };
    }
}
