using URead2.Assets;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers;
using URead2.Profiles.Abstractions;

namespace URead2;

/// <summary>
/// Root orchestrator for reading Unreal Engine game assets.
/// Provides a unified API that combines container and asset operations.
/// </summary>
public class UEReader : IDisposable
{
    private readonly ContainerRegistry _containers;
    private readonly AssetRegistry _assets;
    private readonly IProfile _profile;
    private bool _disposed;

    public UEReader(RuntimeConfig config, IProfile profile)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(profile);

        _profile = profile;
        _containers = new ContainerRegistry(config, profile);
        _assets = new AssetRegistry(config, profile);
    }

    /// <summary>
    /// Gets direct access to the container registry.
    /// </summary>
    public ContainerRegistry Containers => _containers;

    /// <summary>
    /// Gets direct access to the asset registry.
    /// </summary>
    public AssetRegistry Assets => _assets;

    /// <summary>
    /// Gets all entries from all containers.
    /// </summary>
    public IEnumerable<IAssetEntry> GetEntries(Func<string, bool>? containerPathFilter = null)
        => _containers.GetEntries(containerPathFilter);

    /// <summary>
    /// Gets all assets grouped with their companion files.
    /// </summary>
    public IEnumerable<AssetGroup> GetAssets(Func<string, bool>? containerPathFilter = null)
        => _assets.GroupAssets(_containers.GetEntries(containerPathFilter));

    /// <summary>
    /// Opens a stream to read entry content.
    /// </summary>
    public Stream OpenRead(IAssetEntry entry) => _assets.OpenRead(entry);

    /// <summary>
    /// Reads asset metadata.
    /// </summary>
    public AssetMetadata? ReadMetadata(IAssetEntry entry) => _assets.ReadMetadata(entry);

    /// <summary>
    /// Reads asset metadata.
    /// </summary>
    public AssetMetadata? ReadMetadata(AssetGroup asset) => _assets.ReadMetadata(asset);

    /// <summary>
    /// Reads export binary data.
    /// </summary>
    public ExportData ReadExportData(IAssetEntry entry, AssetExport export)
        => _assets.ReadExportData(entry, export);

    /// <summary>
    /// Reads export binary data.
    /// </summary>
    public ExportData ReadExportData(AssetGroup asset, AssetExport export)
        => _assets.ReadExportData(asset, export);

    public void Dispose()
    {
        if (!_disposed)
        {
            (_profile as IDisposable)?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
