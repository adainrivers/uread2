using URead2.Assets.Models;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Fields;
using URead2.Deserialization.Properties;
using URead2.IO;
using URead2.TypeResolution;

namespace URead2.Deserialization.TypeReaders;

/// <summary>
/// Reads BlueprintGeneratedClass exports to extract property definitions.
/// This reader extracts ChildProperties from UStruct serialization.
/// </summary>
public class BlueprintClassReader : ITypeReader
{
    /// <summary>
    /// Reads a BlueprintGeneratedClass export and returns its properties.
    /// The key data we want is the ChildProperties array which defines the class's fields.
    /// </summary>
    public PropertyBag Read(ArchiveReader ar, PropertyReadContext context, AssetExport export)
    {
        var bag = new PropertyBag();

        // First read the regular tagged properties
        // These are the UObject properties like NumReplicatedProperties, DynamicBindingObjects, etc.
        // We skip these for now and focus on ChildProperties

        // For now, just return empty - the real extraction happens via ReadClassProperties
        return bag;
    }

    /// <summary>
    /// Reads ChildProperties from a UStruct/UClass export.
    /// This is the core method for extracting BP type property definitions.
    /// </summary>
    public static FProperty[]? ReadClassProperties(
        ArchiveReader ar,
        AssetMetadata metadata,
        AssetExport export)
    {
        // Create context for resolving package indices
        var context = new FFieldContext
        {
            NameTable = metadata.NameTable,
            Imports = metadata.Imports,
            Exports = metadata.Exports
        };

        try
        {
            // UStruct serialization format (after UObject properties):
            // 0. Unknown 4 bytes (possibly padding or version-specific)
            // 1. SuperStruct (FPackageIndex) - 4 bytes
            // 2. Children array (FPackageIndex[])
            // 3. ChildProperties array (FProperty[])
            // 4. BytecodeBufferSize (int)
            // 5. SerializedScriptSize (int)
            // 6. Script bytecode

            // Skip unknown 4 bytes before SuperStruct
            if (!ar.TryReadInt32(out _))
                return null;

            // Skip SuperStruct
            if (!ar.TryReadInt32(out _))
                return null;

            // Read Children array (FPackageIndex[])
            if (!ar.TryReadInt32(out var childCount))
                return null;

            if (childCount > 0 && childCount < 65536)
            {
                // Skip the children array (FPackageIndex = 4 bytes each)
                if (!ar.TrySkip(childCount * 4))
                    return null;
            }

            // Read ChildProperties array - this is what we want!
            var properties = FProperty.ReadPropertyArray(ar, context);
            return properties;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to read class properties for {ClassName}", export.Name);
            return null;
        }
    }

    /// <summary>
    /// Converts FProperty array to PropertyDefinition dictionary for TypeRegistry.
    /// </summary>
    public static Dictionary<int, PropertyDefinition> ConvertToPropertyDefinitions(FProperty[] properties)
    {
        var result = new Dictionary<int, PropertyDefinition>();

        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            var propDef = ConvertProperty(prop, i);
            if (propDef != null)
            {
                result[i] = propDef;
            }
        }

