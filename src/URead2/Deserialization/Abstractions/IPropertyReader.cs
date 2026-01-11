using URead2.Deserialization.Properties;
using URead2.Deserialization.TypeMappings;
using URead2.IO;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Reads properties from serialized UObject data.
/// </summary>
public interface IPropertyReader
{
    /// <summary>
    /// Reads all properties from an export's serialized data.
    /// </summary>
    /// <param name="ar">Archive reader positioned at start of property data.</param>
    /// <param name="context">Context providing name table and type resolution.</param>
    /// <param name="className">Class name of the object (for schema lookup).</param>
    /// <param name="isUnversioned">True if using unversioned serialization.</param>
    /// <returns>A property bag containing all read properties.</returns>
    PropertyBag ReadProperties(
        ArchiveReader ar,
        PropertyReadContext context,
        string className,
        bool isUnversioned = false);

    /// <summary>
    /// Reads a single property value by type name (for tagged properties).
    /// </summary>
    PropertyValue ReadPropertyValue(
        ArchiveReader ar,
        PropertyReadContext context,
        string typeName,
        PropertyTagData tagData,
        int size,
        ReadContext readContext);

    /// <summary>
    /// Reads a single property value by property type (for unversioned properties).
    /// </summary>
    PropertyValue ReadPropertyByType(
        ArchiveReader ar,
        PropertyReadContext context,
        UsmapPropertyType propType,
        ReadContext readContext);
}
