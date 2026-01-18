using System.Buffers;

namespace URead2.Assets.Models;

/// <summary>
/// Wraps export data in a pooled buffer. Dispose to return buffer to pool.
/// This is a class (not struct) to prevent pool corruption from value copies.
/// </summary>
public sealed class ExportData : IDisposable
{
    private byte[]? _buffer;
    private readonly int _length;
    private bool _disposed;

    /// <summary>
    /// The export binary data.
    /// </summary>
    public ReadOnlySpan<byte> Data
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer.AsSpan(0, _length);
        }
    }

    /// <summary>
    /// Creates a MemoryStream over the buffer without copying.
    /// </summary>
    public MemoryStream AsStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new MemoryStream(_buffer ?? [], 0, _length, writable: false);
    }

    /// <summary>
    /// Length of the export data in bytes.
    /// </summary>
    public int Length => _length;

    /// <summary>
    /// True if no data is present.
    /// </summary>
    public bool IsEmpty => _length == 0;

    internal ExportData(byte[]? buffer, int length)
    {
        _buffer = buffer;
        _length = length;
    }

    /// <summary>
    /// Returns the pooled buffer. Must be called when done with the data.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed && _buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
            _disposed = true;
        }
    }
}
