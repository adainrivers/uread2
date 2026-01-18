namespace URead2.Deserialization;

/// <summary>
/// Error information from a failed export deserialization.
/// </summary>
public readonly record struct ExportReadError(
    ReadErrorCode ErrorCode,
    long Position,
    string? ExportName,
    string? AssetPath,
    string? Detail);

/// <summary>
/// Exception thrown when export deserialization fails with a fatal error.
/// </summary>
public class ExportReadException : Exception
{
    /// <summary>
    /// The fatal error code.
    /// </summary>
    public ReadErrorCode ErrorCode { get; }

    /// <summary>
    /// Stream position where the error occurred.
    /// </summary>
    public long Position { get; }

    /// <summary>
    /// The export name that failed to deserialize.
    /// </summary>
    public string? ExportName { get; }

    /// <summary>
    /// The asset path containing the export.
    /// </summary>
    public string? AssetPath { get; }

    public ExportReadException(ReadErrorCode errorCode, long position, string? detail = null)
        : base(FormatMessage(errorCode, position, null, null, detail))
    {
        ErrorCode = errorCode;
        Position = position;
    }

    public ExportReadException(ReadErrorCode errorCode, long position, string? exportName, string? assetPath, string? detail = null)
        : base(FormatMessage(errorCode, position, exportName, assetPath, detail))
    {
        ErrorCode = errorCode;
        Position = position;
        ExportName = exportName;
        AssetPath = assetPath;
    }

    public ExportReadException(ReadErrorCode errorCode, long position, Exception innerException)
        : base(FormatMessage(errorCode, position, null, null, innerException.Message), innerException)
    {
        ErrorCode = errorCode;
        Position = position;
    }

    private static string FormatMessage(ReadErrorCode errorCode, long position, string? exportName, string? assetPath, string? detail)
    {
        var location = exportName != null
            ? $"export '{exportName}' in '{assetPath}'"
            : "export";

        var msg = $"Failed to deserialize {location}: {errorCode} at position {position}";
        if (!string.IsNullOrEmpty(detail))
            msg += $" ({detail})";

        return msg;
    }
}
