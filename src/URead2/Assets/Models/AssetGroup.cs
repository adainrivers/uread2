using URead2.Assets.Abstractions;

namespace URead2.Assets.Models;

/// <summary>
/// Groups related asset entries for a single asset (.uasset/.umap with optional .uexp/.ubulk).
/// </summary>
public class AssetGroup
{
    private readonly AssetRegistry _registry;
    private AssetMetadata? _metadata;
    private bool _metadataLoaded;

    public AssetGroup(AssetRegistry registry, string basePath, IAssetEntry asset, bool isMap,
        IAssetEntry? uExp = null, IAssetEntry? uBulk = null)
    {
        _registry = registry;
        BasePath = basePath;
        Asset = asset;
        IsMap = isMap;
        UExp = uExp;
        UBulk = uBulk;
    }

    public string BasePath { get; }
    public IAssetEntry Asset { get; }
    public bool IsMap { get; }
    public IAssetEntry? UExp { get; }
    public IAssetEntry? UBulk { get; }

    /// <summary>
    /// Asset metadata. Loaded lazily on first access, or pre-populated via PreloadAllMetadata.
    /// </summary>
    public AssetMetadata? Metadata
    {
        get
        {
            if (!_metadataLoaded)
            {
                _metadata = _registry.LoadMetadata(Asset);
                _metadataLoaded = true;
            }
            return _metadata;
        }
        internal set
        {
            _metadata = value;
            _metadataLoaded = true;
        }
    }
}
