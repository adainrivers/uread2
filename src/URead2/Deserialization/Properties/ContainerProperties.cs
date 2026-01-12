using System.Runtime.InteropServices;

namespace URead2.Deserialization.Properties;

/// <summary>
/// Array property value.
/// </summary>
public sealed class ArrayProperty : PropertyValue<PropertyValue[]>
{
    /// <summary>
    /// The inner element type name.
    /// </summary>
    public string? InnerType { get; }

    public ArrayProperty(PropertyValue[] value, string? innerType = null)
    {
        Value = value;
        InnerType = innerType;
    }

    public override string ToString() => $"[{Value?.Length ?? 0} elements]";
}

/// <summary>
/// Set property value.
/// </summary>
public sealed class SetProperty : PropertyValue<PropertyValue[]>
{
    /// <summary>
    /// The element type name.
    /// </summary>
    public string? ElementType { get; }

    public SetProperty(PropertyValue[] value, string? elementType = null)
    {
        Value = value;
        ElementType = elementType;
    }

    public override string ToString() => $"Set[{Value?.Length ?? 0} elements]";
}

/// <summary>
/// Map property value.
/// </summary>
public sealed class MapProperty : PropertyValue<MapEntry[]>
{
    /// <summary>
    /// The key type name.
    /// </summary>
    public string? KeyType { get; }

    /// <summary>
    /// The value type name.
    /// </summary>
    public string? ValueType { get; }

    public MapProperty(MapEntry[] value, string? keyType = null, string? valueType = null)
    {
        Value = value;
        KeyType = keyType;
        ValueType = valueType;
    }

    public override string ToString() => $"Map[{Value?.Length ?? 0} entries]";
}

/// <summary>
/// A key-value entry in a map.
/// </summary>
public readonly record struct MapEntry(PropertyValue Key, PropertyValue Value);

/// <summary>
/// Struct property value - contains nested properties.
/// </summary>
public sealed class StructProperty : PropertyValue<PropertyBag>
{
    /// <summary>
    /// The struct type name.
    /// </summary>
    public string? StructType { get; }

    public StructProperty(PropertyBag value, string? structType = null)
    {
        Value = value;
        StructType = structType;
    }

    public override string ToString() => StructType ?? "Struct";
}

/// <summary>
/// Optional property value (UE5+).
/// </summary>
public sealed class OptionalProperty : PropertyValue<PropertyValue?>
{
    /// <summary>
    /// The inner type name.
    /// </summary>
    public string? InnerType { get; }

    /// <summary>
    /// True if the optional has a value.
    /// </summary>
    public bool HasValue => Value != null;

    public OptionalProperty(PropertyValue? value, string? innerType = null)
    {
        Value = value;
        InnerType = innerType;
    }

    public override string? ToString() => HasValue ? Value!.ToString() : null;
}

/// <summary>
/// Enum property value.
/// </summary>
public sealed class EnumProperty : PropertyValue<string?>
{
    /// <summary>
    /// The enum type name.
    /// </summary>
    public string? EnumType { get; }

    public EnumProperty(string? value, string? enumType = null)
    {
        Value = value;
        EnumType = enumType;
    }

    public override string ToString() => Value ?? "None";
}

/// <summary>
/// A bag of properties (name -> value).
/// </summary>
public sealed class PropertyBag
{
    private readonly Dictionary<string, PropertyValue> _properties;

    public PropertyBag()
    {
        _properties = new Dictionary<string, PropertyValue>(StringComparer.Ordinal);
    }

    public PropertyBag(int capacity)
    {
        _properties = new Dictionary<string, PropertyValue>(capacity, StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets all property names.
    /// </summary>
    public IEnumerable<string> Names => _properties.Keys;

    /// <summary>
    /// Gets all properties.
    /// </summary>
    public IEnumerable<KeyValuePair<string, PropertyValue>> Properties => _properties;

    /// <summary>
    /// Gets the number of properties.
    /// </summary>
    public int Count => _properties.Count;

    /// <summary>
    /// Gets a property by name.
    /// </summary>
    public PropertyValue? this[string name] => _properties.GetValueOrDefault(name);

    /// <summary>
    /// Gets a typed property value by name.
    /// </summary>
    public T? Get<T>(string name) where T : PropertyValue
    {
        return _properties.GetValueOrDefault(name) as T;
    }

    /// <summary>
    /// Gets the raw value of a property by name.
    /// </summary>
    public object? GetValue(string name)
    {
        return _properties.GetValueOrDefault(name)?.GenericValue;
    }

    /// <summary>
    /// Gets the typed raw value of a property by name.
    /// </summary>
    public T? GetValue<T>(string name)
    {
        var prop = _properties.GetValueOrDefault(name);
        if (prop?.GenericValue is T value)
            return value;
        return default;
    }

    /// <summary>
    /// Adds a property.
    /// </summary>
    public void Add(string name, PropertyValue value)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_properties, name, out _);
        slot = value;
    }

    /// <summary>
    /// Tries to get a property by name.
    /// </summary>
    public bool TryGet(string name, out PropertyValue? value)
    {
        return _properties.TryGetValue(name, out value);
    }
}
