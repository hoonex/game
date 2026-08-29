using System.Text.Json;

namespace PCWheelReceiver.Protocol;

public sealed class ProtocolConfig
{
    public int ListenPort { get; set; } = 26760;
    public int ControllerPacketSize { get; set; } = 36;
    public int PingPacketSize { get; set; } = 12;
    public bool EchoPingPackets { get; set; } = true;
    public string Endianness { get; set; } = "auto";
    public string AutoPreferredEndianness { get; set; } = "big";
    public FieldLayout Fields { get; set; } = new();
    public OutputConfig Output { get; set; } = new();

    public static ProtocolConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Protocol configuration was not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<ProtocolConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        return config ?? throw new InvalidDataException("protocol.json could not be parsed.");
    }
}

public sealed class FieldLayout
{
    public FieldSpec Sequence { get; set; } = new() { Offset = 0, Type = "uint32" };
    public FieldSpec Timestamp { get; set; } = new() { Offset = 4, Type = "int64" };
    public FieldSpec Steering { get; set; } = new() { Offset = 12, Type = "float32" };
    public FieldSpec Throttle { get; set; } = new() { Offset = 16, Type = "float32" };
    public FieldSpec Brake { get; set; } = new() { Offset = 20, Type = "float32" };
    public FieldSpec Clutch { get; set; } = new() { Offset = 24, Type = "float32" };
    public FieldSpec Handbrake { get; set; } = new() { Offset = 28, Type = "float32" };
    public FieldSpec Buttons { get; set; } = new() { Offset = 32, Type = "uint32" };
}

public sealed class FieldSpec
{
    public int Offset { get; set; }
    public string Type { get; set; } = "float32";
}

public sealed class OutputConfig
{
    public int ShiftUpBit { get; set; } = 0;
    public int ShiftDownBit { get; set; } = 1;
    public int HornBit { get; set; } = 2;
    public int CameraBit { get; set; } = 3;
    public int ResetBit { get; set; } = 4;
    public float HandbrakeButtonThreshold { get; set; } = 0.5f;
    public bool MapClutchToRightStickY { get; set; } = true;
}
