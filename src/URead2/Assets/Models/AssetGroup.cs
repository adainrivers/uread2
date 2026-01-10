using URead2.Assets.Abstractions;

namespace URead2.Assets.Models;

/// <summary>
/// Groups related asset entries for a single asset (.uasset/.umap with optional .uexp/.ubulk).
/// </summary>
public record AssetGroup(
    string BasePath,
    IAssetEntry Asset,
    bool IsMap,
    IAssetEntry? UExp = null,
    IAssetEntry? UBulk = null
);
