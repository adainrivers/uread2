namespace URead2;

/// <summary>
/// Runtime configuration for container reading.
/// </summary>
public record RuntimeConfig
{
    public required string PaksPath { get; init; }
    public string? UsmapPath { get; init; }
    public string? TypeRegistryJsonPath { get; init; }
    public byte[]? AesKey { get; init; }
}
