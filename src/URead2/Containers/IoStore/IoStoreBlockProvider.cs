using URead2.Assets.Models;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Containers.IoStore.Models;
using URead2.Crypto;

namespace URead2.Containers.IoStore;

/// <summary>
/// Provides raw block data from an IO Store container (.ucas file).
/// </summary>
/// <remarks>
/// IO Store uses global compression blocks shared across all entries.
/// This provider calculates which blocks span the entry and reads from the .ucas file.
/// </remarks>
internal sealed class IoStoreBlockProvider : IBlockProvider
{
    private readonly IoStoreEntry _entry;
    private readonly Stream _containerStream;
    private readonly List<CompressionBlock> _blocks;
    private readonly List<byte> _blockCompressionMethodIndices;
    private readonly int _firstGlobalBlockIndex;
    private bool _disposed;

    public long UncompressedSize => _entry.Size;
    public int BlockCount => _blocks.Count;
    public int BlockSize { get; }
    public CompressionMethod CompressionMethod { get; }
    public bool IsEncrypted { get; }
    public int FirstBlockOffset { get; }

    public IoStoreBlockProvider(IoStoreEntry entry)
    {
        _entry = entry;
        _containerStream = File.OpenRead(entry.ContainerPath);

        var tocInfo = entry.TocInfo;
        BlockSize = tocInfo.CompressionBlockSize;
        IsEncrypted = tocInfo.IsEncrypted;

        if (BlockSize <= 0)
            BlockSize = 65536; // Default block size

        // Calculate which global blocks this entry spans
        _firstGlobalBlockIndex = (int)(_entry.Offset / BlockSize);
        int lastGlobalBlockIndex = _entry.Size > 0
            ? (int)((_entry.Offset + _entry.Size - 1) / BlockSize)
            : _firstGlobalBlockIndex;

        // Calculate offset within first block where entry data starts
        FirstBlockOffset = (int)(_entry.Offset % BlockSize);

        // Determine compression method from first block (used as default)
        if (_firstGlobalBlockIndex < tocInfo.CompressionBlocks.Count)
        {
            var firstBlock = tocInfo.CompressionBlocks[_firstGlobalBlockIndex];
            CompressionMethod = GetCompressionMethod(firstBlock.CompressionMethodIndex, tocInfo);
        }
        else
        {
            CompressionMethod = CompressionMethod.None;
        }

        // Build block list for this entry, tracking per-block compression methods
        _blockCompressionMethodIndices = new List<byte>();
        _blocks = BuildBlockList(_firstGlobalBlockIndex, lastGlobalBlockIndex, tocInfo, _blockCompressionMethodIndices);
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
        _containerStream.Seek(block.CompressedOffset, SeekOrigin.Begin);
        _containerStream.ReadExactly(buffer[..readSize]);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _containerStream.Dispose();
            _disposed = true;
        }
    }
}