        return result;
    }

    private static PropertyDefinition? ConvertProperty(FProperty prop, int index)
    {
        var type = GetPropertyType(prop);
        if (type == null)
            return null;

        return new PropertyDefinition(prop.Name, index, type)
        {
            ArraySize = prop.ArrayDim > 1 ? prop.ArrayDim : 1
        };
    }

    private static PropertyType? GetPropertyType(FProperty prop)
    {
        // Note: More specific types must come before their base types in pattern matching
        return prop switch
        {
            FArrayProperty arr => new PropertyType(PropertyKind.ArrayProperty)
            {
                InnerType = arr.Inner != null ? GetPropertyType(arr.Inner) : null
            },
            FBoolProperty => new PropertyType(PropertyKind.BoolProperty),
            FByteProperty bp => bp.EnumName != null
                ? new PropertyType(PropertyKind.EnumProperty) { EnumName = bp.EnumName }
                : new PropertyType(PropertyKind.ByteProperty),
            FClassProperty => new PropertyType(PropertyKind.ObjectProperty), // ClassProperty uses ObjectProperty kind
            FDelegateProperty => new PropertyType(PropertyKind.DelegateProperty),
            FEnumProperty ep => new PropertyType(PropertyKind.EnumProperty)
            {
                EnumName = ep.EnumName,
                InnerType = ep.UnderlyingProp != null ? GetPropertyType(ep.UnderlyingProp) : null
            },
            FFieldPathProperty => new PropertyType(PropertyKind.FieldPathProperty),
            FInterfaceProperty => new PropertyType(PropertyKind.InterfaceProperty),
            FMapProperty mp => new PropertyType(PropertyKind.MapProperty)
            {
                InnerType = mp.KeyProp != null ? GetPropertyType(mp.KeyProp) : null,
                ValueType = mp.ValueProp != null ? GetPropertyType(mp.ValueProp) : null
            },
            FMulticastDelegateProperty => new PropertyType(PropertyKind.MulticastDelegateProperty),
            FSetProperty sp => new PropertyType(PropertyKind.SetProperty)
            {
                InnerType = sp.ElementProp != null ? GetPropertyType(sp.ElementProp) : null
            },
            FStructProperty sp => new PropertyType(PropertyKind.StructProperty)
            {
                StructName = sp.StructName
            },
            FOptionalProperty op => new PropertyType(PropertyKind.OptionalProperty)
            {
                InnerType = op.ValueProperty != null ? GetPropertyType(op.ValueProperty) : null
            },
            // FObjectProperty hierarchy - most specific first
            FSoftClassProperty => new PropertyType(PropertyKind.SoftObjectProperty),
            FSoftObjectProperty => new PropertyType(PropertyKind.SoftObjectProperty),
            FWeakObjectProperty => new PropertyType(PropertyKind.WeakObjectProperty),
            FLazyObjectProperty => new PropertyType(PropertyKind.LazyObjectProperty),
            FObjectProperty => new PropertyType(PropertyKind.ObjectProperty), // Base type last
            FNumericProperty => GetNumericPropertyKind(prop),
            // For base FProperty instances (NameProperty, StrProperty, TextProperty), use stored TypeName
            _ => GetPropertyKindByTypeName(prop.TypeName)
        };
    }

    private static PropertyType GetNumericPropertyKind(FProperty prop)
    {
        // FNumericProperty is base for all numeric types
        // We use the stored TypeName from serialization to get the exact type
        var kind = prop.TypeName switch
        {
            "DoubleProperty" => PropertyKind.DoubleProperty,
            "FloatProperty" => PropertyKind.FloatProperty,
            "Int16Property" => PropertyKind.Int16Property,
            "Int64Property" => PropertyKind.Int64Property,
            "Int8Property" => PropertyKind.Int8Property,
            "IntProperty" => PropertyKind.IntProperty,
            "UInt16Property" => PropertyKind.UInt16Property,
            "UInt32Property" => PropertyKind.UInt32Property,
            "UInt64Property" => PropertyKind.UInt64Property,
            _ => PropertyKind.IntProperty // Default fallback
        };
        return new PropertyType(kind);
    }

    private static PropertyType? GetPropertyKindByTypeName(string typeName)
    {
        // Remove 'F' prefix if present
        if (typeName.StartsWith('F'))
            typeName = typeName[1..];

        var kind = typeName switch
        {
            "NameProperty" => PropertyKind.NameProperty,
            "StrProperty" => PropertyKind.StrProperty,
            "TextProperty" => PropertyKind.TextProperty,
            _ => (PropertyKind?)null
        };

        if (!kind.HasValue)
        {
            Serilog.Log.Warning("Unknown property type in Blueprint: {TypeName}", typeName);
            return null;
        }

        return new PropertyType(kind.Value);
    }
}
