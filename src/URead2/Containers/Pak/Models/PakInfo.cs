namespace URead2.Containers.Pak.Models;

/// <summary>
/// Pak file header information.
/// </summary>
public record PakInfo(
    int Version,
    long IndexOffset,
    long IndexSize,
    bool IsIndexEncrypted,
    string[] CompressionMethods
);
