namespace URead2.Deserialization;

/// <summary>
/// Fatal error codes that abort export deserialization.
/// These indicate structural corruption or missing critical data.
/// </summary>
public enum ReadErrorCode
{
    /// <summary>
    /// Unexpected exception during deserialization.
    /// </summary>
    UnexpectedException,

    /// <summary>
    /// Export SerialSize is invalid (negative or too large).
    /// </summary>
    InvalidSerialSize,

    /// <summary>
    /// Export data is in .uexp but no .uexp file exists.
    /// </summary>
    MissingUExpFile,

    /// <summary>
    /// No export data reader configured in profile.
    /// </summary>
    NoExportDataReader,

    /// <summary>
    /// Stream I/O error while reading export data.
    /// </summary>
    StreamIOError,

    /// <summary>
    /// Unversioned header has too many fragments (likely delta-serialized).
    /// </summary>
    TooManyFragments,

    /// <summary>
    /// Required struct schema not found in type registry.
    /// </summary>
    MissingStructSchema,

    /// <summary>
    /// Stream position went past expected bounds.
    /// </summary>
    StreamOverrun,
}
