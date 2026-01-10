using System.Buffers;

namespace URead2.Assets.Models;

/// <summary>
/// Wraps export data in a pooled buffer. Dispose to return buffer to pool.
/// </summary>
public readonly struct ExportData : IDisposable
{
    private readonly byte[]? _buffer;
    private readonly int _length;

    /// <summary>
    /// The export binary data.
    /// </summary>
    public ReadOnlySpan<byte> Data => _buffer.AsSpan(0, _length);

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
        if (_buffer != null)
            ArrayPool<byte>.Shared.Return(_buffer);
    }
}
