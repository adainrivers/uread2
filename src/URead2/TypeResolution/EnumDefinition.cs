namespace URead2.TypeResolution;

/// <summary>
/// Definition of an enum type.
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
    /// Enum values: numeric value -> name.
    /// </summary>
    public IReadOnlyDictionary<long, string> Values { get; }

    /// <summary>
    /// Reverse lookup: name -> numeric value.
    /// </summary>
    private readonly Dictionary<string, long> _valuesByName;

    public EnumDefinition(string name, TypeSource source, Dictionary<long, string> values)
    {
        Name = name;
        Source = source;
        Values = values;

        _valuesByName = new Dictionary<string, long>(values.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (value, valueName) in values)
        {
            _valuesByName.TryAdd(valueName, value);
        }
    }

    /// <summary>
    /// Gets the name for a numeric value.
    /// </summary>
    public string? GetName(long value)
    {
        return Values.GetValueOrDefault(value);
    }

    /// <summary>
    /// Gets the numeric value for a name.
    /// </summary>
    public long? GetValue(string name)
    {
        return _valuesByName.TryGetValue(name, out var value) ? value : null;
    }

    public override string ToString() => $"{Name} ({Values.Count} values)";
}
