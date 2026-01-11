namespace URead2.Deserialization.Properties;

/// <summary>
/// Context for property reading.
/// </summary>
public enum ReadContext : byte
{
    /// <summary>Normal tagged property.</summary>
    Normal,
    /// <summary>Element inside an array.</summary>
    Array,
    /// <summary>Key or value inside a map.</summary>
    Map,
    /// <summary>Zero/default value (unversioned).</summary>
    Zero
}

/// <summary>
/// Base class for all property values.
/// </summary>
public abstract class PropertyValue
{
    /// <summary>
    /// Gets the value as an object.
    /// </summary>
    public abstract object? GenericValue { get; }

    /// <summary>
    /// Gets the value as type T, or default if not compatible.
    /// </summary>
    public T? GetValue<T>()
    {
        if (GenericValue is T value)
            return value;
        return default;
    }
}

/// <summary>
/// Typed property value.
/// </summary>
public abstract class PropertyValue<T> : PropertyValue
{
    public T? Value { get; protected set; }

    public override object? GenericValue => Value;

    public override string? ToString() => Value?.ToString();
}
