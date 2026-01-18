namespace URead2.TypeResolution;

/// <summary>
/// Combined export of all types and enums.
/// </summary>
public sealed class TypeRegistryExport
{
    public required List<TypeInfo> Types { get; init; }
    public required List<EnumInfo> Enums { get; init; }
}
