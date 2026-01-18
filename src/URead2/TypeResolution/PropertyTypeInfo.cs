namespace URead2.TypeResolution;

/// <summary>
/// Serializable representation of a property type.
/// </summary>
public sealed class PropertyTypeInfo
{
    public required string Kind { get; init; }
    public string? StructName { get; init; }
    public string? EnumName { get; init; }
    public PropertyTypeInfo? InnerType { get; init; }
    public PropertyTypeInfo? ValueType { get; init; }
}
