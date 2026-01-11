namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// .usmap file format version.
/// </summary>
public enum EUsmapVersion : byte
{
    /// <summary>
    /// Initial format.
    /// </summary>
    Initial = 0,

    /// <summary>
    /// Adds optional asset package versioning.
    /// </summary>
    PackageVersioning = 1,

    /// <summary>
    /// 16-bit wide names in name map.
    /// </summary>
    LongFName = 2,

    /// <summary>
    /// 16-bit enum entry count.
    /// </summary>
    LargeEnums = 3,

    /// <summary>
    /// Explicit enum values instead of assuming ordinal.
    /// </summary>
    ExplicitEnumValues = 4,

    Latest = ExplicitEnumValues
}
