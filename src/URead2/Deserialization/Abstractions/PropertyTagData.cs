namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Tag-specific data read from tagged properties.
/// </summary>
public record PropertyTagData
{
    public string? StructType { get; init; }
    public string? EnumName { get; init; }
    public string? InnerType { get; init; }
    public string? ValueType { get; init; }
    public bool BoolValue { get; init; }
}
