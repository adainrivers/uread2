using URead2.Assets.Models;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Containers.IoStore.Models;
using URead2.Crypto;

namespace URead2.Containers.IoStore;

/// <summary>
/// Provides raw block data from an IO Store container (.ucas file).
/// Uses memory-mapped file access for efficient reads.
/// </summary>
/// <remarks>
/// IO Store uses global compression blocks shared across all entries.
/// This provider calculates which blocks span the entry and reads from the mounted container.
/// </remarks>
internal sealed class IoStoreBlockProvider : IBlockProvider
{
    private readonly IoStoreEntry _entry;
    private readonly MountedContainer _mountedContainer;
    private readonly List<CompressionBlock> _blocks;
    private readonly List<byte> _blockCompressionMethodIndices;

    public long UncompressedSize => _entry.Size;
    public int BlockCount => _blocks.Count;
    public int BlockSize { get; }
    public CompressionMethod CompressionMethod { get; }
    public bool IsEncrypted { get; }
    public int FirstBlockOffset { get; }

    public IoStoreBlockProvider(IoStoreEntry entry, MountedContainer mountedContainer)
    {
        _entry = entry;
        _mountedContainer = mountedContainer ?? throw new ArgumentNullException(nameof(mountedContainer));

        var tocInfo = entry.TocInfo;
        BlockSize = tocInfo.CompressionBlockSize;
        IsEncrypted = tocInfo.IsEncrypted;

        if (BlockSize <= 0)
            BlockSize = 65536; // Default block size

        // Calculate which global blocks this entry spans
        int firstGlobalBlockIndex = (int)(_entry.Offset / BlockSize);
        int lastGlobalBlockIndex = _entry.Size > 0
            ? (int)((_entry.Offset + _entry.Size - 1) / BlockSize)
            : firstGlobalBlockIndex;

        // Calculate offset within first block where entry data starts
        FirstBlockOffset = (int)(_entry.Offset % BlockSize);

        // Determine compression method from first block (used as default)
        if (firstGlobalBlockIndex < tocInfo.CompressionBlocks.Count)
        {
            var firstBlock = tocInfo.CompressionBlocks[firstGlobalBlockIndex];
            CompressionMethod = GetCompressionMethod(firstBlock.CompressionMethodIndex, tocInfo);
        }
        else
        {
            CompressionMethod = CompressionMethod.None;
        }

        // Build block list for this entry, tracking per-block compression methods
        _blockCompressionMethodIndices = new List<byte>();
        _blocks = BuildBlockList(firstGlobalBlockIndex, lastGlobalBlockIndex, tocInfo, _blockCompressionMethodIndices);
    }

    private IoStoreTocInfo TocInfo => _entry.TocInfo;

    private static CompressionMethod GetCompressionMethod(byte methodIndex, IoStoreTocInfo tocInfo)
    {
        if (methodIndex == 0)
            return CompressionMethod.None;

        if (methodIndex < tocInfo.CompressionMethods.Count)
        {
            string methodName = tocInfo.CompressionMethods[methodIndex];
            return Decompressor.ParseMethod(methodName);
        }

        return CompressionMethod.Unknown;
    }

    private List<CompressionBlock> BuildBlockList(int firstBlockIndex, int lastBlockIndex, IoStoreTocInfo tocInfo, List<byte> compressionMethodIndices)
    {
        var blocks = new List<CompressionBlock>();
        long uncompressedOffset = 0;

        for (int globalIdx = firstBlockIndex; globalIdx <= lastBlockIndex && globalIdx < tocInfo.CompressionBlocks.Count; globalIdx++)
        {
            var tocBlock = tocInfo.CompressionBlocks[globalIdx];

            blocks.Add(new CompressionBlock
            {
                CompressedOffset = tocBlock.CompressedOffset,
                CompressedSize = tocBlock.CompressedSize,
                UncompressedSize = tocBlock.UncompressedSize,
                UncompressedOffset = uncompressedOffset
            });

            compressionMethodIndices.Add(tocBlock.CompressionMethodIndex);
            uncompressedOffset += tocBlock.UncompressedSize;
        }

        // Fallback: if no blocks found, treat as single uncompressed block
        if (blocks.Count == 0)
        {
            blocks.Add(new CompressionBlock
            {
                CompressedOffset = _entry.Offset,
                CompressedSize = (int)_entry.Size,
                UncompressedSize = (int)_entry.Size,
                UncompressedOffset = 0
            });
            compressionMethodIndices.Add(0); // No compression
        }

        return blocks;
    }

    public CompressionMethod GetBlockCompressionMethod(int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= _blockCompressionMethodIndices.Count)
            return CompressionMethod;

        return GetCompressionMethod(_blockCompressionMethodIndices[blockIndex], TocInfo);
    }

    public CompressionBlock GetBlock(int blockIndex) => _blocks[blockIndex];

    public int GetBlockReadSize(int blockIndex)
    {
        var block = _blocks[blockIndex];
        return IsEncrypted
            ? AesDecryptor.Align16(block.CompressedSize)
            : block.CompressedSize;
    }

    public byte[] ReadBlockRaw(int blockIndex)
    {
        int readSize = GetBlockReadSize(blockIndex);
        var data = new byte[readSize];
        ReadBlockRaw(blockIndex, data);
        return data;
    }

    public void ReadBlockRaw(int blockIndex, Span<byte> buffer)
    {
        var block = _blocks[blockIndex];
        int readSize = GetBlockReadSize(blockIndex);
        _mountedContainer.Read(block.CompressedOffset, buffer[..readSize]);
    }

    public void Dispose()
    {
        // MountedContainer is shared and owned by ContainerRegistry - don't dispose
    }
}
