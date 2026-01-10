namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// An enum definition from .usmap.
/// </summary>
public sealed class UsmapEnum
{
    /// <summary>
    /// Enum name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Enum values indexed by their numeric value.
    /// </summary>
    public IReadOnlyDictionary<long, string> Values { get; }

    public UsmapEnum(string name, Dictionary<long, string> values)
    {
        Name = name;
        Values = values;
    }

    /// <summary>
    /// Gets the name for an enum value.
    /// </summary>
    public string? GetValueName(long value)
    {
        return Values.GetValueOrDefault(value);
    }

    public override string ToString() => $"{Name} ({Values.Count} values)";
}
