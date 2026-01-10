namespace URead2.Containers.IoStore.Models;

/// <summary>
/// Compression block in an IO Store container.
/// </summary>
public readonly record struct IoStoreCompressionBlock(
    long CompressedOffset,
    int CompressedSize,
    int UncompressedSize,
    byte CompressionMethodIndex)
{
    public bool IsCompressed => CompressionMethodIndex != 0;
}
