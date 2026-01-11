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
    int CookedHeaderSize = 0
);
