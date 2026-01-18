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

    public byte ReadByte()
    {
        int b = _stream.ReadByte();
        if (b < 0)
            throw new EndOfStreamException();
        return (byte)b;
    }

    public bool ReadBool() => ReadByte() != 0;

    public short ReadInt16()
    {
        _stream.ReadExactly(_buffer, 0, 2);
        return BinaryPrimitives.ReadInt16LittleEndian(_buffer);
    }

    public ushort ReadUInt16()
    {
        _stream.ReadExactly(_buffer, 0, 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(_buffer);
    }

    public int ReadInt32()
    {
        _stream.ReadExactly(_buffer, 0, 4);
        return BinaryPrimitives.ReadInt32LittleEndian(_buffer);
    }

    public uint ReadUInt32()
    {
        _stream.ReadExactly(_buffer, 0, 4);
        return BinaryPrimitives.ReadUInt32LittleEndian(_buffer);
    }

    public long ReadInt64()
    {
        _stream.ReadExactly(_buffer, 0, 8);
        return BinaryPrimitives.ReadInt64LittleEndian(_buffer);
    }

    public ulong ReadUInt64()
    {
        _stream.ReadExactly(_buffer, 0, 8);
        return BinaryPrimitives.ReadUInt64LittleEndian(_buffer);
    }

    public float ReadFloat()
    {
        _stream.ReadExactly(_buffer, 0, 4);
        return BinaryPrimitives.ReadSingleLittleEndian(_buffer);
    }

    public double ReadDouble()
    {
        _stream.ReadExactly(_buffer, 0, 8);
        return BinaryPrimitives.ReadDoubleLittleEndian(_buffer);
    }

    public byte[] ReadBytes(int count)
    {
        var buffer = new byte[count];
        _stream.ReadExactly(buffer);
        return buffer;
    }

    public void ReadBytes(Span<byte> destination)
    {
        _stream.ReadExactly(destination);
    }

    /// <summary>
    /// Reads an Unreal Engine FString (length-prefixed string).
    /// </summary>
    public string ReadFString()
    {
        if (!TryReadFString(out var result))
            throw new InvalidDataException("Failed to read FString");
        return result;
    }

    /// <summary>
    /// Tries to read an Unreal Engine FString without throwing exceptions.
    /// Returns false if the string cannot be read (invalid length, not enough data, etc.).
    /// </summary>
    public bool TryReadFString(out string result)
    {
        result = string.Empty;

        // Need at least 4 bytes for length
        if (Remaining < 4)
            return false;

        int length = ReadInt32();

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
    /// Reads an Unreal Engine GUID (16 bytes).
    /// </summary>
    public Guid ReadGuid()
    {
        _stream.ReadExactly(_buffer, 0, 16);
        return new Guid(
            BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(0, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(4, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(6, 2)),
            _buffer[8], _buffer[9], _buffer[10], _buffer[11],
            _buffer[12], _buffer[13], _buffer[14], _buffer[15]
        );
    }

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
    {
        _stream.Seek(offset, origin);
    }

    public void Skip(long count)
    {
        _stream.Seek(count, SeekOrigin.Current);
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
