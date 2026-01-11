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
        _assets = new AssetRegistry(config, profile, _containers);
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
    /// Uses cached results. Optional filter applies to AssetGroup.BasePath.
    /// </summary>
    public IEnumerable<AssetGroup> GetAssets(Func<string, bool>? pathFilter = null)
        => pathFilter == null
            ? _assets.GetAssetGroups()
            : _assets.GetAssetGroups().Where(g => pathFilter(g.BasePath));

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

    /// <summary>
    /// Preloads all asset metadata in parallel and builds the global export index.
    /// Call once at startup for fastest cross-package access.
    /// </summary>
    public void PreloadAllMetadata(int? maxDegreeOfParallelism = null, Action<int, int>? progress = null)
        => _assets.PreloadAllMetadata(maxDegreeOfParallelism, progress);

    /// <summary>
    /// Resolves an export by its full path. O(1) lookup after PreloadAllMetadata.
    /// </summary>
    public (AssetMetadata Metadata, AssetExport Export)? ResolveExport(string exportPath)
        => _assets.ResolveExport(exportPath);

    /// <summary>
    /// Number of cached metadata entries.
    /// </summary>
    public int MetadataCacheCount => _assets.MetadataCacheCount;

    /// <summary>
    /// Number of indexed exports for cross-package resolution.
    /// </summary>
    public int ExportIndexCount => _assets.ExportIndexCount;

    public void Dispose()
    {
        if (!_disposed)
        {
            _containers.Dispose();
            (_profile as IDisposable)?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
