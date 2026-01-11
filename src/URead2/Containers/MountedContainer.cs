using System.IO.MemoryMappedFiles;
using URead2.Assets.Abstractions;

namespace URead2.Containers;

/// <summary>
/// Represents a mounted container with memory-mapped file access and cached entries.
/// Thread-safe for concurrent reads.
/// </summary>
public sealed class MountedContainer : IDisposable
{
    private readonly MemoryMappedFile _mappedFile;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _fileSize;
    private bool _disposed;

    /// <summary>
    /// Path to the data file (.ucas for IoStore, .pak for Pak).
    /// </summary>
    public string DataPath { get; }

    /// <summary>
    /// Cached entries from this container.
    /// </summary>
    public IReadOnlyList<IAssetEntry> Entries { get; }

    public MountedContainer(string dataPath, IReadOnlyList<IAssetEntry> entries)
    {
        DataPath = dataPath ?? throw new ArgumentNullException(nameof(dataPath));
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));

        var fileInfo = new FileInfo(dataPath);
        _fileSize = fileInfo.Length;

        _mappedFile = MemoryMappedFile.CreateFromFile(
            dataPath,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);

        _accessor = _mappedFile.CreateViewAccessor(0, _fileSize, MemoryMappedFileAccess.Read);
    }

    /// <summary>
    /// Reads data from the container at the specified offset.
    /// Thread-safe for concurrent reads.
    /// </summary>
    public unsafe void Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (offset < 0 || offset + buffer.Length > _fileSize)
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"Read range [{offset}, {offset + buffer.Length}) is outside file bounds [0, {_fileSize})");

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            new ReadOnlySpan<byte>(ptr + offset, buffer.Length).CopyTo(buffer);
        }
        finally
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        }
    }

    /// <summary>
    /// Reads data from the container at the specified offset into a new array.
    /// Thread-safe for concurrent reads.
    /// </summary>
    public byte[] Read(long offset, int length)
    {
        var buffer = new byte[length];
        Read(offset, buffer);
        return buffer;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _accessor.Dispose();
            _mappedFile.Dispose();
            _disposed = true;
        }
    }
}
