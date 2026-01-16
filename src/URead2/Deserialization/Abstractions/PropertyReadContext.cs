using URead2.Assets.Models;
using URead2.Deserialization.Properties;
using URead2.TypeResolution;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Context for property reading operations.
/// Provides name table, type resolution, and object reference resolution.
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
    /// Imports are pre-resolved during metadata loading.
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
    /// Resolves a package index to an ObjectReference.
    /// </summary>
    public ObjectReference ResolveReference(int packageIndex)
    {
        if (packageIndex == 0)
            return new ObjectReference { Index = 0 };

        if (packageIndex < 0)
        {
            // Import reference - imports are pre-resolved during metadata loading
            int importIndex = -packageIndex - 1;
            if (Imports != null && importIndex >= 0 && importIndex < Imports.Length)
            {
                var import = Imports[importIndex];

                return new ObjectReference
                {
                    Type = import.ClassName,
                    Name = import.Name,
                    Path = import.PackageName,
                    Index = packageIndex,
                    IsFullyResolved = import.IsResolved
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
}
