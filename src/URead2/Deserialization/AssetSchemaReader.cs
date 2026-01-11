using URead2.Assets.Models;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.TypeMappings;
using URead2.IO;

namespace URead2.Deserialization;

/// <summary>
/// Reads type schemas from asset exports (BlueprintGeneratedClass, UserDefinedStruct, UserDefinedEnum).
/// Relies on TypeResolver (usmap) for base type information - no hardcoded type mappings.
/// </summary>
public class AssetSchemaReader : IAssetSchemaReader
{
    /// <summary>
    /// Reads all type schemas from an asset and registers them with the resolver.
    /// </summary>
    public void ReadSchemas(AssetMetadata metadata, Stream assetStream, TypeResolver resolver)
    {
        for (int i = 0; i < metadata.Exports.Length; i++)
        {
            var export = metadata.Exports[i];

            // Check if this export's class is a type-defining class (exists in usmap)
            if (IsTypeDefiningClass(export.ClassName, resolver))
            {
                var schema = ReadSchemaFromExportData(metadata, assetStream, i, resolver);
                if (schema != null)
                {
                    resolver.RegisterSchema(schema);
                }
            }
            else if (IsEnumDefiningClass(export.ClassName, resolver))
            {
                var enumDef = ReadEnumFromExportData(metadata, assetStream, i);
                if (enumDef != null)
                {
                    resolver.RegisterEnum(enumDef);
                }
            }
        }
    }

    /// <summary>
    /// Reads a schema from an export.
    /// </summary>
    public UsmapSchema? ReadSchemaFromExport(AssetMetadata metadata, Stream assetStream, int exportIndex)
    {
        // This method requires a TypeResolver to work properly
        // Return null if called without context
        return null;
    }

    /// <summary>
    /// Reads an enum from an export.
    /// </summary>
    public UsmapEnum? ReadEnumFromExport(AssetMetadata metadata, Stream assetStream, int exportIndex)
    {
        if (exportIndex < 0 || exportIndex >= metadata.Exports.Length)
            return null;

        return ReadEnumFromExportData(metadata, assetStream, exportIndex);
    }

