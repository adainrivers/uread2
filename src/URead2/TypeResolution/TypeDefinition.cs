using URead2.Deserialization.Abstractions;

namespace URead2.TypeResolution;

/// <summary>
/// Definition of a class or struct type.
/// </summary>
public sealed class TypeDefinition
{
    /// <summary>
    /// Type name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Kind of type (class or struct).
    /// </summary>
    public TypeKind Kind { get; init; } = TypeKind.Class;

    /// <summary>
    /// Source of this type definition.
    /// </summary>
    public TypeSource Source { get; }

    /// <summary>
    /// Super type name, or null if none.
    /// </summary>
    public string? SuperName { get; init; }

    /// <summary>
    /// Resolved super type reference (set by TypeRegistry).
    /// </summary>
    public TypeDefinition? Super { get; internal set; }

    /// <summary>
    /// Package path for Blueprint types (e.g., "/Game/Blueprints/BP_Player").
    /// </summary>
    public string? PackagePath { get; init; }

    /// <summary>
    /// Total property count including inherited properties.
    /// Used for unversioned serialization.
    /// </summary>
    public int PropertyCount { get; init; }

    /// <summary>
    /// Properties defined directly on this type, indexed by schema index.
    /// </summary>
    public IReadOnlyDictionary<int, PropertyDefinition> Properties { get; }

    /// <summary>
    /// Custom deserializer for this type, or null to use default property reader.
    /// </summary>
    public ITypeReader? Deserializer { get; set; }

    /// <summary>
    /// If true, skip deserialization entirely (return empty PropertyBag).
    /// </summary>
    public bool ShouldSkip { get; set; }

    /// <summary>
    /// Properties indexed by (name, arrayIndex) for fast lookup.
    /// </summary>
    private readonly Dictionary<(string, int), PropertyDefinition> _propertiesByName;

    public TypeDefinition(string name, TypeSource source, Dictionary<int, PropertyDefinition> properties)
    {
        Name = name;
        Source = source;
        Properties = properties;

        _propertiesByName = new Dictionary<(string, int), PropertyDefinition>(
            properties.Count,
            TupleComparer.Instance);

        foreach (var prop in properties.Values)
        {
            _propertiesByName[(prop.Name, prop.ArrayIndex)] = prop;
        }
    }

    /// <summary>
    /// Gets a property by name and array index (does not search parent types).
    /// </summary>
    public PropertyDefinition? GetProperty(string name, int arrayIndex = 0)
    {
        return _propertiesByName.GetValueOrDefault((name, arrayIndex));
    }

    /// <summary>
    /// Checks if this type or any ancestor matches the given type name.
    /// </summary>
    public bool IsA(string typeName)
    {
        var current = this;
        while (current != null)
        {
            if (current.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                return true;
            current = current.Super;
        }
        return false;
    }

    public override string ToString() => SuperName != null ? $"{Name} : {SuperName}" : Name;

    /// <summary>
    /// Case-insensitive tuple comparer for property lookup.
    /// </summary>
    private sealed class TupleComparer : IEqualityComparer<(string, int)>
    {
        public static readonly TupleComparer Instance = new();

        public bool Equals((string, int) x, (string, int) y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && x.Item2 == y.Item2;
        }

        public int GetHashCode((string, int) obj)
        {
            return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), obj.Item2);
        }
    }
}
