using URead2.Assets.Abstractions;
using URead2.Profiles.Abstractions;

namespace URead2.Containers.Abstractions;

/// <summary>
/// Reads a container (pak or IO Store) and exposes its entries.
/// </summary>
public interface IContainerReader
{
    IEnumerable<IAssetEntry> ReadEntries(string filePath, IProfile profile, byte[]? aesKey = null);
}