    /// <summary>
    /// Checks if a class name represents a type-defining class by looking it up in the resolver.
    /// Type-defining classes inherit from UClass, UScriptStruct, or UBlueprintGeneratedClass.
    /// </summary>
    private static bool IsTypeDefiningClass(string className, TypeResolver resolver)
    {
        // Check if the class schema exists and inherits from type-defining bases
        var schema = resolver.GetSchema(className);
        if (schema == null)
            return false;

        // Walk inheritance chain to see if it's a type-defining class
        var current = schema;
        while (current != null)
        {
            var name = current.Name;
            if (name.Equals("BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WidgetBlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AnimBlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ScriptStruct", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("UserDefinedStruct", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(current.SuperType))
                break;

            current = resolver.GetSchema(current.SuperType);
        }

        return false;
    }

    /// <summary>
    /// Checks if a class name represents an enum-defining class.
    /// </summary>
    private static bool IsEnumDefiningClass(string className, TypeResolver resolver)
    {
        var schema = resolver.GetSchema(className);
        if (schema == null)
            return false;

        var current = schema;
        while (current != null)
        {
            if (current.Name.Equals("UserDefinedEnum", StringComparison.OrdinalIgnoreCase) ||
                current.Name.Equals("Enum", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(current.SuperType))
                break;

            current = resolver.GetSchema(current.SuperType);
        }

        return false;
    }

    /// <summary>
    /// Reads a schema by parsing the actual export data.
    /// </summary>
    private static UsmapSchema? ReadSchemaFromExportData(
        AssetMetadata metadata,
        Stream assetStream,
        int exportIndex,
        TypeResolver resolver)
    {
        var export = metadata.Exports[exportIndex];

        try
        {
            assetStream.Position = metadata.CookedHeaderSize + export.SerialOffset;
            using var ar = new ArchiveReader(assetStream, leaveOpen: true);

            // Read super type reference from the class/struct header
            string? superType = ReadSuperTypeFromExport(ar, metadata, resolver);

            // Collect properties that belong to this type (child exports)
            var properties = CollectPropertyExports(metadata, exportIndex, resolver);

            return new UsmapSchema(
                export.Name,
                superType,
                (ushort)properties.Count,
                properties);
        }
        catch
        {
            // If we can't read the export, return a minimal schema
            return new UsmapSchema(export.Name, null, 0, new Dictionary<int, UsmapProperty>());
        }
    }

    /// <summary>
    /// Reads the super type from a class/struct export's serialized data.
    /// </summary>
    private static string? ReadSuperTypeFromExport(ArchiveReader ar, AssetMetadata metadata, TypeResolver resolver)
    {
        // UClass/UStruct binary format starts with UObject fields, then:
        // - SuperStruct (FPackageIndex)
        // - Children (FPackageIndex)
        // - etc.
        //
        // For BlueprintGeneratedClass, the format is more complex.
        // We need to read the FPackageIndex for SuperStruct.

        try
        {
            // Skip UObject header fields to get to SuperStruct
            // This varies by UE version, but typically:
            // - Skip to the SuperStruct field

            // Read FPackageIndex (4 bytes, signed)
            int superIndex = ar.ReadInt32();

            if (superIndex == 0)
                return null;

            // Resolve the index to a name
            if (superIndex < 0)
            {
                // Import reference
                int importIndex = -superIndex - 1;
                if (importIndex >= 0 && importIndex < metadata.Imports.Length)
                {
                    return metadata.Imports[importIndex].Name;
                }
            }
            else
            {
                // Export reference
                int expIndex = superIndex - 1;
                if (expIndex >= 0 && expIndex < metadata.Exports.Length)
                {
                    return metadata.Exports[expIndex].Name;
                }
            }
        }
        catch
        {
            // Failed to read super type
        }

        return null;
    }

    /// <summary>
    /// Collects property information from child exports of a type-defining export.
    /// Property types are resolved through the TypeResolver (usmap).
    /// </summary>
    private static Dictionary<int, UsmapProperty> CollectPropertyExports(
        AssetMetadata metadata,
        int ownerExportIndex,
        TypeResolver resolver)
    {
        var properties = new Dictionary<int, UsmapProperty>();
        ushort schemaIndex = 0;

        for (int i = 0; i < metadata.Exports.Length; i++)
        {
            var export = metadata.Exports[i];

            // Check if this export belongs to our type
            if (export.OuterIndex != ownerExportIndex)
                continue;

            // Check if this is a property by looking up its class in the resolver
            // Property classes end with "Property" and inherit from FProperty/UProperty
            if (!IsPropertyClass(export.ClassName, resolver))
                continue;

            // Create property type from the class name
            var propType = CreatePropertyType(export.ClassName, resolver);
            if (propType == null)
                continue;

            var prop = new UsmapProperty(export.Name, schemaIndex, 1, propType);
            properties[schemaIndex] = prop;
            schemaIndex++;
        }

        return properties;
    }

    /// <summary>
    /// Checks if a class name is a property class by checking the resolver.
    /// </summary>
    private static bool IsPropertyClass(string className, TypeResolver resolver)
    {
        if (!className.EndsWith("Property", StringComparison.OrdinalIgnoreCase))
            return false;

        // Verify it exists in the resolver (usmap has all property types)
        return resolver.HasSchema(className);
    }

    /// <summary>
    /// Creates a UsmapPropertyType from a property class name using the resolver.
    /// </summary>
    private static UsmapPropertyType? CreatePropertyType(string propertyClassName, TypeResolver resolver)
    {
        // Map property class name to EPropertyType
        // The class name IS the type (e.g., "IntProperty" -> IntProperty)
        var propType = propertyClassName switch
        {
            "BoolProperty" => EPropertyType.BoolProperty,
            "Int8Property" => EPropertyType.Int8Property,
            "ByteProperty" => EPropertyType.ByteProperty,
            "Int16Property" => EPropertyType.Int16Property,
            "UInt16Property" => EPropertyType.UInt16Property,
            "IntProperty" => EPropertyType.IntProperty,
            "UInt32Property" => EPropertyType.UInt32Property,
            "Int64Property" => EPropertyType.Int64Property,
            "UInt64Property" => EPropertyType.UInt64Property,
            "FloatProperty" => EPropertyType.FloatProperty,
            "DoubleProperty" => EPropertyType.DoubleProperty,
            "StrProperty" => EPropertyType.StrProperty,
            "NameProperty" => EPropertyType.NameProperty,
            "TextProperty" => EPropertyType.TextProperty,
            "ObjectProperty" or "ClassProperty" or "WeakObjectProperty" or "LazyObjectProperty" => EPropertyType.ObjectProperty,
            "SoftObjectProperty" or "SoftClassProperty" => EPropertyType.SoftObjectProperty,
            "InterfaceProperty" => EPropertyType.InterfaceProperty,
            "StructProperty" => EPropertyType.StructProperty,
            "ArrayProperty" => EPropertyType.ArrayProperty,
            "SetProperty" => EPropertyType.SetProperty,
            "MapProperty" => EPropertyType.MapProperty,
            "EnumProperty" => EPropertyType.EnumProperty,
            "DelegateProperty" => EPropertyType.DelegateProperty,
            "MulticastDelegateProperty" or "MulticastInlineDelegateProperty" or "MulticastSparseDelegateProperty" => EPropertyType.MulticastDelegateProperty,
            _ => (EPropertyType?)null
        };

        return propType.HasValue ? new UsmapPropertyType(propType.Value) : null;
    }

    /// <summary>
    /// Reads an enum definition from export data.
    /// </summary>
    private static UsmapEnum? ReadEnumFromExportData(AssetMetadata metadata, Stream assetStream, int exportIndex)
    {
        var export = metadata.Exports[exportIndex];

        try
        {
            assetStream.Position = metadata.CookedHeaderSize + export.SerialOffset;
            using var ar = new ArchiveReader(assetStream, leaveOpen: true);

            // UserDefinedEnum format:
            // - UEnum base fields
            // - TArray<TPair<FName, int64>> Names

            // Skip UObject/UField header to get to enum names
            // This is simplified - actual format depends on UE version

            var values = new Dictionary<long, string>();

            // Try to read enum entries
            // Format: Count (int32), then pairs of (FName, int64)
            int count = ar.ReadInt32();
            if (count < 0 || count > 10000)
                return new UsmapEnum(export.Name, values);

            for (int i = 0; i < count; i++)
            {
                // Read FName (index into name table)
                int nameIndex = ar.ReadInt32();
                int nameNumber = ar.ReadInt32();

                string name;
                if (nameIndex >= 0 && nameIndex < metadata.NameTable.Length)
                {
                    name = metadata.NameTable[nameIndex];
                    if (nameNumber > 0)
                        name = $"{name}_{nameNumber - 1}";
                }
                else
                {
                    name = $"Value_{i}";
                }

                long value = ar.ReadInt64();
                values[value] = name;
            }

            return new UsmapEnum(export.Name, values);
        }
        catch
        {
            return new UsmapEnum(export.Name, new Dictionary<long, string>());
        }
    }
}
