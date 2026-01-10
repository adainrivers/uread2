using URead2.Assets.Models;

namespace URead2.Assets.Abstractions;

/// <summary>
/// Reads metadata from an Unreal Engine asset file.
/// </summary>
public interface IAssetMetadataReader
{
    /// <summary>
    /// Reads asset metadata from a stream.
    /// </summary>
    /// <param name="stream">The asset data stream.</param>
    /// <param name="name">Asset name (for identification).</param>
    /// <returns>Asset metadata, or null if the format is invalid.</returns>
    AssetMetadata? ReadMetadata(Stream stream, string name);
}
