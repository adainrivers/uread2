using URead2.Assets;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers;
using URead2.Deserialization;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Properties;
using URead2.IO;
using URead2.Profiles.Abstractions;
using URead2.TypeResolution;
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
    private TypeRegistry? _typeRegistry;
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
                _typeRegistry = TypeRegistry.FromUsmap(config.UsmapPath, _profile.Decompressor);
                // Set up lazy type resolution for Blueprint classes
                _typeRegistry.LazyResolver = ResolveTypeFromExportIndex;
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
    /// Resolves type info from the export index for Blueprint/asset-defined types.
    /// </summary>
    private (string? SuperName, string? PackagePath)? ResolveTypeFromExportIndex(string typeName)
    {
        // Export index must be built (via PreloadAllMetadata)
        if (Assets.ExportIndexCount == 0)
            return null;

        // Find the export that defines this type
        var export = FindTypeDefiningExport(typeName);
        if (export == null)
            return null;

        // Check if it's a type-defining class by checking inheritance chain
        if (!IsTypeDefiningClass(export.ClassName))
            return null;

        return (export.SuperClassName, null);
    }

    /// <summary>
    /// Finds an export that defines the given type name.
    /// </summary>
    private Assets.Models.AssetExport? FindTypeDefiningExport(string typeName)
    {
        // Use O(1) name-based index lookup
        foreach (var (_, export) in Assets.FindExportsByName(typeName))
        {
            if (IsTypeDefiningClass(export.ClassName))
            {
                return export;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a class name represents a type-defining class.
    /// </summary>
    private bool IsTypeDefiningClass(string className)
    {
        // Check if the class itself or its base is a type-defining class
        var typeDef = TypeRegistry.GetType(className);
        if (typeDef == null)
            return false;

        // Walk inheritance chain
        var current = typeDef;
        while (current != null)
        {
            var name = current.Name;
            if (name.Equals("BlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WidgetBlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AnimBlueprintGeneratedClass", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ScriptStruct", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("UserDefinedStruct", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrEmpty(current.SuperName))
                break;

            current = TypeRegistry.GetType(current.SuperName);
        }

        return false;
    }

    /// <summary>
    /// Gets the type registry for deserializing properties.
    /// Returns the usmap-based registry if configured, otherwise an empty registry.
    /// </summary>
    public TypeRegistry TypeRegistry => _typeRegistry ?? TypeRegistry.Empty;

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
    /// Uses cached results.
    /// </summary>
    public IReadOnlyList<AssetGroup> GetAssets()
        => Assets.GetAssetGroups();


    /// <summary>
    /// Gets all assets grouped with their companion files.
    /// Uses cached results. Filter applies to AssetGroup.BasePath.
    /// </summary>
    public IEnumerable<AssetGroup> GetAssets(Func<string, bool> pathFilter)
        => Assets.GetAssetGroups().Where(g => pathFilter(g.BasePath));

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
    /// Gets all exports from an asset.
    /// Returns empty array if metadata cannot be read.
    /// </summary>
    public AssetExport[] GetExports(AssetGroup asset)
    {
        var metadata = Assets.ReadMetadata(asset);
        return metadata?.Exports ?? [];
    }

    /// <summary>
    /// Deserializes an export's properties.
    /// </summary>
    /// <param name="asset">The asset containing the export.</param>
    /// <param name="export">The export to deserialize.</param>
    /// <returns>Deserialized properties, or empty bag if deserialization fails.</returns>
    public PropertyBag DeserializeExport(AssetGroup asset, AssetExport export)
    {
        var metadata = Assets.ReadMetadata(asset);
        if (metadata is null)
            return new PropertyBag();

        var context = CreateReadContext(metadata);

        using var exportData = Assets.ReadExportData(asset, export, metadata);
        using var stream = exportData.AsStream();
        using var ar = new ArchiveReader(stream);

        // Check for custom type reader first
        var customReader = _profile.TypeReaderRegistry.GetReader(export.ClassName);
        if (customReader != null)
        {
            return customReader.Read(ar, context, export);
        }

        return _profile.PropertyReader.ReadProperties(ar, context, export.ClassName, metadata.IsUnversioned);
    }

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
            TypeRegistry = TypeRegistry,
            Imports = metadata.Imports,
            Exports = metadata.Exports,
            PackagePath = GetPackagePath(metadata.Name),
            PackageResolver = enableCrossPackageResolution ? PackageResolver : null
        };
    }

    /// <summary>
    /// Creates a PropertyReadContext for deserializing an asset with a custom type registry.
    /// </summary>
    public PropertyReadContext CreateReadContext(AssetMetadata metadata, TypeRegistry typeRegistry, bool enableCrossPackageResolution = true)
    {
        return new PropertyReadContext
        {
            NameTable = metadata.NameTable,
            TypeRegistry = typeRegistry,
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
