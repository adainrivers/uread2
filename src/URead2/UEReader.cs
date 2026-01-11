using URead2.Assets;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers;
using URead2.Deserialization;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.TypeMappings;
using URead2.Profiles.Abstractions;
using ZenPackageMetadataReader = URead2.Assets.ZenPackageMetadataReader;

namespace URead2;

/// <summary>
/// Root orchestrator for reading Unreal Engine game assets.
/// Provides a unified API that combines container and asset operations.
/// </summary>
public class UEReader : IDisposable
{
    private readonly IProfile _profile;
    private PackageResolver? _packageResolver;
    private TypeResolver? _typeResolver;
    private bool _disposed;

    public UEReader(RuntimeConfig config, IProfile profile)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(profile);

        _profile = profile;

        // Initialize singleton registries
        ContainerRegistry.Initialize(config, profile);
        AssetRegistry.Initialize(config, profile);

        // Auto-configure type resolution from config
        ConfigureTypeResolution(config);
    }

    /// <summary>
    /// Configures type resolution from runtime config.
    /// Loads usmap and global data if available.
    /// </summary>
    private void ConfigureTypeResolution(RuntimeConfig config)
    {
        // Load usmap type mappings if configured
        if (!string.IsNullOrEmpty(config.UsmapPath) && File.Exists(config.UsmapPath))
        {
            try
            {
                _typeResolver = TypeResolver.FromUsmapFile(config.UsmapPath, _profile.Decompressor);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load usmap from: {Path}", config.UsmapPath);
            }
        }

        // Load script object index for import resolution
        var scriptObjectIndex = Containers.ScriptObjectIndex;
        if (scriptObjectIndex != null)
        {
            // Inject into ZenPackageMetadataReader for import resolution
            if (_profile.ZenPackageReader is ZenPackageMetadataReader zenReader)
            {
                zenReader.ScriptObjectIndex = scriptObjectIndex;
            }
        }
    }

    /// <summary>
    /// Gets the type resolver for deserializing properties.
    /// Returns the usmap-based resolver if configured, otherwise an empty resolver.
    /// </summary>
    public TypeResolver TypeResolver => _typeResolver ?? TypeResolver.Empty;

    /// <summary>
    /// Gets direct access to the container registry.
    /// </summary>
    public ContainerRegistry Containers => ContainerRegistry.Instance
        ?? throw new InvalidOperationException("ContainerRegistry not initialized");

    /// <summary>
    /// Gets direct access to the asset registry.
    /// </summary>
    public AssetRegistry Assets => AssetRegistry.Instance
        ?? throw new InvalidOperationException("AssetRegistry not initialized");

    /// <summary>
    /// Gets all entries from all containers.
    /// </summary>
    public IEnumerable<IAssetEntry> GetEntries(Func<string, bool>? containerPathFilter = null)
        => Containers.GetEntries(containerPathFilter);

    /// <summary>
    /// Gets all assets grouped with their companion files.
    /// Uses cached results. Optional filter applies to AssetGroup.BasePath.
    /// </summary>
    public IEnumerable<AssetGroup> GetAssets(Func<string, bool>? pathFilter = null)
        => pathFilter == null
            ? Assets.GetAssetGroups()
            : Assets.GetAssetGroups().Where(g => pathFilter(g.BasePath));

    /// <summary>
    /// Opens a stream to read entry content.
    /// </summary>
    public Stream OpenRead(IAssetEntry entry) => Assets.OpenRead(entry);

    /// <summary>
    /// Reads asset metadata.
    /// </summary>
    public AssetMetadata? ReadMetadata(IAssetEntry entry) => Assets.ReadMetadata(entry);

    /// <summary>
    /// Reads asset metadata.
    /// </summary>
    public AssetMetadata? ReadMetadata(AssetGroup asset) => Assets.ReadMetadata(asset);

    /// <summary>
    /// Reads export binary data.
    /// </summary>
    public ExportData ReadExportData(IAssetEntry entry, AssetExport export)
        => Assets.ReadExportData(entry, export);

    /// <summary>
    /// Reads export binary data.
    /// </summary>
    public ExportData ReadExportData(AssetGroup asset, AssetExport export)
        => Assets.ReadExportData(asset, export);

    /// <summary>
    /// Preloads all asset metadata in parallel and builds the global export index.
    /// Call once at startup for fastest cross-package access.
    /// </summary>
    public void PreloadAllMetadata(int? maxDegreeOfParallelism = null, Action<int, int>? progress = null)
        => Assets.PreloadAllMetadata(maxDegreeOfParallelism, progress);

    /// <summary>
    /// Resolves an export by its full path. O(1) lookup after PreloadAllMetadata.
    /// </summary>
    public (AssetMetadata Metadata, AssetExport Export)? ResolveExport(string exportPath)
        => Assets.ResolveExport(exportPath);

    /// <summary>
    /// Number of cached metadata entries.
    /// </summary>
    public int MetadataCacheCount => Assets.MetadataCacheCount;

    /// <summary>
    /// Number of indexed exports for cross-package resolution.
    /// </summary>
    public int ExportIndexCount => Assets.ExportIndexCount;

    /// <summary>
    /// Gets the package resolver for cross-package reference resolution.
    /// Created lazily on first access.
    /// </summary>
    public PackageResolver PackageResolver
    {
        get
        {
            _packageResolver ??= new PackageResolver();
            return _packageResolver;
        }
    }

    /// <summary>
    /// Gets the profile used by this reader.
    /// </summary>
    public IProfile Profile => _profile;

    /// <summary>
    /// Creates a PropertyReadContext for deserializing an asset.
    /// Includes cross-package resolution support if metadata has been preloaded.
    /// </summary>
    /// <param name="metadata">The asset metadata.</param>
    /// <param name="enableCrossPackageResolution">
    /// If true, enables cross-package import resolution.
    /// Requires PreloadAllMetadata() to have been called for best results.
    /// </param>
    public PropertyReadContext CreateReadContext(AssetMetadata metadata, bool enableCrossPackageResolution = true)
    {
        return new PropertyReadContext
        {
            NameTable = metadata.NameTable,
            TypeResolver = TypeResolver,
            Imports = metadata.Imports,
            Exports = metadata.Exports,
            PackagePath = GetPackagePath(metadata.Name),
            PackageResolver = enableCrossPackageResolution ? PackageResolver : null
        };
    }

    /// <summary>
    /// Creates a PropertyReadContext for deserializing an asset with a custom type resolver.
    /// </summary>
    public PropertyReadContext CreateReadContext(AssetMetadata metadata, ITypeResolver typeResolver, bool enableCrossPackageResolution = true)
    {
        return new PropertyReadContext
        {
            NameTable = metadata.NameTable,
            TypeResolver = typeResolver,
            Imports = metadata.Imports,
            Exports = metadata.Exports,
            PackagePath = GetPackagePath(metadata.Name),
            PackageResolver = enableCrossPackageResolution ? PackageResolver : null
        };
    }

    /// <summary>
    /// Extracts the package path from an asset name/path.
    /// </summary>
    private static string? GetPackagePath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        // Remove extension
        var lastDot = name.LastIndexOf('.');
        if (lastDot > 0)
            name = name[..lastDot];

        return name;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Containers.Dispose();
            (_profile as IDisposable)?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
