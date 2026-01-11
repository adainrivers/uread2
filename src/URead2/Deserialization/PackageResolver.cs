using System.Collections.Concurrent;
using URead2.Assets;
using URead2.Assets.Models;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Properties;

namespace URead2.Deserialization;

/// <summary>
/// Resolves cross-package references using the AssetRegistry.
/// Provides lazy loading of package metadata and O(1) export lookups.
/// </summary>
public class PackageResolver : IPackageResolver
{
    // Cache for resolved imports: "PackageName.ObjectName" -> ResolvedReference
    private readonly ConcurrentDictionary<string, ResolvedReference?> _importCache = new(StringComparer.OrdinalIgnoreCase);

    // Package path mappings: import package name -> asset path
    private readonly ConcurrentDictionary<string, string?> _packagePathCache = new(StringComparer.OrdinalIgnoreCase);

    private static AssetRegistry Assets => AssetRegistry.Instance
        ?? throw new InvalidOperationException("AssetRegistry not initialized");

    /// <summary>
    /// Resolves an import to its actual export in another package.
    /// Uses the preloaded export index for O(1) lookups when available.
    /// </summary>
    /// <param name="import">The import to resolve.</param>
    /// <returns>Resolved reference with full metadata, or null if not found.</returns>
    public ResolvedReference? ResolveImport(AssetImport import)
    {
        if (string.IsNullOrEmpty(import.Name))
            return null;

        // Build lookup key
        var lookupKey = BuildImportKey(import);
        if (string.IsNullOrEmpty(lookupKey))
            return null;

        // Check cache first
        if (_importCache.TryGetValue(lookupKey, out var cached))
            return cached;

        // Try export index first (O(1) if preloaded)
        var resolved = TryResolveFromExportIndex(import);

        // Cache and return (even null to avoid repeated lookups)
        _importCache.TryAdd(lookupKey, resolved);
        return resolved;
    }

    /// <summary>
    /// Resolves an import using the global export index.
    /// </summary>
    private ResolvedReference? TryResolveFromExportIndex(AssetImport import)
    {
        // Convert import package path to asset path format
        var packagePath = NormalizePackagePath(import.PackageName);
        if (string.IsNullOrEmpty(packagePath))
            return null;

        // Try direct lookup: "PackagePath.ObjectName"
        var exportPath = $"{packagePath}.{import.Name}";
        var result = Assets.ResolveExport(exportPath);

        if (result.HasValue)
        {
            return new ResolvedReference
            {
                Type = result.Value.Export.ClassName,
                Name = result.Value.Export.Name,
                PackagePath = result.Value.Metadata.Name,
                Export = result.Value.Export,
                Metadata = result.Value.Metadata,
                IsResolved = true
            };
        }

        // Try with class suffix variations (_C, _GEN_VARIABLE)
        foreach (var suffix in new[] { "_C", "_GEN_VARIABLE", "Blueprint" })
        {
            exportPath = $"{packagePath}.{import.Name}{suffix}";
            result = Assets.ResolveExport(exportPath);
            if (result.HasValue)
            {
                return new ResolvedReference
                {
                    Type = result.Value.Export.ClassName,
                    Name = result.Value.Export.Name,
                    PackagePath = result.Value.Metadata.Name,
                    Export = result.Value.Export,
                    Metadata = result.Value.Metadata,
                    IsResolved = true
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Normalizes a package name to asset path format.
    /// E.g., "/Game/Characters/Player" -> "Game/Characters/Player"
    /// </summary>
    public static string? NormalizePackagePath(string? packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return null;

        // Remove leading slash
        var path = packageName.TrimStart('/');

        // Handle /Script/ packages (engine types)
        if (path.StartsWith("Script/", StringComparison.OrdinalIgnoreCase))
            return null; // Script packages aren't in the asset registry

        // Handle /Engine/ packages
        if (path.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase))
            path = "Engine/Content/" + path[7..];

        return path;
    }

    /// <summary>
    /// Builds a cache key for an import.
    /// </summary>
    private static string? BuildImportKey(AssetImport import)
    {
        if (string.IsNullOrEmpty(import.PackageName) || string.IsNullOrEmpty(import.Name))
            return null;

        return $"{import.PackageName}.{import.Name}";
    }

    /// <summary>
    /// Resolves an export in a specific package by name.
    /// </summary>
    /// <param name="packagePath">The package path (e.g., "Game/Characters/Player").</param>
    /// <param name="exportName">The export name to find.</param>
    /// <returns>Resolved reference, or null if not found.</returns>
    public ResolvedReference? ResolveExportByName(string packagePath, string exportName)
    {
        var exportPath = $"{packagePath}.{exportName}";
        var result = Assets.ResolveExport(exportPath);

        if (result.HasValue)
        {
            return new ResolvedReference
            {
                Type = result.Value.Export.ClassName,
                Name = result.Value.Export.Name,
                PackagePath = result.Value.Metadata.Name,
                Export = result.Value.Export,
                Metadata = result.Value.Metadata,
                IsResolved = true
            };
        }

        return null;
    }

    /// <summary>
    /// Gets the metadata for a package by path.
    /// Uses cached metadata when available.
    /// </summary>
    public AssetMetadata? GetPackageMetadata(string packagePath)
    {
        // Try to find asset in registry
        var normalizedPath = NormalizePackagePath(packagePath);
        if (normalizedPath == null)
            return null;

        // Check metadata cache directly
        var cached = Assets.GetCachedMetadata(normalizedPath + ".uasset");
        if (cached != null)
            return cached;

        cached = Assets.GetCachedMetadata(normalizedPath + ".umap");
        return cached;
    }

    /// <summary>
    /// Number of cached import resolutions.
    /// </summary>
    public int ImportCacheCount => _importCache.Count;

    /// <summary>
    /// Clears the import resolution cache.
    /// </summary>
    public void ClearCache()
    {
        _importCache.Clear();
        _packagePathCache.Clear();
    }
}
