namespace URead2.TypeResolution;

/// <summary>
/// Serializable representation of a property definition.
/// </summary>
public sealed class PropertyInfo
{
    public required string Name { get; init; }
    public int Index { get; init; }
    public int ArrayIndex { get; init; }
    public int ArraySize { get; init; }
    public required PropertyTypeInfo Type { get; init; }
}
