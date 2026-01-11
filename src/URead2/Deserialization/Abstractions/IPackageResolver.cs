using URead2.Assets.Models;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Resolves cross-package references.
/// </summary>
public interface IPackageResolver
{
    /// <summary>
    /// Resolves an import to its actual export in another package.
    /// </summary>
    ResolvedReference? ResolveImport(AssetImport import);

    /// <summary>
    /// Resolves an export in a specific package by name.
    /// </summary>
    ResolvedReference? ResolveExportByName(string packagePath, string exportName);

    /// <summary>
    /// Gets the metadata for a package by path.
    /// </summary>
    AssetMetadata? GetPackageMetadata(string packagePath);

    /// <summary>
    /// Clears the resolution cache.
    /// </summary>
    void ClearCache();
}
