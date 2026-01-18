using System.Buffers;
using Serilog;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Crypto;

namespace URead2.IO;

/// <summary>
/// Read-only stream for asset data with decompression and decryption support.
/// Reads blocks on-demand to handle large files without loading everything into memory.
/// Uses ArrayPool to reduce GC pressure from block allocations.
/// </summary>
public sealed class AssetStream : Stream
{
    private readonly IBlockProvider _blockProvider;
    private readonly Decompressor _decompressor;
    private readonly IDecryptor _decryptor;
    private readonly byte[]? _aesKey;
    private readonly int _blockSize;
    private readonly int _blockCount;
    private readonly int _firstBlockOffset;

    private long _position;
    private int _currentBlockIndex = -1;
    private byte[]? _currentBlockData;
    private int _currentBlockDataLength; // Actual data length (pooled array may be larger)
    private bool _currentBlockDataPooled;
    private bool _disposed;

    // Cached block bounds to avoid repeated GetBlock calls
    private long _currentBlockStart;
    private long _currentBlockEnd;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _blockProvider.UncompressedSize;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Position cannot be negative");
            if (value > Length)
                throw new ArgumentOutOfRangeException(nameof(value), "Position cannot exceed stream length");
            _position = value;
        }
    }

    public AssetStream(IBlockProvider blockProvider, Decompressor decompressor, IDecryptor decryptor, byte[]? aesKey)
    {
        _blockProvider = blockProvider;
        _decompressor = decompressor;
        _decryptor = decryptor;
        _aesKey = aesKey;

        // Cache frequently accessed values
        _blockSize = blockProvider.BlockSize;
        _blockCount = blockProvider.BlockCount;
        _firstBlockOffset = blockProvider.FirstBlockOffset;

        if (blockProvider.IsEncrypted && _aesKey == null)
            throw new InvalidOperationException("Asset is encrypted but no AES key provided");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateReadArguments(buffer, offset, count);

        if (_disposed)
            throw new ObjectDisposedException(nameof(AssetStream));

        if (_position >= Length)
            return 0;

        int totalRead = 0;
        while (count > 0 && _position < Length)
        {
            // Fast path: check if position is still within cached block bounds
            if (_currentBlockIndex < 0 || _position < _currentBlockStart || _position >= _currentBlockEnd)
            {
                int targetBlock = FindBlockForPosition(_position);
                LoadBlock(targetBlock);
            }

            int blockOffset = (int)(_position - _currentBlockStart);
            if (_currentBlockIndex == 0)
                blockOffset += _firstBlockOffset;

            int availableInBlock = _currentBlockDataLength - blockOffset;
            if (availableInBlock <= 0)
                break;

            int toRead = Math.Min(count, availableInBlock);
            Buffer.BlockCopy(_currentBlockData!, blockOffset, buffer, offset, toRead);

            _position += toRead;
            offset += toRead;
            count -= toRead;
            totalRead += toRead;
        }

        return totalRead;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AssetStream));

        if (_position >= Length)
            return 0;

        int totalRead = 0;
        while (buffer.Length > 0 && _position < Length)
        {
            // Fast path: check if position is still within cached block bounds
            if (_currentBlockIndex < 0 || _position < _currentBlockStart || _position >= _currentBlockEnd)
            {
                int targetBlock = FindBlockForPosition(_position);
                LoadBlock(targetBlock);
            }

            int blockOffset = (int)(_position - _currentBlockStart);
            if (_currentBlockIndex == 0)
                blockOffset += _firstBlockOffset;

            int availableInBlock = _currentBlockDataLength - blockOffset;
            if (availableInBlock <= 0)
                break;

            int toRead = Math.Min(buffer.Length, availableInBlock);
            _currentBlockData.AsSpan(blockOffset, toRead).CopyTo(buffer);

            _position += toRead;
            buffer = buffer.Slice(toRead);
            totalRead += toRead;
        }

        return totalRead;
    }

    private int FindBlockForPosition(long position)
    {
        // Fast path: use division for uniformly-sized blocks
        if (_blockSize > 0)
        {
            int estimated = (int)(position / _blockSize);
            if (estimated >= _blockCount)
                estimated = _blockCount - 1;
            return estimated;
        }

        // Linear search fallback for variable-size blocks
        for (int i = 0; i < _blockCount; i++)
        {
            var block = _blockProvider.GetBlock(i);
            if (position < block.UncompressedOffset + block.UncompressedSize)
                return i;
        }

        return _blockCount - 1;
    }

    private void LoadBlock(int blockIndex)
    {
        // Return previous block data to pool
        ReturnCurrentBlockData();

        var block = _blockProvider.GetBlock(blockIndex);
        int rawReadSize = _blockProvider.GetBlockReadSize(blockIndex);

        // Rent buffer for raw/compressed data
        byte[] rawBuffer = ArrayPool<byte>.Shared.Rent(rawReadSize);
        bool returnRawBuffer = true;
        try
        {
            _blockProvider.ReadBlockRaw(blockIndex, rawBuffer);

            if (_blockProvider.IsEncrypted && _aesKey != null)
                _decryptor.Decrypt(rawBuffer.AsSpan(0, rawReadSize), _aesKey);

            ReadOnlySpan<byte> compressedData = rawBuffer.AsSpan(0, block.CompressedSize);
            var blockCompressionMethod = _blockProvider.GetBlockCompressionMethod(blockIndex);

            if (blockCompressionMethod != CompressionMethod.None)
            {
                // Rent buffer for decompressed data - only assign to _currentBlockData after successful decompression
                byte[] decompressBuffer = ArrayPool<byte>.Shared.Rent(block.UncompressedSize);
                try
                {
                    _decompressor.Decompress(compressedData, decompressBuffer.AsSpan(0, block.UncompressedSize), blockCompressionMethod);
                    _currentBlockData = decompressBuffer;
                    _currentBlockDataLength = block.UncompressedSize;
                    _currentBlockDataPooled = true;
                }
                catch
                {
                    ArrayPool<byte>.Shared.Return(decompressBuffer);
                    throw;
                }
            }
            else
            {
                // Uncompressed - reuse rawBuffer
                _currentBlockData = rawBuffer;
                _currentBlockDataLength = block.CompressedSize;
                _currentBlockDataPooled = true;
                returnRawBuffer = false;
            }
        }
        finally
        {
            if (returnRawBuffer)
            {
                ArrayPool<byte>.Shared.Return(rawBuffer);
            }
        }

        // Cache block bounds for fast path in Read
        _currentBlockIndex = blockIndex;
        _currentBlockStart = block.UncompressedOffset;
        _currentBlockEnd = block.UncompressedOffset + block.UncompressedSize;
    }

    private void ReturnCurrentBlockData()
    {
        if (_currentBlockData != null && _currentBlockDataPooled)
        {
            ArrayPool<byte>.Shared.Return(_currentBlockData);
        }
        _currentBlockData = null;
        _currentBlockDataLength = 0;
        _currentBlockDataPooled = false;
    }

    private static void ValidateReadArguments(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (buffer.Length - offset < count)
            throw new ArgumentException("Buffer too small");
    }

    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
    {
        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };

        Position = newPosition;
        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                ReturnCurrentBlockData();
                _blockProvider.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
