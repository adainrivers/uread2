using System.Collections.Concurrent;
using URead2.Compression;
using URead2.Deserialization.Abstractions;

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

    // Lookup indexes
    private readonly ConcurrentDictionary<ulong, TypeDefinition> _typesByHash = new();
    private readonly ConcurrentDictionary<string, TypeDefinition> _typesByPackagePath = new(StringComparer.OrdinalIgnoreCase);

    // Caches
    private readonly ConcurrentDictionary<string, PropertyDefinition?[]> _flattenedPropertiesCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _failedResolutions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Callback for lazy type resolution when type is not found locally.
    /// Used to resolve Blueprint types on-demand from asset exports.
    /// Returns (superTypeName, packagePath) or null if not resolvable.
    /// </summary>
    public Func<string, (string? SuperName, string? PackagePath)?>? LazyResolver { get; set; }

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
    /// Loads additional types from a .usmap file into this registry.
    /// </summary>
    public void LoadUsmap(string path, Decompressor? decompressor = null)
    {
        var loader = new UsmapLoader(decompressor);
        loader.Load(path, this);
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
    /// Gets a type by hash.
    /// </summary>
    public TypeDefinition? GetTypeByHash(ulong hash)
    {
        return _typesByHash.GetValueOrDefault(hash);
    }

    /// <summary>
    /// Gets a type by package path.
    /// </summary>
    public TypeDefinition? GetTypeByPackagePath(string packagePath)
    {
        return _typesByPackagePath.GetValueOrDefault(packagePath);
    }

    /// <summary>
    /// Checks if a type exists.
    /// </summary>
    public bool HasType(string typeName)
    {
        return _types.ContainsKey(typeName);
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

        // Index by package path
        if (!string.IsNullOrEmpty(type.PackagePath))
        {
            _typesByPackagePath[type.PackagePath] = type;
        }

        // Invalidate flattened properties cache
        _flattenedPropertiesCache.TryRemove(type.Name, out _);
    }

    /// <summary>
    /// Registers a type with a hash for lookup.
    /// </summary>
    public void RegisterWithHash(TypeDefinition type, ulong hash)
    {
        Register(type);
        _typesByHash[hash] = type;
    }

    /// <summary>
    /// Marks a type to be skipped during deserialization.
    /// </summary>
    public void SetSkip(string typeName, bool skip = true)
    {
        if (_types.TryGetValue(typeName, out var type))
        {
            type.ShouldSkip = skip;
        }
    }

    /// <summary>
    /// Marks multiple types to be skipped during deserialization.
    /// </summary>
    public void SetSkip(IEnumerable<string> typeNames, bool skip = true)
    {
        foreach (var name in typeNames)
            SetSkip(name, skip);
    }

    /// <summary>
    /// Sets a custom deserializer for a type.
    /// </summary>
    public void SetDeserializer(string typeName, ITypeReader deserializer)
    {
        if (_types.TryGetValue(typeName, out var type))
        {
            type.Deserializer = deserializer;
        }
    }

    /// <summary>
    /// Gets the deserializer for a type (custom or null for default).
    /// </summary>
    public ITypeReader? GetDeserializer(string typeName)
    {
        return _types.TryGetValue(typeName, out var type) ? type.Deserializer : null;
    }

    /// <summary>
    /// Checks if a type should be skipped.
    /// </summary>
    public bool ShouldSkip(string typeName)
    {
        return _types.TryGetValue(typeName, out var type) && type.ShouldSkip;
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
    /// Checks if an enum exists.
    /// </summary>
    public bool HasEnum(string enumName)
    {
        return _enums.ContainsKey(enumName);
    }

    /// <summary>
    /// Registers an enum definition.
    /// </summary>
    public void Register(EnumDefinition enumDef)
    {
        _enums[enumDef.Name] = enumDef;
    }

    /// <summary>
    /// Gets the name for an enum value.
    /// </summary>
    public string? GetEnumValueName(string enumName, long value)
    {
        return GetEnum(enumName)?.GetName(value);
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

    /// <summary>
    /// Gets a property by type and index from flattened properties.
    /// </summary>
    public PropertyDefinition? GetProperty(string typeName, int index)
    {
        var props = GetFlattenedProperties(typeName);
        if (props == null || index < 0 || index >= props.Length)
            return null;
        return props[index];
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

        var (superName, packagePath) = result.Value;

        // Get parent's property count for inheritance
        int propertyCount = 0;
        if (!string.IsNullOrEmpty(superName))
        {
            var parent = GetType(superName);
            if (parent != null)
                propertyCount = parent.PropertyCount;
        }

        // Create minimal type definition
        var type = new TypeDefinition(typeName, TypeSource.Asset, new Dictionary<int, PropertyDefinition>())
        {
            SuperName = superName,
            PackagePath = packagePath,
            PropertyCount = propertyCount
        };

        Register(type);
        return type;
    }

    /// <summary>
    /// Clears failed resolution cache to allow retrying.
    /// </summary>
    public void ClearFailedResolutions()
    {
        _failedResolutions.Clear();
    }

    #endregion

    #region Statistics

    /// <summary>
    /// Gets the total number of registered types.
    /// </summary>
    public int TypeCount => _types.Count;

    /// <summary>
    /// Gets the total number of registered enums.
    /// </summary>
    public int EnumCount => _enums.Count;

    /// <summary>
    /// Gets the number of types from a specific source.
    /// </summary>
    public int GetTypeCount(TypeSource source)
    {
        return _types.Values.Count(t => t.Source == source);
    }

    /// <summary>
    /// Gets all type names.
    /// </summary>
    public IEnumerable<string> TypeNames => _types.Keys;

    /// <summary>
    /// Gets all enum names.
    /// </summary>
    public IEnumerable<string> EnumNames => _enums.Keys;

    /// <summary>
    /// Gets all types.
    /// </summary>
    public IEnumerable<TypeDefinition> Types => _types.Values;

    /// <summary>
    /// Gets all enums.
    /// </summary>
    public IEnumerable<EnumDefinition> Enums => _enums.Values;

    #endregion

    #region Clear

    /// <summary>
    /// Clears all types from a specific source.
    /// </summary>
    public void ClearSource(TypeSource source)
    {
        var toRemove = _types.Where(kvp => kvp.Value.Source == source).Select(kvp => kvp.Key).ToList();
        foreach (var name in toRemove)
        {
            if (_types.TryRemove(name, out var type))
            {
                if (!string.IsNullOrEmpty(type.PackagePath))
                    _typesByPackagePath.TryRemove(type.PackagePath, out _);
            }
        }

        var enumsToRemove = _enums.Where(kvp => kvp.Value.Source == source).Select(kvp => kvp.Key).ToList();
        foreach (var name in enumsToRemove)
            _enums.TryRemove(name, out _);

        _flattenedPropertiesCache.Clear();
    }

    /// <summary>
    /// Clears all types and enums.
    /// </summary>
    public void Clear()
    {
        _types.Clear();
        _enums.Clear();
        _typesByHash.Clear();
        _typesByPackagePath.Clear();
        _flattenedPropertiesCache.Clear();
        _failedResolutions.Clear();
    }

    #endregion
}
