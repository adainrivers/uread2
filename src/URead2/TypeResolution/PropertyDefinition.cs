namespace URead2.TypeResolution;

/// <summary>
/// Definition of a property within a type.
/// </summary>
public sealed class PropertyDefinition
{
    /// <summary>
    /// Property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Schema index for unversioned serialization.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Array index for static arrays (0 for non-array).
    /// </summary>
    public int ArrayIndex { get; init; }

    /// <summary>
    /// Static array size (1 for non-array properties).
    /// </summary>
    public int ArraySize { get; init; } = 1;

    /// <summary>
    /// Property type information.
    /// </summary>
    public PropertyType Type { get; }

    public PropertyDefinition(string name, int index, PropertyType type)
    {
        Name = name;
        Index = index;
        Type = type;
    }

    public override string ToString() => $"{Name}[{Index}]: {Type}";
}
