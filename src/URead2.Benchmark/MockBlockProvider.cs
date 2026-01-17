using System;
using System.Buffers;
using URead2.Compression;
using URead2.Containers.Abstractions;

namespace URead2.Benchmark;

public class MockBlockProvider : IBlockProvider
{
    private readonly int _blockSize;
    private readonly int _blockCount;
    private readonly byte[] _data;

    public MockBlockProvider(int blockSize, int blockCount)
    {
        _blockSize = blockSize;
        _blockCount = blockCount;
        _data = new byte[blockSize];
        new Random(42).NextBytes(_data);
    }

    public long UncompressedSize => (long)_blockSize * _blockCount;

    public int BlockCount => _blockCount;

    public int BlockSize => _blockSize;

    public CompressionMethod CompressionMethod => CompressionMethod.None;

    public bool IsEncrypted => false;

    public int FirstBlockOffset => 0;

    public CompressionBlock GetBlock(int blockIndex)
    {
        long offset = (long)blockIndex * _blockSize;
        return new CompressionBlock
        {
            CompressedOffset = offset,
            CompressedSize = _blockSize,
            UncompressedSize = _blockSize,
            UncompressedOffset = offset
        };
    }

    public byte[] ReadBlockRaw(int blockIndex)
    {
        var buffer = new byte[_blockSize];
        _data.CopyTo(buffer, 0);
        return buffer;
    }

    public void ReadBlockRaw(int blockIndex, Span<byte> buffer)
    {
        _data.AsSpan().CopyTo(buffer);
    }

    public int GetBlockReadSize(int blockIndex)
    {
        return _blockSize;
    }

    public void Dispose()
    {
    }
}
