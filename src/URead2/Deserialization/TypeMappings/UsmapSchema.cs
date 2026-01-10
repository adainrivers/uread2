namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// A class or struct schema from .usmap.
/// </summary>
public sealed class UsmapSchema
{
    /// <summary>
    /// Schema name (class or struct name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Super type name (parent class), or null if none.
    /// </summary>
    public string? SuperType { get; }

    /// <summary>
    /// Total property count including inherited properties.
    /// </summary>
    public ushort PropertyCount { get; }

    /// <summary>
    /// Properties indexed by schema index.
    /// </summary>
    public IReadOnlyDictionary<int, UsmapProperty> Properties { get; }

    /// <summary>
    /// Properties indexed by (name, arrayIndex) for fast lookup.
    /// </summary>
    private readonly Dictionary<(string, int), UsmapProperty> _propertiesByName;

    public UsmapSchema(
        string name,
        string? superType,
        ushort propertyCount,
        Dictionary<int, UsmapProperty> properties)
    {
        Name = name;
        SuperType = superType;
        PropertyCount = propertyCount;
        Properties = properties;

        _propertiesByName = new Dictionary<(string, int), UsmapProperty>(
            StringComparer.OrdinalIgnoreCase.ToTupleComparer());

        foreach (var prop in properties.Values)
        {
            _propertiesByName[(prop.Name, prop.ArrayIndex)] = prop;
        }
    }

    /// <summary>
    /// Gets a property by name and array index.
    /// </summary>
    public UsmapProperty? GetProperty(string name, int arrayIndex = 0)
    {
        return _propertiesByName.GetValueOrDefault((name, arrayIndex));
    }

    public override string ToString() => SuperType != null ? $"{Name} : {SuperType}" : Name;
}

/// <summary>
/// Helper to create tuple comparers.
/// </summary>
internal static class ComparerExtensions
{
    public static IEqualityComparer<(string, int)> ToTupleComparer(this StringComparer comparer)
    {
        return new TupleComparer(comparer);
    }

    private sealed class TupleComparer : IEqualityComparer<(string, int)>
    {
        private readonly StringComparer _stringComparer;

        public TupleComparer(StringComparer stringComparer)
        {
            _stringComparer = stringComparer;
        }

        public bool Equals((string, int) x, (string, int) y)
        {
            return _stringComparer.Equals(x.Item1, y.Item1) && x.Item2 == y.Item2;
        }

        public int GetHashCode((string, int) obj)
        {
            return HashCode.Combine(_stringComparer.GetHashCode(obj.Item1), obj.Item2);
        }
    }
}
