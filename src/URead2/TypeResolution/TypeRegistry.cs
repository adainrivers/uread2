using System.Collections.Concurrent;
using URead2.Compression;

namespace URead2.TypeResolution;

/// <summary>
/// Unified registry for all type and enum definitions.
/// Single source of truth for type resolution, similar to .NET's Assembly.GetTypes().
/// </summary>
public sealed class TypeRegistry
{
    // Type storage
    private readonly ConcurrentDictionary<string, TypeDefinition> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EnumDefinition> _enums = new(StringComparer.OrdinalIgnoreCase);

    // Caches
    private readonly ConcurrentDictionary<string, PropertyDefinition?[]> _flattenedPropertiesCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _failedResolutions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Callback for lazy type resolution when type is not found locally.
    /// Used to resolve Blueprint types on-demand from asset exports.
    /// Returns (superTypeName, packagePath, properties) or null if not resolvable.
    /// </summary>
    public Func<string, (string? SuperName, string? PackagePath, Dictionary<int, PropertyDefinition>? Properties)?>? LazyResolver { get; set; }

    /// <summary>
    /// Gets an empty registry with no types.
    /// </summary>
    public static TypeRegistry Empty { get; } = new();

    /// <summary>
    /// Creates a TypeRegistry and loads types from a .usmap file.
    /// </summary>
    public static TypeRegistry FromUsmap(string path, Decompressor? decompressor = null)
    {
        var registry = new TypeRegistry();
        var loader = new UsmapLoader(decompressor);
        loader.Load(path, registry);
        return registry;
    }

    /// <summary>
    /// Creates a TypeRegistry and loads types from a .usmap stream.
    /// </summary>
    public static TypeRegistry FromUsmap(Stream stream, Decompressor? decompressor = null)
    {
        var registry = new TypeRegistry();
        var loader = new UsmapLoader(decompressor);
        loader.Load(stream, registry);
        return registry;
    }

    /// <summary>
    /// Creates a TypeRegistry and loads types from a JSON file (SDK generator format).
    /// </summary>
    public static TypeRegistry FromJson(string path)
    {
        var registry = new TypeRegistry();
        var loader = new TypeRegistryJsonLoader();
        loader.Load(path, registry);
        return registry;
    }

    /// <summary>
    /// Creates a TypeRegistry and loads types from a JSON stream (SDK generator format).
    /// </summary>
    public static TypeRegistry FromJson(Stream stream)
    {
        var registry = new TypeRegistry();
        var loader = new TypeRegistryJsonLoader();
        loader.Load(stream, registry);
        return registry;
    }

    #region Type Operations

    /// <summary>
    /// Gets a type by name. Falls back to lazy resolution if not found.
    /// </summary>
    public TypeDefinition? GetType(string typeName)
    {
        if (_types.TryGetValue(typeName, out var type))
            return type;

        return TryLazyResolve(typeName);
    }

    /// <summary>
    /// Tries to get a type by name without triggering lazy resolution.
    /// Use this when you need to check types during lazy resolution to avoid infinite recursion.
    /// </summary>
    public TypeDefinition? TryGetType(string typeName)
    {
        return _types.GetValueOrDefault(typeName);
    }

    /// <summary>
    /// Registers a type definition.
    /// </summary>
    public void Register(TypeDefinition type)
    {
        _types[type.Name] = type;

        // Resolve super reference
        if (!string.IsNullOrEmpty(type.SuperName) && type.Super == null)
        {
            type.Super = GetType(type.SuperName);
        }

        // Invalidate flattened properties cache
        _flattenedPropertiesCache.TryRemove(type.Name, out _);
    }

    #endregion

    #region Enum Operations

    /// <summary>
    /// Gets an enum by name.
    /// </summary>
    public EnumDefinition? GetEnum(string enumName)
    {
        return _enums.GetValueOrDefault(enumName);
    }

    /// <summary>
    /// Registers an enum definition.
    /// </summary>
    public void Register(EnumDefinition enumDef)
    {
        _enums[enumDef.Name] = enumDef;
    }

    #endregion

    #region Property Resolution

    /// <summary>
    /// Gets flattened properties for a type including inherited ones.
    /// Results are cached. Returns null if type not found.
    /// </summary>
    public PropertyDefinition?[]? GetFlattenedProperties(string typeName)
    {
        if (_flattenedPropertiesCache.TryGetValue(typeName, out var cached))
            return cached;

        var type = GetType(typeName);
        if (type == null)
            return null;

        // Build inheritance chain (child -> parent -> grandparent -> ...)
        var chain = new List<TypeDefinition>();
        var current = type;
        while (current != null)
        {
            chain.Add(current);
            current = current.Super ?? (string.IsNullOrEmpty(current.SuperName) ? null : GetType(current.SuperName));
        }

        // Calculate total property count
        int totalCount = 0;
        foreach (var t in chain)
            totalCount += t.PropertyCount;

        var properties = new PropertyDefinition?[totalCount];

        // Fill properties: derived class properties come FIRST (lower indices),
        // parent class properties come AFTER (higher indices).
        int offset = 0;
        foreach (var t in chain)
        {
            foreach (var kvp in t.Properties)
            {
                int globalIndex = offset + kvp.Key;
                if (globalIndex >= 0 && globalIndex < properties.Length)
                    properties[globalIndex] = kvp.Value;
            }
            offset += t.PropertyCount;
        }

        _flattenedPropertiesCache[typeName] = properties;
        return properties;
    }

    #endregion

    #region Lazy Resolution

    private TypeDefinition? TryLazyResolve(string typeName)
    {
        if (LazyResolver == null)
            return null;

        if (_failedResolutions.ContainsKey(typeName))
            return null;

        var result = LazyResolver(typeName);
        if (result == null)
        {
            _failedResolutions.TryAdd(typeName, 0);
            return null;
        }

        var (superName, packagePath, properties) = result.Value;

        // Use provided properties or empty dictionary
        var propsDict = properties ?? new Dictionary<int, PropertyDefinition>();

        // Get parent's property count for inheritance
        int propertyCount = propsDict.Count;
        if (!string.IsNullOrEmpty(superName))
        {
            var parent = GetType(superName);
            if (parent != null)
                propertyCount += parent.PropertyCount;
        }

        // Create type definition with resolved properties
        var type = new TypeDefinition(typeName, TypeSource.Asset, propsDict)
        {
            SuperName = superName,
            PackagePath = packagePath,
            PropertyCount = propertyCount
        };

        Register(type);
        return type;
    }

    #endregion

    #region Enumeration

    /// <summary>
    /// Gets all types.
    /// </summary>
    public IEnumerable<TypeDefinition> Types => _types.Values;

    /// <summary>
    /// Gets all enums.
    /// </summary>
    public IEnumerable<EnumDefinition> Enums => _enums.Values;

    #endregion
}
