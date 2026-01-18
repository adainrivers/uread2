using System.Text.Json;
using System.Text.Json.Serialization;

namespace URead2.TypeResolution;

/// <summary>
/// Loads type definitions from JSON files (exported by SDK generator) into a TypeRegistry.
/// This format includes full package paths unlike usmap.
/// </summary>
public sealed class TypeRegistryJsonLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads types and enums from a JSON file into the registry.
    /// </summary>
    public void Load(string path, TypeRegistry registry)
    {
        using var stream = File.OpenRead(path);
        Load(stream, registry);
    }

    /// <summary>
    /// Loads types and enums from a JSON stream into the registry.
    /// </summary>
    public void Load(Stream stream, TypeRegistry registry)
    {
        var export = JsonSerializer.Deserialize<TypeRegistryJsonExport>(stream, JsonOptions)
            ?? throw new InvalidDataException("Failed to deserialize type registry JSON");

        // Load enums first (types may reference them)
        foreach (var enumInfo in export.Enums)
        {
            var enumDef = ConvertEnum(enumInfo);
            registry.Register(enumDef);
        }

        // Load types
        foreach (var typeInfo in export.Types)
        {
            var typeDef = ConvertType(typeInfo);
            registry.Register(typeDef);
        }
    }

    private EnumDefinition ConvertEnum(JsonEnumInfo info)
    {
        var values = new Dictionary<long, string>();
        foreach (var entry in info.Values)
        {
            values[entry.Value] = entry.Name;
        }

        return new EnumDefinition(
            ParseTypeName(info.Name).Name,
            ConvertSource(info.Source),
            values,
            info.UnderlyingType);
    }

    private TypeDefinition ConvertType(JsonTypeInfo info)
    {
        var (packagePath, name) = ParseTypeName(info.Name);
        var (superPackagePath, superName) = info.SuperName != null
            ? ParseTypeName(info.SuperName)
            : (null, null);

        var properties = new Dictionary<int, PropertyDefinition>();
        if (info.Properties != null)
        {
            foreach (var prop in info.Properties)
            {
                var propDef = ConvertProperty(prop);
                properties[prop.Index] = propDef;
            }
        }

        return new TypeDefinition(name, ConvertSource(info.Source), properties)
        {
            SuperName = superName,
            PackagePath = packagePath,
            Kind = info.Kind == JsonTypeKind.Struct ? TypeKind.Struct : TypeKind.Class,
            PropertyCount = info.Properties?.Count ?? 0
        };
    }

    private PropertyDefinition ConvertProperty(JsonPropertyInfo info)
    {
        return new PropertyDefinition(info.Name, info.Index, ConvertPropertyType(info.Type))
        {
            ArrayIndex = info.ArrayIndex,
            ArraySize = info.ArraySize
        };
    }

    private PropertyType ConvertPropertyType(JsonPropertyTypeInfo info)
    {
        var kind = ParsePropertyKind(info.Kind);

        return new PropertyType(kind)
        {
            StructName = info.StructName != null ? ParseTypeName(info.StructName).Name : null,
            EnumName = info.EnumName != null ? ParseTypeName(info.EnumName).Name : null,
            InnerType = info.InnerType != null ? ConvertPropertyType(info.InnerType) : null,
            ValueType = info.ValueType != null ? ConvertPropertyType(info.ValueType) : null
        };
    }

    private static PropertyKind ParsePropertyKind(string kind)
    {
        return kind switch
        {
            "BoolProperty" => PropertyKind.BoolProperty,
            "ByteProperty" => PropertyKind.ByteProperty,
            "Int8Property" => PropertyKind.Int8Property,
            "Int16Property" => PropertyKind.Int16Property,
            "UInt16Property" => PropertyKind.UInt16Property,
            "IntProperty" => PropertyKind.IntProperty,
            "UInt32Property" => PropertyKind.UInt32Property,
            "Int64Property" => PropertyKind.Int64Property,
            "UInt64Property" => PropertyKind.UInt64Property,
            "FloatProperty" => PropertyKind.FloatProperty,
            "DoubleProperty" => PropertyKind.DoubleProperty,
            "EnumProperty" => PropertyKind.EnumProperty,
            "StructProperty" => PropertyKind.StructProperty,
            "ObjectProperty" => PropertyKind.ObjectProperty,
            "WeakObjectProperty" => PropertyKind.WeakObjectProperty,
            "SoftObjectProperty" => PropertyKind.SoftObjectProperty,
            "SoftClassProperty" => PropertyKind.SoftObjectProperty, // Maps to SoftObjectProperty
            "LazyObjectProperty" => PropertyKind.LazyObjectProperty,
            "ClassProperty" => PropertyKind.ObjectProperty, // TSubclassOf maps to ObjectProperty
            "NameProperty" => PropertyKind.NameProperty,
            "StrProperty" => PropertyKind.StrProperty,
            "TextProperty" => PropertyKind.TextProperty,
            "ArrayProperty" => PropertyKind.ArrayProperty,
            "SetProperty" => PropertyKind.SetProperty,
            "MapProperty" => PropertyKind.MapProperty,
            "OptionalProperty" => PropertyKind.OptionalProperty,
            "InterfaceProperty" => PropertyKind.InterfaceProperty,
            "DelegateProperty" => PropertyKind.DelegateProperty,
            "MulticastDelegateProperty" => PropertyKind.MulticastDelegateProperty,
            "FieldPathProperty" => PropertyKind.FieldPathProperty,
            _ => PropertyKind.Unknown
        };
    }

    private static TypeSource ConvertSource(JsonTypeSource source)
    {
        return source switch
        {
            JsonTypeSource.Runtime => TypeSource.Runtime,
            JsonTypeSource.Asset => TypeSource.Asset,
            JsonTypeSource.Manual => TypeSource.Manual,
            _ => TypeSource.Runtime
        };
    }

    /// <summary>
    /// Parses a fully qualified name into (PackagePath, Name).
    /// E.g., "/Script/Engine.Actor" -> ("/Script/Engine", "Actor")
    /// </summary>
    private static (string? PackagePath, string Name) ParseTypeName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot <= 0)
            return (null, fullName);

        return (fullName[..lastDot], fullName[(lastDot + 1)..]);
    }

    #region JSON DTOs

    private enum JsonTypeSource { Runtime, Asset, Manual }
    private enum JsonTypeKind { Class, Struct }

    private sealed class JsonPropertyTypeInfo
    {
        public string Kind { get; set; } = "Unknown";
        public string? StructName { get; set; }
        public string? EnumName { get; set; }
        public JsonPropertyTypeInfo? InnerType { get; set; }
        public JsonPropertyTypeInfo? ValueType { get; set; }
    }

    private sealed class JsonPropertyInfo
    {
        public string Name { get; set; } = string.Empty;
        public int Index { get; set; }
        public int ArrayIndex { get; set; }
        public int ArraySize { get; set; } = 1;
        public JsonPropertyTypeInfo Type { get; set; } = new();
    }

    private sealed class JsonTypeInfo
    {
        public string Name { get; set; } = string.Empty;
        public string? SuperName { get; set; }
        public JsonTypeSource Source { get; set; }
        public JsonTypeKind Kind { get; set; }
        public List<JsonPropertyInfo>? Properties { get; set; }
    }

    private sealed class JsonEnumValueInfo
    {
        public string Name { get; set; } = string.Empty;
        public long Value { get; set; }
    }

    private sealed class JsonEnumInfo
    {
        public string Name { get; set; } = string.Empty;
        public JsonTypeSource Source { get; set; }
        public string? UnderlyingType { get; set; }
        public List<JsonEnumValueInfo> Values { get; set; } = new();
    }

    private sealed class TypeRegistryJsonExport
    {
        public List<JsonTypeInfo> Types { get; set; } = new();
        public List<JsonEnumInfo> Enums { get; set; } = new();
    }

    #endregion
}
