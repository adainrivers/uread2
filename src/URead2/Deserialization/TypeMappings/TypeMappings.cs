namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// Container for type mappings loaded from .usmap files.
/// Provides schema lookups for deserializing unversioned packages.
/// </summary>
public sealed class TypeMappings
{
    /// <summary>
    /// All schemas indexed by name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, UsmapSchema> Schemas { get; }

    /// <summary>
    /// All enums indexed by name (case-insensitive).
    /// </summary>
    public IReadOnlyDictionary<string, UsmapEnum> Enums { get; }

    public TypeMappings(
        Dictionary<string, UsmapSchema> schemas,
        Dictionary<string, UsmapEnum> enums)
    {
        Schemas = schemas;
        Enums = enums;
    }

    /// <summary>
    /// Gets a schema by name.
    /// </summary>
    public UsmapSchema? GetSchema(string name)
    {
        return Schemas.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets an enum by name.
    /// </summary>
    public UsmapEnum? GetEnum(string name)
    {
        return Enums.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets a property from a schema, walking up the inheritance chain if needed.
    /// </summary>
    public UsmapProperty? GetProperty(string schemaName, string propertyName, int arrayIndex = 0)
    {
        var currentSchema = schemaName;

        while (currentSchema != null)
        {
            var schema = GetSchema(currentSchema);
            if (schema == null)
                return null;

            var prop = schema.GetProperty(propertyName, arrayIndex);
            if (prop != null)
                return prop;

            currentSchema = schema.SuperType;
        }

        return null;
    }

    /// <summary>
    /// Gets all properties for a schema including inherited properties.
    /// </summary>
    public IEnumerable<UsmapProperty> GetAllProperties(string schemaName)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentSchema = schemaName;

        while (currentSchema != null && visited.Add(currentSchema))
        {
            var schema = GetSchema(currentSchema);
            if (schema == null)
                yield break;

            foreach (var prop in schema.Properties.Values)
                yield return prop;

            currentSchema = schema.SuperType;
        }
    }
}
