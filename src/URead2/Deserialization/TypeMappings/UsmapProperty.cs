namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// A property definition from .usmap schema.
/// </summary>
public sealed class UsmapProperty
{
    /// <summary>
    /// Property name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Schema index for this property (used for unversioned serialization).
    /// </summary>
    public ushort SchemaIndex { get; }

    /// <summary>
    /// Array index for static arrays.
    /// </summary>
    public ushort ArrayIndex { get; init; }

    /// <summary>
    /// Static array size (1 for non-array properties).
    /// </summary>
    public byte ArraySize { get; }

    /// <summary>
    /// Property type information.
    /// </summary>
    public UsmapPropertyType PropertyType { get; }

    public UsmapProperty(string name, ushort schemaIndex, byte arraySize, UsmapPropertyType propertyType)
    {
        Name = name;
        SchemaIndex = schemaIndex;
        ArraySize = arraySize;
        PropertyType = propertyType;
    }

    public override string ToString() => $"{Name} [{SchemaIndex}]: {PropertyType}";
}
