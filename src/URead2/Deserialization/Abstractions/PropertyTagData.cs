namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Tag-specific data read from tagged properties.
/// </summary>
public readonly struct PropertyTagData
{
    public static readonly PropertyTagData Empty = default;

    public string? StructType { get; init; }
    public string? EnumName { get; init; }
    public string? InnerType { get; init; }
    public string? ValueType { get; init; }
    public bool BoolValue { get; init; }
}
