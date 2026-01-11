using URead2.Assets.Models;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.TypeMappings;
using URead2.IO;

namespace URead2.Deserialization;

/// <summary>
/// Reads type schemas from BlueprintGeneratedClass, UserDefinedStruct, and UserDefinedEnum exports.
/// </summary>
public class AssetSchemaReader : IAssetSchemaReader
{
    // Class names that define types
    private static readonly HashSet<string> TypeDefiningClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "BlueprintGeneratedClass",
        "WidgetBlueprintGeneratedClass",
        "AnimBlueprintGeneratedClass",
        "UserDefinedStruct",
        "ScriptStruct"
    };

    private static readonly HashSet<string> EnumDefiningClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "UserDefinedEnum",
        "Enum"
    };

    // Property class names and their corresponding EPropertyType
    private static readonly Dictionary<string, EPropertyType> PropertyClassMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BoolProperty"] = EPropertyType.BoolProperty,
        ["Int8Property"] = EPropertyType.Int8Property,
        ["ByteProperty"] = EPropertyType.ByteProperty,
        ["Int16Property"] = EPropertyType.Int16Property,
        ["UInt16Property"] = EPropertyType.UInt16Property,
        ["IntProperty"] = EPropertyType.IntProperty,
        ["UInt32Property"] = EPropertyType.UInt32Property,
        ["Int64Property"] = EPropertyType.Int64Property,
        ["UInt64Property"] = EPropertyType.UInt64Property,
        ["FloatProperty"] = EPropertyType.FloatProperty,
        ["DoubleProperty"] = EPropertyType.DoubleProperty,
        ["StrProperty"] = EPropertyType.StrProperty,
        ["NameProperty"] = EPropertyType.NameProperty,
        ["TextProperty"] = EPropertyType.TextProperty,
        ["ObjectProperty"] = EPropertyType.ObjectProperty,
        ["ClassProperty"] = EPropertyType.ObjectProperty,
        ["SoftObjectProperty"] = EPropertyType.SoftObjectProperty,
        ["SoftClassProperty"] = EPropertyType.SoftObjectProperty,
        ["WeakObjectProperty"] = EPropertyType.ObjectProperty,
        ["LazyObjectProperty"] = EPropertyType.ObjectProperty,
        ["InterfaceProperty"] = EPropertyType.InterfaceProperty,
        ["StructProperty"] = EPropertyType.StructProperty,
        ["ArrayProperty"] = EPropertyType.ArrayProperty,
        ["SetProperty"] = EPropertyType.SetProperty,
        ["MapProperty"] = EPropertyType.MapProperty,
        ["EnumProperty"] = EPropertyType.EnumProperty,
        ["DelegateProperty"] = EPropertyType.DelegateProperty,
        ["MulticastDelegateProperty"] = EPropertyType.MulticastDelegateProperty,
        ["MulticastInlineDelegateProperty"] = EPropertyType.MulticastDelegateProperty,
        ["MulticastSparseDelegateProperty"] = EPropertyType.MulticastDelegateProperty
    };

    private readonly ITypeResolver? _baseResolver;
    private readonly IPropertyReader? _propertyReader;

    /// <summary>
    /// Creates a schema reader.
    /// </summary>
    /// <param name="baseResolver">Base resolver for reading UProperty exports (optional, enables full schema reading).</param>
    /// <param name="propertyReader">Property reader for deserializing exports (optional, enables full schema reading).</param>
    public AssetSchemaReader(ITypeResolver? baseResolver = null, IPropertyReader? propertyReader = null)
    {
        _baseResolver = baseResolver;
        _propertyReader = propertyReader;
    }

    /// <summary>
    /// Reads all type schemas from an asset using quick metadata-based extraction.
    /// </summary>
    public void ReadSchemas(AssetMetadata metadata, Stream assetStream, AssetTypeResolver resolver)
    {
        // Group property exports by their outer (parent class/struct)
        var propertyGroups = GroupPropertiesByOuter(metadata);

        // Find type-defining exports and build schemas
        for (int i = 0; i < metadata.Exports.Length; i++)
        {
            var export = metadata.Exports[i];

            if (TypeDefiningClasses.Contains(export.ClassName))
            {
                var schema = BuildSchemaFromMetadata(metadata, i, propertyGroups);
                if (schema != null)
                {
                    resolver.RegisterSchema(schema);
                }
            }
            else if (EnumDefiningClasses.Contains(export.ClassName))
            {
                var enumDef = ReadEnumFromExport(metadata, assetStream, i);
                if (enumDef != null)
                {
                    resolver.RegisterEnum(enumDef);
                }
            }
        }
    }

    /// <summary>
    /// Reads a schema from an export using metadata only (quick but limited).
    /// </summary>
    public UsmapSchema? ReadSchemaFromExport(AssetMetadata metadata, Stream assetStream, int exportIndex)
    {
        if (exportIndex < 0 || exportIndex >= metadata.Exports.Length)
            return null;

        var export = metadata.Exports[exportIndex];
        if (!TypeDefiningClasses.Contains(export.ClassName))
            return null;

        var propertyGroups = GroupPropertiesByOuter(metadata);
        return BuildSchemaFromMetadata(metadata, exportIndex, propertyGroups);
    }

    /// <summary>
    /// Reads an enum from an export.
    /// </summary>
    public UsmapEnum? ReadEnumFromExport(AssetMetadata metadata, Stream assetStream, int exportIndex)
    {
        if (exportIndex < 0 || exportIndex >= metadata.Exports.Length)
            return null;

        var export = metadata.Exports[exportIndex];
        if (!EnumDefiningClasses.Contains(export.ClassName))
            return null;

        // For now, we need to read the export data to get enum values
        // This requires the property reader and base resolver
        if (_baseResolver == null || _propertyReader == null)
            return null;

        return ReadEnumExportData(metadata, assetStream, exportIndex);
    }

    /// <summary>
    /// Groups property exports by their outer export index.
    /// </summary>
    private static Dictionary<int, List<(int Index, AssetExport Export)>> GroupPropertiesByOuter(AssetMetadata metadata)
    {
        var groups = new Dictionary<int, List<(int, AssetExport)>>();

        for (int i = 0; i < metadata.Exports.Length; i++)
        {
            var export = metadata.Exports[i];

            if (PropertyClassMap.ContainsKey(export.ClassName) && export.OuterIndex >= 0)
            {
                if (!groups.TryGetValue(export.OuterIndex, out var list))
                {
                    list = [];
                    groups[export.OuterIndex] = list;
                }
                list.Add((i, export));
            }
        }

        return groups;
    }

    /// <summary>
    /// Builds a schema from export metadata without reading export data.
    /// This is quick but cannot determine inner types for containers or struct types.
    /// </summary>
    private static UsmapSchema? BuildSchemaFromMetadata(
        AssetMetadata metadata,
        int typeExportIndex,
        Dictionary<int, List<(int Index, AssetExport Export)>> propertyGroups)
    {
        var typeExport = metadata.Exports[typeExportIndex];

        if (!propertyGroups.TryGetValue(typeExportIndex, out var properties))
        {
            // No properties - still valid (empty class/struct)
            return new UsmapSchema(typeExport.Name, null, 0, new Dictionary<int, UsmapProperty>());
        }

        // Build properties - assign schema indices based on order (this is approximate)
        // For accurate indices, we'd need to read the actual property data
        var propDict = new Dictionary<int, UsmapProperty>();
        ushort schemaIndex = 0;

        foreach (var (index, propExport) in properties)
        {
            if (PropertyClassMap.TryGetValue(propExport.ClassName, out var propType))
            {
                var usmapPropType = new UsmapPropertyType(propType);

                // For containers and structs, we don't know the inner type from metadata alone
                // A full read would be needed

                var prop = new UsmapProperty(propExport.Name, schemaIndex, 1, usmapPropType);
                propDict[schemaIndex] = prop;
                schemaIndex++;
            }
        }

        // Try to find super type from imports if the class inherits from something
        string? superType = FindSuperType(metadata, typeExportIndex);

        return new UsmapSchema(typeExport.Name, superType, schemaIndex, propDict);
    }

    /// <summary>
    /// Attempts to find the super type of a class from its metadata.
    /// </summary>
    private static string? FindSuperType(AssetMetadata metadata, int exportIndex)
    {
        // This would require reading the export's SuperStruct property
        // For now, return null - a full implementation would read the class header
        return null;
    }

    /// <summary>
    /// Reads enum values from an enum export.
    /// </summary>
    private UsmapEnum? ReadEnumExportData(AssetMetadata metadata, Stream assetStream, int exportIndex)
    {
        var export = metadata.Exports[exportIndex];

        try
        {
            // Position stream at export data
            var offset = metadata.CookedHeaderSize + export.SerialOffset;
            assetStream.Position = offset;

            using var ar = new ArchiveReader(assetStream, leaveOpen: true);

            // Read enum data - format varies by UE version
            // Simplified: read count, then name-value pairs
            // This is a placeholder - actual implementation depends on UE version

            var context = new PropertyReadContext
            {
                NameTable = metadata.NameTable,
                TypeResolver = _baseResolver!
            };

            // Try to read as UserDefinedEnum
            // UserDefinedEnum has: Names (array of FEnumNameValue)
            var props = _propertyReader!.ReadProperties(ar, context, "UserDefinedEnum", isUnversioned: false);

            // Extract enum values from properties
            // The structure varies - might be "Names", "EnumNames", etc.
            // This is a simplified version
            var values = new Dictionary<long, string>();

            // For now, just create an empty enum definition
            // Full implementation would parse the Names array property
            return new UsmapEnum(export.Name, values);
        }
        catch
        {
            return null;
        }
    }
}
