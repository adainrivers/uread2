using URead2.Compression;
using URead2.Deserialization.Abstractions;

namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// Unified type resolver that combines static type mappings (from .usmap files)
/// with dynamically registered asset-defined types (BlueprintGeneratedClass,
/// UserDefinedStruct, UserDefinedEnum).
/// </summary>
public class TypeResolver : ITypeResolver
{
    private readonly TypeMappings? _mappings;
    private readonly Dictionary<string, UsmapSchema> _assetSchemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsmapEnum> _assetEnums = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsmapProperty?[]> _flattenedPropertiesCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets an empty type resolver with no mappings.
    /// </summary>
    public static TypeResolver Empty { get; } = new();

    /// <summary>
    /// Creates an empty type resolver.
    /// </summary>
    public TypeResolver()
    {
    }

    /// <summary>
    /// Creates a type resolver with static type mappings.
    /// </summary>
    /// <param name="mappings">Type mappings from usmap file.</param>
    public TypeResolver(TypeMappings mappings)
    {
        _mappings = mappings;
    }

    /// <summary>
    /// Creates a type resolver by reading a .usmap file.
    /// </summary>
    /// <param name="path">Path to the .usmap file.</param>
    /// <param name="decompressor">Optional decompressor for Oodle-compressed files.</param>
    public static TypeResolver FromUsmapFile(string path, Decompressor? decompressor = null)
    {
        var reader = new UsmapReader(decompressor);
        var mappings = reader.Read(path);
        return new TypeResolver(mappings);
    }

    /// <summary>
    /// Creates a type resolver by reading a .usmap from a stream.
    /// </summary>
    /// <param name="stream">Stream containing the .usmap data.</param>
    /// <param name="decompressor">Optional decompressor for Oodle-compressed files.</param>
    public static TypeResolver FromUsmapStream(Stream stream, Decompressor? decompressor = null)
    {
        var reader = new UsmapReader(decompressor);
        var mappings = reader.Read(stream);
        return new TypeResolver(mappings);
    }

    /// <summary>
    /// Gets the underlying type mappings, if any.
    /// </summary>
    public TypeMappings? Mappings => _mappings;

    /// <summary>
    /// Gets a schema by name. Checks asset-defined types first, then static mappings.
    /// </summary>
    public UsmapSchema? GetSchema(string typeName)
    {
        // Asset-defined types take precedence (they can override base types)
        if (_assetSchemas.TryGetValue(typeName, out var assetSchema))
            return assetSchema;

        return _mappings?.GetSchema(typeName);
    }

    /// <summary>
    /// Gets an enum by name. Checks asset-defined types first, then static mappings.
    /// </summary>
    public UsmapEnum? GetEnum(string enumName)
    {
        // Asset-defined enums take precedence
        if (_assetEnums.TryGetValue(enumName, out var assetEnum))
            return assetEnum;

        return _mappings?.GetEnum(enumName);
    }

    /// <summary>
    /// Checks if a schema exists for the given type.
    /// </summary>
    public bool HasSchema(string typeName)
    {
        return _assetSchemas.ContainsKey(typeName) || _mappings?.GetSchema(typeName) != null;
    }

    /// <summary>
    /// Checks if an enum exists with the given name.
    /// </summary>
    public bool HasEnum(string enumName)
    {
        return _assetEnums.ContainsKey(enumName) || _mappings?.GetEnum(enumName) != null;
    }

    /// <summary>
    /// Registers a schema for an asset-defined type.
    /// </summary>
    public void RegisterSchema(UsmapSchema schema)
    {
        _assetSchemas[schema.Name] = schema;
    }

    /// <summary>
    /// Registers a schema with the given properties.
    /// </summary>
    public void RegisterSchema(string name, string? superType, IEnumerable<UsmapProperty> properties)
    {
        var propDict = new Dictionary<int, UsmapProperty>();
        int maxIndex = 0;

        foreach (var prop in properties)
        {
            propDict[prop.SchemaIndex] = prop;
            if (prop.SchemaIndex > maxIndex)
                maxIndex = prop.SchemaIndex;
        }

        var schema = new UsmapSchema(name, superType, (ushort)(maxIndex + 1), propDict);
        _assetSchemas[name] = schema;
    }

    /// <summary>
    /// Registers an enum for an asset-defined type.
    /// </summary>
    public void RegisterEnum(UsmapEnum enumDef)
    {
        _assetEnums[enumDef.Name] = enumDef;
    }

    /// <summary>
    /// Registers an enum with the given values.
    /// </summary>
    public void RegisterEnum(string name, IEnumerable<KeyValuePair<long, string>> values)
    {
        var valueDict = new Dictionary<long, string>();
        foreach (var kvp in values)
        {
            valueDict[kvp.Key] = kvp.Value;
        }
        _assetEnums[name] = new UsmapEnum(name, valueDict);
    }

    /// <summary>
    /// Removes an asset-defined schema registration.
    /// </summary>
    public bool UnregisterSchema(string name) => _assetSchemas.Remove(name);

    /// <summary>
    /// Removes an asset-defined enum registration.
    /// </summary>
    public bool UnregisterEnum(string name) => _assetEnums.Remove(name);

    /// <summary>
    /// Clears all asset-defined type registrations.
    /// Static mappings from usmap are not affected.
    /// </summary>
    public void ClearAssetTypes()
    {
        _assetSchemas.Clear();
        _assetEnums.Clear();
    }

    /// <summary>
    /// Gets the number of asset-defined schemas.
    /// </summary>
    public int AssetSchemaCount => _assetSchemas.Count;

    /// <summary>
    /// Gets the number of asset-defined enums.
    /// </summary>
    public int AssetEnumCount => _assetEnums.Count;

    /// <summary>
    /// Gets the total number of schemas (static + asset-defined).
    /// </summary>
    public int TotalSchemaCount => (_mappings?.Schemas.Count ?? 0) + _assetSchemas.Count;

    /// <summary>
    /// Gets the total number of enums (static + asset-defined).
    /// </summary>
    public int TotalEnumCount => (_mappings?.Enums.Count ?? 0) + _assetEnums.Count;

    /// <summary>
    /// Gets all asset-defined schema names.
    /// </summary>
    public IEnumerable<string> AssetSchemaNames => _assetSchemas.Keys;

    /// <summary>
    /// Gets all asset-defined enum names.
    /// </summary>
    public IEnumerable<string> AssetEnumNames => _assetEnums.Keys;

    /// <summary>
    /// Gets flattened properties for a schema including inherited ones.
    /// Results are cached for performance.
    /// </summary>
    public UsmapProperty?[]? GetFlattenedProperties(string typeName)
    {
        if (_flattenedPropertiesCache.TryGetValue(typeName, out var cached))
            return cached;

        var schema = GetSchema(typeName);
        if (schema == null)
            return null;

        var properties = new UsmapProperty?[schema.PropertyCount];

        // Fill from current schema
        foreach (var kvp in schema.Properties)
        {
            if (kvp.Key >= 0 && kvp.Key < properties.Length)
                properties[kvp.Key] = kvp.Value;
        }

        // Fill missing from parent schemas
        var currentSchema = schema;
        while (currentSchema.SuperType != null)
        {
            var parent = GetSchema(currentSchema.SuperType);
            if (parent == null)
                break;

            foreach (var kvp in parent.Properties)
            {
                if (kvp.Key >= 0 && kvp.Key < properties.Length && properties[kvp.Key] == null)
                    properties[kvp.Key] = kvp.Value;
            }

            currentSchema = parent;
        }

        _flattenedPropertiesCache[typeName] = properties;
        return properties;
    }
}
