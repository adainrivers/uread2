namespace URead2.Containers.IoStore.Models;

/// <summary>
/// Shared TOC information needed for reading IO Store entries.
/// </summary>
public record IoStoreTocInfo(
    int CompressionBlockSize,
    bool IsEncrypted,
    IReadOnlyList<IoStoreCompressionBlock> CompressionBlocks,
    IReadOnlyList<string> CompressionMethods
);
