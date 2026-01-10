using URead2.Assets.Abstractions;
using URead2.Containers.IoStore.Models;

namespace URead2.Assets.Models;

/// <summary>
/// An entry in an IO Store container.
/// </summary>
public record IoStoreEntry(
    string ContainerPath,
    string Path,
    long Offset,
    long Size,
    IoStoreTocInfo TocInfo
) : IAssetEntry;
