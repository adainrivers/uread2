using System.Buffers.Binary;
using System.Text;

namespace URead2.IO;

/// <summary>
/// Stream-based binary reader for Unreal Engine data formats.
/// </summary>
public class ArchiveReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly byte[] _buffer = new byte[256];
    private bool _disposed;

    public ArchiveReader(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
    }

    public ArchiveReader(string filePath)
        : this(File.OpenRead(filePath), leaveOpen: false)
    {
    }

    public long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public long Length => _stream.Length;

    public long Remaining => _stream.Length - _stream.Position;

    public bool TryReadByte(out byte value)
    {
        if (Remaining < 1)
        {
            value = 0;
            return false;
        }
        int b = _stream.ReadByte();
        value = (byte)b;
        return b >= 0;
    }

    public bool TryReadBool(out bool value)
    {
        if (TryReadByte(out byte b))
        {
            value = b != 0;
            return true;
        }
        value = false;
        return false;
    }

    public bool TryReadInt16(out short value)
    {
        if (Remaining < 2)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 2);
        value = BinaryPrimitives.ReadInt16LittleEndian(_buffer);
        return true;
    }

    public bool TryReadUInt16(out ushort value)
    {
        if (Remaining < 2)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 2);
        value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
        return true;
    }

    public bool TryReadInt32(out int value)
    {
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 4);
        value = BinaryPrimitives.ReadInt32LittleEndian(_buffer);
        return true;
    }

    public bool TryReadUInt32(out uint value)
    {
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 4);
        value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer);
        return true;
    }

    public bool TryReadInt64(out long value)
    {
        if (Remaining < 8)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 8);
        value = BinaryPrimitives.ReadInt64LittleEndian(_buffer);
        return true;
    }

    public bool TryReadUInt64(out ulong value)
    {
        if (Remaining < 8)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 8);
        value = BinaryPrimitives.ReadUInt64LittleEndian(_buffer);
        return true;
    }

    public bool TryReadFloat(out float value)
    {
        if (Remaining < 4)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 4);
        value = BinaryPrimitives.ReadSingleLittleEndian(_buffer);
        return true;
    }

    public bool TryReadDouble(out double value)
    {
        if (Remaining < 8)
        {
            value = 0;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 8);
        value = BinaryPrimitives.ReadDoubleLittleEndian(_buffer);
        return true;
    }

    public bool TryReadBytes(int count, out byte[] value)
    {
        if (Remaining < count)
        {
            value = [];
            return false;
        }
        value = new byte[count];
        _stream.ReadExactly(value);
        return true;
    }

    public bool TryReadBytes(Span<byte> destination)
    {
        if (Remaining < destination.Length)
            return false;
        _stream.ReadExactly(destination);
        return true;
    }

    public bool TrySkip(long count)
    {
        if (Remaining < count)
            return false;
        _stream.Seek(count, SeekOrigin.Current);
        return true;
    }

    /// <summary>
    /// Tries to read an Unreal Engine FString without throwing exceptions.
    /// Returns false if the string cannot be read (invalid length, not enough data, etc.).
    /// </summary>
    public bool TryReadFString(out string result)
    {
        result = string.Empty;

        if (!TryReadInt32(out int length))
            return false;

        if (length == 0)
            return true; // Empty string is valid

        if (length < 0)
        {
            // Unicode string (UTF-16LE)
            int charCount = -length;
            int byteCount = charCount * 2;

            if (byteCount > 1024 * 1024 || byteCount > Remaining)
            {
                // Seek back since we read the length but can't read the string
                _stream.Seek(-4, SeekOrigin.Current);
                return false;
            }

            var bytes = byteCount <= _buffer.Length
                ? _buffer.AsSpan(0, byteCount)
                : new byte[byteCount];

            _stream.ReadExactly(bytes);

            // Find null terminator (2-byte aligned for UTF-16)
            int actualBytes = byteCount;
            for (int i = 0; i < byteCount - 1; i += 2)
            {
                if (bytes[i] == 0 && bytes[i + 1] == 0)
                {
                    actualBytes = i;
                    break;
                }
            }

            result = Encoding.Unicode.GetString(bytes[..actualBytes]);
            return true;
        }
        else
        {
            // ASCII/UTF-8 string
            if (length > 1024 * 1024 || length > Remaining)
            {
                // Seek back since we read the length but can't read the string
                _stream.Seek(-4, SeekOrigin.Current);
                return false;
            }

            var bytes = length <= _buffer.Length
                ? _buffer.AsSpan(0, length)
                : new byte[length];

            _stream.ReadExactly(bytes);

            // Find null terminator
            int actualLength = bytes.IndexOf((byte)0);
            if (actualLength < 0)
                actualLength = length;

            result = Encoding.UTF8.GetString(bytes[..actualLength]);
            return true;
        }
    }

    /// <summary>
    /// Tries to read an Unreal Engine GUID (16 bytes).
    /// </summary>
    public bool TryReadGuid(out Guid value)
    {
        if (Remaining < 16)
        {
            value = Guid.Empty;
            return false;
        }
        _stream.ReadExactly(_buffer, 0, 16);
        value = new Guid(
            BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(0, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(6, 2)),
            _buffer[8], _buffer[9], _buffer[10], _buffer[11],
            _buffer[12], _buffer[13], _buffer[14], _buffer[15]
        );
        return true;
    }

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        _stream.Seek(offset, origin);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing && !_leaveOpen)
            {
                _stream.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
