using System.Text.Json.Serialization;

namespace URead2.TypeResolution;

/// <summary>
/// Serializable representation of a type definition.
/// </summary>
public sealed class TypeInfo
{
    /// <summary>
    /// Fully qualified name (e.g., "/Game/Blueprints/BP_Player.BP_Player_C" or "Actor" for runtime types).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Fully qualified super name, or null if no parent.
    /// </summary>
    public string? SuperName { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TypeSource Source { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TypeKind Kind { get; init; }

    /// <summary>
    /// Properties defined directly on this type (not inherited).
    /// </summary>
    public List<PropertyInfo>? Properties { get; init; }
}
