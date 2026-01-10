using URead2.Assets.Abstractions;
using URead2.Containers.Pak.Models;

namespace URead2.Assets.Models;

/// <summary>
/// An entry in a pak file.
/// </summary>
public record PakEntry(
    string Path,
    string ContainerPath,
    long Offset,
    long Size,
    long CompressedSize,
    bool IsEncrypted,
    string? CompressionMethod,
    uint CompressionBlockSize,
    PakCompressionBlock[] CompressionBlocks
) : IAssetEntry;
