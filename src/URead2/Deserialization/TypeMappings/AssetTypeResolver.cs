using URead2.Deserialization.Abstractions;

namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// A mutable type resolver for asset-defined types.
/// Schemas and enums can be registered from BlueprintGeneratedClass,
/// UserDefinedStruct, and UserDefinedEnum exports.
/// </summary>
public class AssetTypeResolver : ITypeResolver
{
    private readonly Dictionary<string, UsmapSchema> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UsmapEnum> _enums = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a schema for a type.
    /// </summary>
    public void RegisterSchema(UsmapSchema schema)
    {
        _schemas[schema.Name] = schema;
    }

    /// <summary>
    /// Registers a schema with the given name.
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
        _schemas[name] = schema;
    }

    /// <summary>
    /// Registers an enum definition.
    /// </summary>
    public void RegisterEnum(UsmapEnum enumDef)
    {
        _enums[enumDef.Name] = enumDef;
    }

    /// <summary>
    /// Registers an enum with the given name and values.
    /// </summary>
    public void RegisterEnum(string name, IEnumerable<KeyValuePair<long, string>> values)
    {
        var valueDict = new Dictionary<long, string>();
        foreach (var kvp in values)
        {
            valueDict[kvp.Key] = kvp.Value;
        }
        _enums[name] = new UsmapEnum(name, valueDict);
    }

    /// <summary>
    /// Removes a schema registration.
    /// </summary>
    public bool UnregisterSchema(string name) => _schemas.Remove(name);

    /// <summary>
    /// Removes an enum registration.
    /// </summary>
    public bool UnregisterEnum(string name) => _enums.Remove(name);

    /// <summary>
    /// Clears all registrations.
    /// </summary>
    public void Clear()
    {
        _schemas.Clear();
        _enums.Clear();
    }

    public UsmapSchema? GetSchema(string typeName)
    {
        return _schemas.GetValueOrDefault(typeName);
    }

    public UsmapEnum? GetEnum(string enumName)
    {
        return _enums.GetValueOrDefault(enumName);
    }

    public bool HasSchema(string typeName) => _schemas.ContainsKey(typeName);
    public bool HasEnum(string enumName) => _enums.ContainsKey(enumName);

    /// <summary>
    /// Gets the number of registered schemas.
    /// </summary>
    public int SchemaCount => _schemas.Count;

    /// <summary>
    /// Gets the number of registered enums.
    /// </summary>
    public int EnumCount => _enums.Count;

    /// <summary>
    /// Gets all registered schema names.
    /// </summary>
    public IEnumerable<string> SchemaNames => _schemas.Keys;

    /// <summary>
    /// Gets all registered enum names.
    /// </summary>
    public IEnumerable<string> EnumNames => _enums.Keys;
}
