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

    public TypeDefinition(string name, TypeSource source, Dictionary<int, PropertyDefinition> properties)
    {
        Name = name;
        Source = source;
        Properties = properties;
    }

    public override string ToString() => SuperName != null ? $"{Name} : {SuperName}" : Name;
}
