namespace URead2.Assets.Abstractions;

/// <summary>
/// An entry in a container (pak or IO Store). Pure data - no reading logic.
/// </summary>
public interface IAssetEntry
{
    /// <summary>
    /// Virtual path of the entry.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// Uncompressed size in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Path to the container file.
    /// </summary>
    string ContainerPath { get; }

    /// <summary>
    /// Offset within the container file.
    /// </summary>
    long Offset { get; }
}
