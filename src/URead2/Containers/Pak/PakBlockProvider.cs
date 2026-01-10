using URead2.Assets.Models;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Crypto;

namespace URead2.Containers.Pak;

/// <summary>
/// Provides raw block data from a pak file.
/// </summary>
internal sealed class PakBlockProvider : IBlockProvider
{
    private readonly PakEntry _entry;
    private readonly Stream _fileStream;
    private readonly List<CompressionBlock> _blocks;
    private bool _disposed;

    public long UncompressedSize => _entry.Size;
    public int BlockCount => _blocks.Count;
    public int BlockSize => (int)_entry.CompressionBlockSize;
    public CompressionMethod CompressionMethod { get; }
    public bool IsEncrypted => _entry.IsEncrypted;
    public int FirstBlockOffset => 0;

    public PakBlockProvider(PakEntry entry, CompressionMethod compressionMethod)
    {
        _entry = entry;
        _fileStream = File.OpenRead(entry.ContainerPath);
        CompressionMethod = compressionMethod;
        _blocks = BuildBlockList(entry);
    }

    // Base struct size prepended to each entry
    private const int BaseStructSize = 53;

    private static List<CompressionBlock> BuildBlockList(PakEntry entry)
    {
        var blocks = new List<CompressionBlock>();

        if (entry.CompressionMethod == null || entry.CompressionBlocks.Length == 0)
        {
            // Uncompressed - single block (still has 53-byte header prepended)
            blocks.Add(new CompressionBlock
            {
                CompressedOffset = entry.Offset + BaseStructSize,
                CompressedSize = (int)entry.Size,
                UncompressedSize = (int)entry.Size,
                UncompressedOffset = 0
            });
        }
        else
        {
            long uncompressedOffset = 0;
            int blockSize = (int)entry.CompressionBlockSize;

            foreach (var block in entry.CompressionBlocks)
            {
                int uncompSize = (int)Math.Min(blockSize, entry.Size - uncompressedOffset);

                blocks.Add(new CompressionBlock
                {
                    CompressedOffset = entry.Offset + block.Start,
                    CompressedSize = (int)block.Size,
                    UncompressedSize = uncompSize,
                    UncompressedOffset = uncompressedOffset
                });

                uncompressedOffset += uncompSize;
            }
        }

        return blocks;
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
        _fileStream.Seek(block.CompressedOffset, SeekOrigin.Begin);
        _fileStream.ReadExactly(buffer[..readSize]);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _fileStream.Dispose();
            _disposed = true;
        }
    }
}
