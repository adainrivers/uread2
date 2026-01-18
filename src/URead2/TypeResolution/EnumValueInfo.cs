namespace URead2.TypeResolution;

/// <summary>
/// Serializable representation of an enum value.
/// </summary>
public sealed class EnumValueInfo
{
    public required string Name { get; init; }
    public long Value { get; init; }
}
