namespace URead2.Deserialization;

/// <summary>
/// Warning codes for non-fatal issues during deserialization.
/// These indicate schema drift, unknown types, or recoverable data issues.
/// </summary>
public enum DiagnosticCode
{
    /// <summary>
    /// Type definition not found in type registry.
    /// </summary>
    MissingTypeDef,

    /// <summary>
    /// Flattened properties not found for type.
    /// </summary>
    MissingFlattenedProperties,

    /// <summary>
    /// Unknown property kind encountered.
    /// </summary>
    UnknownPropertyKind,

    /// <summary>
    /// Unknown tagged property type.
    /// </summary>
    UnknownTaggedPropertyType,

    /// <summary>
    /// Enum definition not found in type registry.
    /// </summary>
    UnknownEnum,

    /// <summary>
    /// Enum value not found in enum definition.
    /// </summary>
    UnknownEnumValue,

    /// <summary>
    /// Invalid FName index in name table.
    /// </summary>
    InvalidFNameIndex,

    /// <summary>
    /// Array/Set/Map count is negative or exceeds sanity limit.
    /// </summary>
    InvalidCollectionCount,

    /// <summary>
    /// Property size mismatch - consumed bytes differ from declared size.
    /// Stream position was corrected.
    /// </summary>
    SizeMismatch,

    /// <summary>
    /// Schema index exceeds available properties.
    /// </summary>
    SchemaIndexOutOfRange,
}
