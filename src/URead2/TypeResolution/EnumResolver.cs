namespace URead2.TypeResolution;

/// <summary>
/// Provides lookup operations for enum definitions.
/// </summary>
public static class EnumResolver
{
    /// <summary>
    /// Gets the name for a numeric value from an enum definition.
    /// </summary>
    public static string? GetName(EnumDefinition? enumDef, long value)
    {
        if (enumDef?.Values == null)
            return null;

        return enumDef.Values.GetValueOrDefault(value);
    }

    /// <summary>
    /// Gets the numeric value for a name from an enum definition.
    /// </summary>
    public static long? GetValue(EnumDefinition? enumDef, string name)
    {
        if (enumDef?.ValuesByName == null)
            return null;

        return enumDef.ValuesByName.TryGetValue(name, out var value) ? value : null;
    }
}
