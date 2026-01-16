using System.Buffers;
using System.Collections.Concurrent;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers;
using URead2.Profiles.Abstractions;

namespace URead2.Assets;

/// <summary>
/// Registry that provides asset-level operations: reading, metadata, and export data.
/// </summary>
public class AssetRegistry
{
    private readonly RuntimeConfig _config;

    // Cached reader references
    private readonly IAssetEntryReader? _pakEntryReader;
    private readonly IAssetEntryReader? _ioStoreEntryReader;
    private readonly IAssetMetadataReader? _uAssetReader;
    private readonly IAssetMetadataReader? _zenPackageReader;
    private readonly IExportDataReader? _exportDataReader;

    // Cached grouped assets
    private List<AssetGroup>? _cachedAssetGroups;
    private readonly object _groupLock = new();

    // Cached metadata (thread-safe)
    private readonly ConcurrentDictionary<string, AssetMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    // Global export index for cross-package resolution: "PackagePath.ExportName" -> ExportIndexEntry
    private ConcurrentDictionary<string, ExportIndexEntry>? _exportIndex;

    // Name-based index for type lookup: "ExportName" -> List<ExportIndexEntry>
    private Dictionary<string, List<ExportIndexEntry>>? _exportNameIndex;

    // Hash index for PackageImport resolution: PublicExportHash -> ExportInfo
    private Dictionary<ulong, ExportHashInfo>? _publicExportHashIndex;

    /// <summary>
    /// Info stored in hash index for import resolution.
    /// </summary>
    public readonly record struct ExportHashInfo(string Name, string ClassName, string PackagePath, int ExportIndex);

    // Singleton instance for global access
    public static AssetRegistry? Instance { get; private set; }

    private static ContainerRegistry Containers => ContainerRegistry.Instance
        ?? throw new InvalidOperationException("ContainerRegistry not initialized");

