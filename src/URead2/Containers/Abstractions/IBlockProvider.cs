using URead2.Compression;

namespace URead2.Containers.Abstractions;

/// <summary>
/// Represents a compression block within an asset.
/// </summary>
public readonly struct CompressionBlock
{
    /// <summary>
    /// Offset of compressed data within the container file.
    /// </summary>
    public long CompressedOffset { get; init; }

    /// <summary>
    /// Size of compressed data (may be aligned for encryption).
    /// </summary>
    public int CompressedSize { get; init; }

    /// <summary>
    /// Size of uncompressed data.
    /// </summary>
    public int UncompressedSize { get; init; }

    /// <summary>
    /// Logical offset of this block in the decompressed stream.
    /// </summary>
    public long UncompressedOffset { get; init; }
}

/// <summary>
/// Provides raw block data from a container (pak or IO Store).
/// </summary>
public interface IBlockProvider : IDisposable
{
    /// <summary>
    /// Total uncompressed size of the asset.
    /// </summary>
    long UncompressedSize { get; }

    /// <summary>
    /// Number of compression blocks (1 if uncompressed).
    /// </summary>
    int BlockCount { get; }

    /// <summary>
    /// Size of each uncompressed block (except possibly the last).
    /// </summary>
    int BlockSize { get; }

    /// <summary>
    /// Default compression method for this asset.
    /// Note: Use GetBlockCompressionMethod for per-block compression when blocks may differ.
    /// </summary>
    CompressionMethod CompressionMethod { get; }

    /// <summary>
    /// Whether the asset data is encrypted.
    /// </summary>
    bool IsEncrypted { get; }

    /// <summary>
    /// Gets the compression method for a specific block.
    /// Different blocks may use different compression methods (e.g., last block may be uncompressed).
    /// </summary>
    CompressionMethod GetBlockCompressionMethod(int blockIndex) => CompressionMethod;

    /// <summary>
    /// Offset within the first decompressed block where entry data starts.
    /// </summary>
    int FirstBlockOffset { get; }

    /// <summary>
    /// Gets block metadata by index.
    /// </summary>
    CompressionBlock GetBlock(int blockIndex);

    /// <summary>
    /// Reads raw (compressed/encrypted) block data from the container.
    /// </summary>
    byte[] ReadBlockRaw(int blockIndex);

    /// <summary>
    /// Reads raw block data into a caller-provided buffer.
    /// Buffer must be at least GetBlockReadSize(blockIndex) bytes.
    /// </summary>
    void ReadBlockRaw(int blockIndex, Span<byte> buffer);

    /// <summary>
    /// Gets the required buffer size for reading a block (aligned for encryption if needed).
    /// </summary>
    int GetBlockReadSize(int blockIndex);
}
