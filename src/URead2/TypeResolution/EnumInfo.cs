using System.Text.Json.Serialization;

namespace URead2.TypeResolution;

/// <summary>
/// Serializable representation of an enum definition.
/// </summary>
public sealed class EnumInfo
{
    public required string Name { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TypeSource Source { get; init; }

    public required List<EnumValueInfo> Values { get; init; }
}
