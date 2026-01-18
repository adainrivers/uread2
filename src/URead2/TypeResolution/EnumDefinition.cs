namespace URead2.TypeResolution;

/// <summary>
/// Definition of an enum type (DTO - no business logic).
/// </summary>
public sealed class EnumDefinition
{
    /// <summary>
    /// Enum name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Source of this enum definition.
    /// </summary>
    public TypeSource Source { get; }

    /// <summary>
    /// Underlying numeric type (e.g., "UInt8", "Int32").
    /// </summary>
    public string? UnderlyingType { get; }

    /// <summary>
    /// Enum values: numeric value -> name.
    /// </summary>
    public IReadOnlyDictionary<long, string> Values { get; }

    /// <summary>
    /// Reverse lookup: name -> numeric value.
    /// </summary>
    public IReadOnlyDictionary<string, long> ValuesByName { get; }

    public EnumDefinition(string name, TypeSource source, Dictionary<long, string> values, string? underlyingType = null)
    {
        Name = name;
        Source = source;
        Values = values;
        UnderlyingType = underlyingType;

        var valuesByName = new Dictionary<string, long>(values.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (value, valueName) in values)
        {
            valuesByName.TryAdd(valueName, value);
        }
        ValuesByName = valuesByName;
    }

    public override string ToString() => $"{Name} ({Values.Count} values)";
}
