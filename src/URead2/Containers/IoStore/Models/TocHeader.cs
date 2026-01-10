namespace URead2.Containers.IoStore.Models;

/// <summary>
/// IO Store TOC header information.
/// </summary>
public record TocHeader(
    int Version,
    int HeaderSize,
    int EntryCount,
    int CompressedBlockCount,
    int CompressionMethodCount,
    int CompressionMethodLength,
    int CompressionBlockSize,
    int DirectoryIndexSize,
    int PerfectHashSeedsCount,
    int ChunksWithoutPerfectHashCount,
    bool IsIndexed,
    bool IsEncrypted,
    bool IsSigned
);
