namespace URead2.Containers.Pak.Models;

/// <summary>
/// Compression block range in a pak entry.
/// </summary>
public readonly record struct PakCompressionBlock(long Start, long End)
{
    public long Size => End - Start;
}
