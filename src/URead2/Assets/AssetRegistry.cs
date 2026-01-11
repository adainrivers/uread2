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

    // Global export index for cross-package resolution: "PackagePath.ExportName" -> (Metadata, ExportIndex)
    private ConcurrentDictionary<string, (AssetMetadata Metadata, int ExportIndex)>? _exportIndex;

    // Singleton instance for global access (e.g., from PackageResolver)
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
        _exportIndex = new ConcurrentDictionary<string, (AssetMetadata, int)>(StringComparer.OrdinalIgnoreCase);

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
                // Key format: "PackagePath.ExportName"
                var exportKey = $"{packagePath}.{export.Name}";
                _exportIndex.TryAdd(exportKey, (metadata, i));
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

        if (_exportIndex.TryGetValue(exportPath, out var result))
            return (result.Metadata, result.Metadata.Exports[result.ExportIndex]);

        return null;
    }

    /// <summary>
    /// Gets the number of indexed exports.
    /// </summary>
    public int ExportIndexCount => _exportIndex?.Count ?? 0;

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