    private AssetRegistry(RuntimeConfig config, IProfile profile)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Cache reader references from profile
        _pakEntryReader = profile.PakEntryReader;
        _ioStoreEntryReader = profile.IoStoreEntryReader;
        _uAssetReader = profile.UAssetReader;
        _zenPackageReader = profile.ZenPackageReader;
        _exportDataReader = profile.ExportDataReader;
    }

    /// <summary>
    /// Initializes the singleton instance.
    /// </summary>
    public static AssetRegistry Initialize(RuntimeConfig config, IProfile profile)
    {
        Instance = new AssetRegistry(config, profile);
        return Instance;
    }

    /// <summary>
    /// Gets all assets grouped with their companion files (.uexp, .ubulk).
    /// Results are cached after first call.
    /// </summary>
    public IReadOnlyList<AssetGroup> GetAssetGroups()
    {
        if (_cachedAssetGroups != null)
            return _cachedAssetGroups;

        lock (_groupLock)
        {
            if (_cachedAssetGroups != null)
                return _cachedAssetGroups;

            _cachedAssetGroups = GroupAssetsCore(Containers.GetEntries());
            return _cachedAssetGroups;
        }
    }

    /// <summary>
    /// Groups entries with their companion files (.uexp, .ubulk).
    /// </summary>
    public IEnumerable<AssetGroup> GroupAssets(IEnumerable<IAssetEntry> entries)
    {
        return GroupAssetsCore(entries);
    }

    private static List<AssetGroup> GroupAssetsCore(IEnumerable<IAssetEntry> entries)
    {
        var byBasePath = new Dictionary<string, (IAssetEntry? Asset, IAssetEntry? UExp, IAssetEntry? UBulk, bool IsMap)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var path = entry.Path;
            var ext = Path.GetExtension(path);
            var basePath = path[..^ext.Length];

            if (!byBasePath.TryGetValue(basePath, out var group))
                group = (null, null, null, false);

            if (ext.Equals(".uasset", StringComparison.OrdinalIgnoreCase))
                group = (entry, group.UExp, group.UBulk, false);
            else if (ext.Equals(".umap", StringComparison.OrdinalIgnoreCase))
                group = (entry, group.UExp, group.UBulk, true);
            else if (ext.Equals(".uexp", StringComparison.OrdinalIgnoreCase))
                group = (group.Asset, entry, group.UBulk, group.IsMap);
            else if (ext.Equals(".ubulk", StringComparison.OrdinalIgnoreCase))
                group = (group.Asset, group.UExp, entry, group.IsMap);
            else
                continue; // Skip non-asset files

            byBasePath[basePath] = group;
        }

        var result = new List<AssetGroup>(byBasePath.Count);
        foreach (var (basePath, group) in byBasePath)
        {
            if (group.Asset != null)
                result.Add(new AssetGroup(basePath, group.Asset, group.IsMap, group.UExp, group.UBulk));
        }
        return result;
    }

    /// <summary>
    /// Opens a stream to read the entry's content.
    /// Uses memory-mapped container for efficient reads.
    /// </summary>
    public Stream OpenRead(IAssetEntry entry)
    {
        var container = Containers.GetMountedContainer(entry.ContainerPath);

        return entry switch
        {
            PakEntry => _pakEntryReader?.OpenRead(entry, _config.AesKey, container)
                ?? throw new InvalidOperationException("No pak entry reader configured"),
            IoStoreEntry => _ioStoreEntryReader?.OpenRead(entry, _config.AesKey, container)
                ?? throw new InvalidOperationException("No IO Store entry reader configured"),
            _ => throw new NotSupportedException($"Unknown entry type: {entry.GetType().Name}")
        };
    }

    /// <summary>
    /// Reads asset metadata (names, imports, exports) from an entry.
    /// Results are cached for subsequent calls. Thread-safe.
    /// Only works for .uasset/.umap files.
    /// </summary>
    public AssetMetadata? ReadMetadata(IAssetEntry entry)
    {
        // Check cache first
        if (_metadataCache.TryGetValue(entry.Path, out var cached))
            return cached;

        // Read from container
        using var stream = OpenRead(entry);
        var metadata = entry switch
        {
            PakEntry => _uAssetReader?.ReadMetadata(stream, entry.Path),
            IoStoreEntry => _zenPackageReader?.ReadMetadata(stream, entry.Path),
            _ => null
        };

        // Cache result (only if non-null)
        if (metadata != null)
            _metadataCache.TryAdd(entry.Path, metadata);

        return metadata;
    }

    /// <summary>
    /// Reads asset metadata (names, imports, exports) from an asset group.
    /// Results are cached for subsequent calls. Thread-safe.
    /// </summary>
    public AssetMetadata? ReadMetadata(AssetGroup asset)
    {
        return ReadMetadata(asset.Asset);
    }

    /// <summary>
    /// Gets cached metadata without reading from container.
    /// Returns null if not cached.
    /// </summary>
    public AssetMetadata? GetCachedMetadata(string path)
    {
        return _metadataCache.TryGetValue(path, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// Gets the number of cached metadata entries.
    /// </summary>
    public int MetadataCacheCount => _metadataCache.Count;

    /// <summary>
    /// Clears the metadata cache and export index.
    /// </summary>
    public void ClearMetadataCache()
    {
        _metadataCache.Clear();
        _exportIndex?.Clear();
        _exportIndex = null;
        _exportNameIndex?.Clear();
        _exportNameIndex = null;
        _publicExportHashIndex?.Clear();
        _publicExportHashIndex = null;
    }

    /// <summary>
    /// Preloads all asset metadata in parallel and builds the global export index.
    /// Call this once at startup for fastest cross-package access.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">Max parallel reads. Default uses all processors.</param>
    /// <param name="progress">Optional progress callback (assetsLoaded, totalAssets).</param>
    public void PreloadAllMetadata(int? maxDegreeOfParallelism = null, Action<int, int>? progress = null)
    {
        var assets = GetAssetGroups();
        var totalCount = assets.Count;
        var loadedCount = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount
        };

        Parallel.ForEach(assets, options, asset =>
        {
            ReadMetadata(asset);
            var count = Interlocked.Increment(ref loadedCount);
            progress?.Invoke(count, totalCount);
        });

        // Build export index after all metadata is loaded
        BuildExportIndex();
    }

    /// <summary>
    /// Builds the global export index from cached metadata.
    /// Called automatically by PreloadAllMetadata.
    /// </summary>
    public void BuildExportIndex()
    {
        _exportIndex = new ConcurrentDictionary<string, ExportIndexEntry>(StringComparer.OrdinalIgnoreCase);
        _exportNameIndex = new Dictionary<string, List<ExportIndexEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, metadata) in _metadataCache)
        {
            // Get package path without extension
            var packagePath = path;
            var lastDot = packagePath.LastIndexOf('.');
            if (lastDot > 0)
                packagePath = packagePath[..lastDot];

            for (int i = 0; i < metadata.Exports.Length; i++)
            {
                var export = metadata.Exports[i];
                var entry = new ExportIndexEntry(metadata, i);

                // Key format: "PackagePath.ExportName"
                var exportKey = $"{packagePath}.{export.Name}";
                _exportIndex.TryAdd(exportKey, entry);

                // Also index by name only for type lookups
                if (!_exportNameIndex.TryGetValue(export.Name, out var list))
                {
                    list = [];
                    _exportNameIndex[export.Name] = list;
                }
                list.Add(entry);
            }
        }

        // Build hash index and resolve class/super names
        BuildPublicExportHashIndex();
        ResolveScriptImportRefs();
        ResolveLocalClassNames();
        ResolveExternalClassNames();

        // Pass 2: Resolve all imports using the export hash index
        ResolveImports();
    }

    /// <summary>
    /// Resolves ScriptImport type references using the ScriptObjectIndex.
    /// These are references to engine classes like Actor, SceneComponent, etc.
    /// </summary>
    private void ResolveScriptImportRefs()
    {
        var scriptObjectIndex = Containers.ScriptObjectIndex;
        if (scriptObjectIndex == null)
            return;

        foreach (var (_, metadata) in _metadataCache)
        {
            foreach (var export in metadata.Exports)
            {
                // Resolve ScriptImport class reference
                if (export.ClassRef is { Type: Models.PackageObjectType.ScriptImport } classRef)
                {
                    var raw = ((ulong)classRef.Type << 62) | classRef.Value;
                    var resolved = scriptObjectIndex.ResolveImportWithModule(raw);
                    if (resolved.HasValue)
                    {
                        var modulePath = resolved.Value.ModuleName != null
                            ? $"/Script/{resolved.Value.ModuleName}"
                            : "/Script";
                        export.Class = new ResolvedRef
                        {
                            ClassName = "Class",
                            Name = resolved.Value.ObjectName,
                            PackagePath = modulePath,
                            ExportIndex = -1
                        };
                    }
                }

                // Resolve ScriptImport super reference
                if (export.SuperRef is { Type: Models.PackageObjectType.ScriptImport } superRef)
                {
                    var raw = ((ulong)superRef.Type << 62) | superRef.Value;
                    var resolved = scriptObjectIndex.ResolveImportWithModule(raw);
                    if (resolved.HasValue)
                    {
                        var modulePath = resolved.Value.ModuleName != null
                            ? $"/Script/{resolved.Value.ModuleName}"
                            : "/Script";
                        export.Super = new ResolvedRef
                        {
                            ClassName = "Class",
                            Name = resolved.Value.ObjectName,
                            PackagePath = modulePath,
                            ExportIndex = -1
                        };
                    }
                }

                // Resolve ScriptImport template reference
                if (export.TemplateRef is { Type: Models.PackageObjectType.ScriptImport } templateRef)
                {
                    var raw = ((ulong)templateRef.Type << 62) | templateRef.Value;
                    var resolved = scriptObjectIndex.ResolveImportWithModule(raw);
                    if (resolved.HasValue)
                    {
                        var modulePath = resolved.Value.ModuleName != null
                            ? $"/Script/{resolved.Value.ModuleName}"
                            : "/Script";
                        export.Template = new ResolvedRef
                        {
                            ClassName = resolved.Value.ObjectName, // For templates, the class is the object itself
                            Name = $"Default__{resolved.Value.ObjectName}",
                            PackagePath = modulePath,
                            ExportIndex = -1
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolves local class/super/template references using export indices within the same package.
    /// </summary>
    private void ResolveLocalClassNames()
    {
        foreach (var (path, metadata) in _metadataCache)
        {
            // Get package path without extension
            var packagePath = GetPackagePath(path);

            for (int i = 0; i < metadata.Exports.Length; i++)
            {
                var export = metadata.Exports[i];

                // Resolve local class reference
                if (export.ClassRef is { Type: Models.PackageObjectType.Export } classRef)
                {
                    var idx = classRef.ExportIndex;
                    if (idx >= 0 && idx < metadata.Exports.Length)
                    {
                        var target = metadata.Exports[idx];
                        export.Class = new ResolvedRef
                        {
                            ClassName = target.ClassName,
                            Name = target.Name,
                            PackagePath = packagePath,
                            ExportIndex = idx
                        };
                    }
                }

                // Resolve local super reference
                if (export.SuperRef is { Type: Models.PackageObjectType.Export } superRef)
                {
                    var idx = superRef.ExportIndex;
                    if (idx >= 0 && idx < metadata.Exports.Length)
                    {
                        var target = metadata.Exports[idx];
                        export.Super = new ResolvedRef
                        {
                            ClassName = target.ClassName,
                            Name = target.Name,
                            PackagePath = packagePath,
                            ExportIndex = idx
                        };
                    }
                }

                // Resolve local template reference
                if (export.TemplateRef is { Type: Models.PackageObjectType.Export } templateRef)
                {
                    var idx = templateRef.ExportIndex;
                    if (idx >= 0 && idx < metadata.Exports.Length)
                    {
                        var target = metadata.Exports[idx];
                        export.Template = new ResolvedRef
                        {
                            ClassName = target.ClassName,
                            Name = target.Name,
                            PackagePath = packagePath,
                            ExportIndex = idx
                        };
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets package path without extension.
    /// </summary>
    private static string GetPackagePath(string path)
    {
        var lastDot = path.LastIndexOf('.');
        return lastDot > 0 ? path[..lastDot] : path;
    }

    /// <summary>
    /// Builds the PublicExportHash -> ExportInfo index from all public exports.
    /// </summary>
    private void BuildPublicExportHashIndex()
    {
        _publicExportHashIndex = new Dictionary<ulong, ExportHashInfo>();

        foreach (var (path, metadata) in _metadataCache)
        {
            var packagePath = GetPackagePath(path);

            for (int i = 0; i < metadata.Exports.Length; i++)
            {
                var export = metadata.Exports[i];

                // Only index public exports with valid hashes
                if (export.IsPublic && export.PublicExportHash != 0)
                {
                    var info = new ExportHashInfo(export.Name, export.ClassName, packagePath, i);
                    // Don't overwrite if we already have an entry - first one wins
                    _publicExportHashIndex.TryAdd(export.PublicExportHash, info);
                }
            }
        }
    }

    /// <summary>
    /// Resolves external class references using the PublicExportHash index.
    /// </summary>
    private void ResolveExternalClassNames()
    {
        if (_publicExportHashIndex == null)
            return;

        foreach (var (_, metadata) in _metadataCache)
        {
            // Skip packages without ImportedPublicExportHashes
            if (metadata.ImportedPublicExportHashes == null)
                continue;

            foreach (var export in metadata.Exports)
            {
                // Resolve external class reference (PackageImport)
                if (export.ClassRef is { Type: Models.PackageObjectType.PackageImport } classRef)
                {
                    var hashIdx = classRef.HashIndex;
                    if (hashIdx < metadata.ImportedPublicExportHashes.Length)
                    {
                        var hash = metadata.ImportedPublicExportHashes[hashIdx];
                        if (_publicExportHashIndex.TryGetValue(hash, out var info))
                        {
                            export.Class = new ResolvedRef
                            {
                                ClassName = info.ClassName,
                                Name = info.Name,
                                PackagePath = info.PackagePath,
                                ExportIndex = info.ExportIndex
                            };
                        }
                    }
                }

                // Resolve external super reference (PackageImport)
                if (export.SuperRef is { Type: Models.PackageObjectType.PackageImport } superRef)
                {
                    var hashIdx = superRef.HashIndex;
                    if (hashIdx < metadata.ImportedPublicExportHashes.Length)
                    {
                        var hash = metadata.ImportedPublicExportHashes[hashIdx];
                        if (_publicExportHashIndex.TryGetValue(hash, out var info))
                        {
                            export.Super = new ResolvedRef
                            {
                                ClassName = info.ClassName,
                                Name = info.Name,
                                PackagePath = info.PackagePath,
                                ExportIndex = info.ExportIndex
                            };
                        }
                    }
                }

                // Resolve external template reference (PackageImport)
                if (export.TemplateRef is { Type: Models.PackageObjectType.PackageImport } templateRef)
                {
                    var hashIdx = templateRef.HashIndex;
                    if (hashIdx < metadata.ImportedPublicExportHashes.Length)
                    {
                        var hash = metadata.ImportedPublicExportHashes[hashIdx];
                        if (_publicExportHashIndex.TryGetValue(hash, out var info))
                        {
                            export.Template = new ResolvedRef
                            {
                                ClassName = info.ClassName,
                                Name = info.Name,
                                PackagePath = info.PackagePath,
                                ExportIndex = info.ExportIndex
                            };
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolves all imports using the PublicExportHash index.
    /// This is pass 2 of metadata loading - imports are resolved to their actual target exports.
    /// </summary>
    private void ResolveImports()
    {
        if (_publicExportHashIndex == null)
            return;

        foreach (var (_, metadata) in _metadataCache)
        {
            // Skip packages without imports or hashes
            if (metadata.Imports == null || metadata.ImportedPublicExportHashes == null)
                continue;

            foreach (var import in metadata.Imports)
            {
                // Skip already resolved imports (ScriptImports are resolved during read)
                if (import.IsResolved)
                    continue;

                // Only resolve imports with valid hash index
                if (import.PublicExportHashIndex < 0 ||
                    import.PublicExportHashIndex >= metadata.ImportedPublicExportHashes.Length)
                    continue;

                var hash = metadata.ImportedPublicExportHashes[import.PublicExportHashIndex];
                if (hash == 0)
                    continue;

                if (_publicExportHashIndex.TryGetValue(hash, out var exportInfo))
                {
                    // Update import with resolved data
                    import.Name = exportInfo.Name;
                    import.ClassName = exportInfo.ClassName;
                    import.PackageName = exportInfo.PackagePath;
                    import.IsResolved = true;
                }
            }
        }
    }

    /// <summary>
    /// Resolves an export by its full path. O(1) lookup after PreloadAllMetadata.
    /// </summary>
    /// <param name="exportPath">Full export path like "Game/Characters/Player.Player_C"</param>
    /// <returns>The metadata and export index, or null if not found.</returns>
    public (AssetMetadata Metadata, AssetExport Export)? ResolveExport(string exportPath)
    {
        if (_exportIndex == null)
            return null;

        if (_exportIndex.TryGetValue(exportPath, out var entry))
            return (entry.Metadata, entry.Metadata.Exports[entry.ExportIndex]);

        return null;
    }

    /// <summary>
    /// Finds all exports with the given name. O(1) lookup after PreloadAllMetadata.
    /// </summary>
    /// <param name="exportName">Export name (e.g., "BP_Water_C").</param>
    /// <returns>Enumerable of matching exports, or empty if not found.</returns>
    public IEnumerable<(AssetMetadata Metadata, AssetExport Export)> FindExportsByName(string exportName)
    {
        if (_exportNameIndex == null)
            yield break;

        if (!_exportNameIndex.TryGetValue(exportName, out var entries))
            yield break;

        foreach (var entry in entries)
        {
            yield return (entry.Metadata, entry.Metadata.Exports[entry.ExportIndex]);
        }
    }

    /// <summary>
    /// Gets the number of indexed exports.
    /// </summary>
    public int ExportIndexCount => _exportIndex?.Count ?? 0;

    /// <summary>
    /// Resolves an export name by its PublicExportHash.
    /// </summary>
    /// <param name="hash">The public export hash.</param>
    /// <returns>The export name, or null if not found.</returns>
    public string? ResolveExportNameByHash(ulong hash)
    {
        if (_publicExportHashIndex == null || hash == 0)
            return null;

        return _publicExportHashIndex.TryGetValue(hash, out var info) ? info.Name : null;
    }

    /// <summary>
    /// Resolves full export info by its PublicExportHash.
    /// </summary>
    /// <param name="hash">The public export hash.</param>
    /// <returns>The export info, or null if not found.</returns>
    public ExportHashInfo? ResolveExportByHash(ulong hash)
    {
        if (_publicExportHashIndex == null || hash == 0)
            return null;

        return _publicExportHashIndex.TryGetValue(hash, out var info) ? info : null;
    }

    /// <summary>
    /// Reads export binary data using a pooled buffer.
    /// Only reads from the .uasset entry - use the AssetGroup overload for .uexp support.
    /// Caller must dispose the result to return buffer to pool.
    /// </summary>
    /// <param name="entry">The container entry containing the asset.</param>
    /// <param name="export">The export to read data for.</param>
    /// <returns>Export data. Dispose when done to return buffer to pool.</returns>
    public ExportData ReadExportData(IAssetEntry entry, AssetExport export)
    {
        if (_exportDataReader == null)
            throw new InvalidOperationException("No export data reader configured");

        if (export.SerialSize <= 0 || export.SerialSize > int.MaxValue)
            throw new ArgumentException($"Invalid SerialSize: {export.SerialSize}", nameof(export));

        var buffer = ArrayPool<byte>.Shared.Rent((int)export.SerialSize);
        try
        {
            using var stream = OpenRead(entry);
            _exportDataReader.ReadExportData(export, stream, buffer);
            return new ExportData(buffer, (int)export.SerialSize);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    /// Reads export binary data using a pooled buffer.
    /// Automatically handles data split across .uasset and .uexp files.
    /// Caller must dispose the result to return buffer to pool.
    /// </summary>
    /// <param name="asset">The asset group containing the asset and companion files.</param>
    /// <param name="export">The export to read data for.</param>
    /// <param name="metadata">Optional metadata for IO Store offset calculations.</param>
    /// <returns>Export data. Dispose when done to return buffer to pool.</returns>
    public ExportData ReadExportData(AssetGroup asset, AssetExport export, AssetMetadata? metadata = null)
    {
        if (_exportDataReader == null)
            throw new InvalidOperationException("No export data reader configured");

        if (export.SerialSize <= 0 || export.SerialSize > int.MaxValue)
            throw new ArgumentException($"Invalid SerialSize: {export.SerialSize}", nameof(export));

        // Determine if export data is in .uasset or .uexp
        bool dataInUExp = export.SerialOffset + export.SerialSize > asset.Asset.Size;

        if (dataInUExp && asset.UExp == null)
            throw new InvalidOperationException(
                $"Export data is in .uexp but no .uexp file found for {asset.BasePath}");

        var buffer = ArrayPool<byte>.Shared.Rent((int)export.SerialSize);
        try
        {
            if (dataInUExp)
            {
                // Data is in .uexp - adjust offset relative to .uexp start
                long uexpOffset = export.SerialOffset - asset.Asset.Size;
                using var stream = OpenRead(asset.UExp!);
                stream.Seek(uexpOffset, SeekOrigin.Begin);
                stream.ReadExactly(buffer.AsSpan(0, (int)export.SerialSize));
            }
            else
            {
                // Data is in .uasset
                using var stream = OpenRead(asset.Asset);

                // For IO Store packages, add CookedHeaderSize to the offset
                // (SerialOffset is relative to the data section, not the file start)
                long actualOffset = export.SerialOffset;
                if (asset.Asset is IoStoreEntry && metadata != null)
                {
                    actualOffset += metadata.CookedHeaderSize;
                }

                stream.Seek(actualOffset, SeekOrigin.Begin);
                stream.ReadExactly(buffer.AsSpan(0, (int)export.SerialSize));
            }

            return new ExportData(buffer, (int)export.SerialSize);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
