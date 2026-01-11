using URead2.Assets.Models;
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

/// <summary>
/// Context for property reading operations.
/// </summary>
public class PropertyReadContext
{
    /// <summary>
    /// Name table from the asset.
    /// </summary>
    public required string[] NameTable { get; init; }

    /// <summary>
    /// Type resolver for schema lookup.
    /// </summary>
    public required ITypeResolver TypeResolver { get; init; }

    /// <summary>
    /// Import table for resolving object references to external objects.
    /// </summary>
    public AssetImport[]? Imports { get; init; }

    /// <summary>
    /// Export table for resolving object references to local objects.
    /// </summary>
    public AssetExport[]? Exports { get; init; }

    /// <summary>
    /// Resolves a package index to an ObjectReference.
    /// </summary>
    public ObjectReference ResolveReference(int packageIndex)
    {
        if (packageIndex == 0)
            return ObjectReference.Null;

        if (packageIndex < 0)
        {
            // Import reference
            int importIndex = -packageIndex - 1;
            if (Imports != null && importIndex >= 0 && importIndex < Imports.Length)
            {
                var import = Imports[importIndex];
                return new ObjectReference
                {
                    Type = import.ClassName,
                    Name = import.Name,
                    Path = import.PackageName,
                    Index = packageIndex
                };
            }
        }
        else
        {
            // Export reference
            int exportIndex = packageIndex - 1;
            if (Exports != null && exportIndex >= 0 && exportIndex < Exports.Length)
            {
                var export = Exports[exportIndex];
                return new ObjectReference
                {
                    Type = export.ClassName,
                    Name = export.Name,
                    Path = null, // Exports are local, path is this package
                    Index = packageIndex
                };
            }
        }

        // Couldn't resolve, return with just the index
        return new ObjectReference { Index = packageIndex };
    }
}

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
