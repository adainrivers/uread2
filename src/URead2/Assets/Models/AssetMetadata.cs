namespace URead2.Assets.Models;

/// <summary>
/// Metadata extracted from an Unreal Engine asset file.
/// Works for both traditional .uasset (PAK) and Zen packages (IO Store).
/// </summary>
public record AssetMetadata(
    string Name,
    string[] NameTable,
    AssetExport[] Exports,
    AssetImport[] Imports,
    /// <summary>
    /// For IO Store packages, the header size to add to export offsets.
    /// For PAK packages, this is 0 (offsets are absolute).
    /// </summary>
    int CookedHeaderSize = 0,
    /// <summary>
    /// True if the package uses unversioned property serialization.
    /// </summary>
    bool IsUnversioned = false,
    /// <summary>
    /// Public export hashes for PackageImport resolution (IO Store only).
    /// Index by ImportedPublicExportHashIndex to get the hash, then match against export's PublicExportHash.
    /// </summary>
    ulong[]? ImportedPublicExportHashes = null,
    /// <summary>
    /// Imported package names/paths for PackageImport resolution (IO Store UE5.3+).
    /// Index by ImportedPackageIndex to get the package path.
    /// </summary>
    string[]? ImportedPackageNames = null
);
