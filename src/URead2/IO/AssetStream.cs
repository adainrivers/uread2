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

    private long _position;
    private int _currentBlockIndex = -1;
    private byte[]? _currentBlockData;
    private int _currentBlockDataLength; // Actual data length (pooled array may be larger)
    private bool _currentBlockDataPooled;
    private bool _disposed;

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

        if (_blockProvider.IsEncrypted && _aesKey == null)
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
            int targetBlock = FindBlockForPosition(_position);

            if (targetBlock != _currentBlockIndex)
                LoadBlock(targetBlock);

            var block = _blockProvider.GetBlock(targetBlock);
            int blockOffset = (int)(_position - block.UncompressedOffset);

            if (targetBlock == 0)
                blockOffset += _blockProvider.FirstBlockOffset;

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
            int targetBlock = FindBlockForPosition(_position);

            if (targetBlock != _currentBlockIndex)
                LoadBlock(targetBlock);

            var block = _blockProvider.GetBlock(targetBlock);
            int blockOffset = (int)(_position - block.UncompressedOffset);

            if (targetBlock == 0)
                blockOffset += _blockProvider.FirstBlockOffset;

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
        if (_blockProvider.BlockSize > 0)
        {
            int estimated = (int)(position / _blockProvider.BlockSize);
            estimated = Math.Min(estimated, _blockProvider.BlockCount - 1);

            // Verify the estimate is correct
            var block = _blockProvider.GetBlock(estimated);
            long blockEnd = block.UncompressedOffset + block.UncompressedSize;

            if (position >= block.UncompressedOffset && position < blockEnd)
                return estimated;

            // Estimate was wrong (variable-size blocks), fall through to linear search
        }

        // Linear search fallback for variable-size blocks
        // Note: Could use binary search for O(log n) but linear is fine for typical block counts
        for (int i = 0; i < _blockProvider.BlockCount; i++)
        {
            var block = _blockProvider.GetBlock(i);
            if (position < block.UncompressedOffset + block.UncompressedSize)
                return i;
        }

        return _blockProvider.BlockCount - 1;
    }

    private void LoadBlock(int blockIndex)
    {
        Log.Verbose("Loading block {BlockIndex} of {BlockCount}", blockIndex, _blockProvider.BlockCount);

        // Return previous block data to pool
        ReturnCurrentBlockData();

        var block = _blockProvider.GetBlock(blockIndex);
        int rawReadSize = _blockProvider.GetBlockReadSize(blockIndex);

        // Rent buffer for raw/compressed data
        byte[] rawBuffer = ArrayPool<byte>.Shared.Rent(rawReadSize);
        try
        {
            _blockProvider.ReadBlockRaw(blockIndex, rawBuffer);

            if (_blockProvider.IsEncrypted && _aesKey != null)
                _decryptor.Decrypt(rawBuffer.AsSpan(0, rawReadSize), _aesKey);

            ReadOnlySpan<byte> compressedData = rawBuffer.AsSpan(0, block.CompressedSize);
            var blockCompressionMethod = _blockProvider.GetBlockCompressionMethod(blockIndex);

            if (blockCompressionMethod != CompressionMethod.None)
            {
                // Rent buffer for decompressed data
                _currentBlockData = ArrayPool<byte>.Shared.Rent(block.UncompressedSize);
                _currentBlockDataLength = block.UncompressedSize;
                _currentBlockDataPooled = true;
                _decompressor.Decompress(compressedData, _currentBlockData.AsSpan(0, block.UncompressedSize), blockCompressionMethod);
            }
            else
            {
                // Uncompressed - rent and copy (can't reuse rawBuffer as it gets returned)
                _currentBlockData = ArrayPool<byte>.Shared.Rent(block.CompressedSize);
                _currentBlockDataLength = block.CompressedSize;
                _currentBlockDataPooled = true;
                compressedData.CopyTo(_currentBlockData);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rawBuffer);
        }

        _currentBlockIndex = blockIndex;
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
