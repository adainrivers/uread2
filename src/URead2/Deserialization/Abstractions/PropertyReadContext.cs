using URead2.Assets.Models;
using URead2.Deserialization.Properties;
using URead2.TypeResolution;

namespace URead2.Deserialization.Abstractions;

using ResolvedReference = URead2.Deserialization.ResolvedReference;

/// <summary>
/// Context for property reading operations.
/// Provides name table, type resolution, and cross-package reference resolution.
/// </summary>
public class PropertyReadContext
{
    /// <summary>
    /// Name table from the asset.
    /// </summary>
    public required string[] NameTable { get; init; }

    /// <summary>
    /// Type registry for type and schema lookup.
    /// </summary>
    public required TypeRegistry TypeRegistry { get; init; }

    /// <summary>
    /// Import table for resolving object references to external objects.
    /// </summary>
    public AssetImport[]? Imports { get; init; }

    /// <summary>
    /// Export table for resolving object references to local objects.
    /// </summary>
    public AssetExport[]? Exports { get; init; }

    /// <summary>
    /// The current package path (for building full object paths).
    /// </summary>
    public string? PackagePath { get; init; }

    /// <summary>
    /// Optional package resolver for cross-package lookups.
    /// When set, enables full resolution of imports to their actual exports.
    /// </summary>
    public IPackageResolver? PackageResolver { get; init; }

    /// <summary>
    /// Resolves a package index to an ObjectReference.
    /// </summary>
    public ObjectReference ResolveReference(int packageIndex)
    {
        if (packageIndex == 0)
            return new ObjectReference { Index = 0 };

        if (packageIndex < 0)
        {
            // Import reference
            int importIndex = -packageIndex - 1;
            if (Imports != null && importIndex >= 0 && importIndex < Imports.Length)
            {
                var import = Imports[importIndex];

                // Try cross-package resolution if resolver available
                ResolvedReference? resolved = null;
                if (PackageResolver != null)
                {
                    resolved = PackageResolver.ResolveImport(import);
                }

                return new ObjectReference
                {
                    Type = resolved?.Type ?? import.ClassName,
                    Name = resolved?.Name ?? import.Name,
                    Path = resolved?.PackagePath ?? import.PackageName,
                    Index = packageIndex,
                    ResolvedExport = resolved?.Export,
                    ResolvedMetadata = resolved?.Metadata,
                    IsFullyResolved = resolved?.IsResolved ?? false
                };
            }
        }
        else
        {
            // Export reference (local to this package)
            int exportIndex = packageIndex - 1;
            if (Exports != null && exportIndex >= 0 && exportIndex < Exports.Length)
            {
                var export = Exports[exportIndex];
                return new ObjectReference
                {
                    Type = export.ClassName,
                    Name = export.Name,
                    Path = PackagePath,
                    Index = packageIndex,
                    ResolvedExport = export,
                    IsFullyResolved = true
                };
            }
        }

        // Couldn't resolve, return with just the index
        return new ObjectReference { Index = packageIndex };
    }

    /// <summary>
    /// Gets an import by index (0-based).
    /// </summary>
    public AssetImport? GetImport(int importIndex)
    {
        if (Imports == null || importIndex < 0 || importIndex >= Imports.Length)
            return null;
        return Imports[importIndex];
    }

    /// <summary>
    /// Gets an export by index (0-based).
    /// </summary>
    public AssetExport? GetExport(int exportIndex)
    {
        if (Exports == null || exportIndex < 0 || exportIndex >= Exports.Length)
            return null;
        return Exports[exportIndex];
    }

    /// <summary>
    /// Resolves an import to its target export in another package.
    /// Returns null if PackageResolver is not set or import cannot be resolved.
    /// </summary>
    public ResolvedReference? ResolveImportToExport(int importIndex)
    {
        if (PackageResolver == null || Imports == null)
            return null;

        if (importIndex < 0 || importIndex >= Imports.Length)
            return null;

        return PackageResolver.ResolveImport(Imports[importIndex]);
    }
}
