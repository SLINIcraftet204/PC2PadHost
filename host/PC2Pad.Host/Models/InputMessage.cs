using System.Text.Json.Serialization;

namespace PC2Pad.Host.Models;

public sealed record InputMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("keyCode")]
    public int? KeyCode { get; init; }

    [JsonPropertyName("scanCode")]
    public int? ScanCode { get; init; }

    [JsonPropertyName("source")]
    public int? Source { get; init; }

    [JsonPropertyName("lx")]
    public float? LeftStickX { get; init; }

    [JsonPropertyName("ly")]
    public float? LeftStickY { get; init; }

    [JsonPropertyName("rx")]
    public float? RightStickX { get; init; }

    [JsonPropertyName("ry")]
    public float? RightStickY { get; init; }

    [JsonPropertyName("lt")]
    public float? LeftTrigger { get; init; }

    [JsonPropertyName("rt")]
    public float? RightTrigger { get; init; }

    [JsonPropertyName("hatX")]
    public float? HatX { get; init; }

    [JsonPropertyName("hatY")]
    public float? HatY { get; init; }


    [JsonPropertyName("dx")]
    public float? DeltaX { get; init; }

    [JsonPropertyName("dy")]
    public float? DeltaY { get; init; }

    [JsonPropertyName("button")]
    public string? Button { get; init; }

    [JsonPropertyName("wheel")]
    public int? Wheel { get; init; }

    [JsonPropertyName("time")]
    public long? ClientTimeUnixMs { get; init; }
}
