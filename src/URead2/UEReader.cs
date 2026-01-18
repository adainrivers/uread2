using URead2.Assets;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers;
using URead2.Deserialization;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Fields;
using URead2.Deserialization.Properties;
using URead2.Deserialization.TypeReaders;
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
    /// Loads type registry from JSON or usmap if available.
    /// </summary>
    private void ConfigureTypeResolution(RuntimeConfig config)
    {
        // Prefer JSON type registry if configured (has package paths)
        if (!string.IsNullOrEmpty(config.TypeRegistryJsonPath) && File.Exists(config.TypeRegistryJsonPath))
        {
            try
            {
                _typeRegistry = TypeRegistry.FromJson(config.TypeRegistryJsonPath);
                _typeRegistry.LazyResolver = ResolveTypeFromExportIndex;
                Serilog.Log.Information("Loaded type registry from JSON: {Path}", config.TypeRegistryJsonPath);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load type registry JSON from: {Path}", config.TypeRegistryJsonPath);
            }
        }
        // Fall back to usmap if JSON not configured
        else if (!string.IsNullOrEmpty(config.UsmapPath) && File.Exists(config.UsmapPath))
        {
            try
            {
                _typeRegistry = TypeRegistry.FromUsmap(config.UsmapPath, _profile.Decompressor);
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
    private (string? SuperName, string? PackagePath, Dictionary<int, PropertyDefinition>? Properties)? ResolveTypeFromExportIndex(string typeName)
    {
        // Export index must be built (via PreloadAllMetadata)
        if (Assets.ExportIndexCount == 0)
        {
            Serilog.Log.Debug("LazyResolver: Export index is empty for {TypeName}", typeName);
            return null;
        }

        // Find the export that defines this type (with metadata)
        var result = FindTypeDefiningExportWithMetadata(typeName);
        if (result == null)
        {
            // Debug: check if export exists at all by name
            var exports = Assets.FindExportsByName(typeName).ToList();
            if (exports.Count > 0)
            {
                Serilog.Log.Debug("LazyResolver: Found {Count} exports named {TypeName} but none are type-defining. ClassNames: {ClassNames}",
                    exports.Count, typeName, string.Join(", ", exports.Select(e => e.Export.ClassName).Distinct()));
            }
            return null;
        }

        var (metadata, export) = result.Value;

        // Check if it's a type-defining class by checking inheritance chain
        if (!IsTypeDefiningClass(export.ClassName))
            return null;

        // Try to read and parse the properties from the export
        var properties = TryReadBlueprintProperties(metadata, export);

        return (export.SuperClassName, null, properties);
    }

    /// <summary>
    /// Finds an export that defines the given type name, with its metadata.
    /// </summary>
    private (AssetMetadata Metadata, AssetExport Export)? FindTypeDefiningExportWithMetadata(string typeName)
    {
        // Use O(1) name-based index lookup
        foreach (var (metadata, export) in Assets.FindExportsByName(typeName))
        {
            if (IsTypeDefiningClass(export.ClassName))
            {
                return (metadata, export);
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to read and parse Blueprint class properties from an export.
    /// </summary>
    private Dictionary<int, PropertyDefinition>? TryReadBlueprintProperties(AssetMetadata metadata, AssetExport export)
    {
        try
        {
            var exportData = Assets.ReadExportData(metadata, export);
            if (exportData == null)
                return null;

            using var _ = exportData;
            using var stream = exportData.AsStream();
            using var ar = new ArchiveReader(stream);

            // Skip tagged properties first (UObject properties)
            // These are the standard properties like NumReplicatedProperties, etc.
            _profile.PropertyReader.ReadProperties(ar, CreateReadContext(metadata), export.ClassName, metadata.IsUnversioned);

            // Now read the FProperty array (ChildProperties)
            var fProperties = BlueprintClassReader.ReadClassProperties(ar, metadata, export);
            if (fProperties == null || fProperties.Length == 0)
                return null;

            // Convert to PropertyDefinition dictionary
            return BlueprintClassReader.ConvertToPropertyDefinitions(fProperties);
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to read Blueprint properties for {TypeName}", export.Name);
            return null;
        }
    }

    /// <summary>
    /// Checks if a class name represents a type-defining class.
    /// Uses TryGetType to avoid triggering lazy resolution (prevents infinite recursion).
    /// </summary>
    private bool IsTypeDefiningClass(string className)
    {
        // First check the name directly for known type-defining classes
        if (IsKnownTypeDefiningClass(className))
            return true;

        // Check if the class itself or its base is a type-defining class
        // Use TryGetType to avoid triggering lazy resolution (prevents infinite recursion)
        var typeDef = TypeRegistry.TryGetType(className);
        if (typeDef == null)
            return false;

        // Walk inheritance chain
        var current = typeDef;
        while (current != null)
        {
            if (IsKnownTypeDefiningClass(current.Name))
                return true;

            if (string.IsNullOrEmpty(current.SuperName))
                break;

            current = TypeRegistry.TryGetType(current.SuperName);
        }

        return false;
    }

    /// <summary>
    /// Checks if a class name is one of the known type-defining classes.
    /// </summary>
    private static bool IsKnownTypeDefiningClass(string className)
    {
        return AssetConstants.TypeDefiningClasses.Contains(className);
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
    /// Opens a stream to read entry content.
    /// </summary>
    public Stream OpenRead(IAssetEntry entry) => Assets.OpenRead(entry);

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
        return asset.Metadata?.Exports ?? [];
    }

    /// <summary>
    /// Deserializes an export's properties.
    /// </summary>
    /// <param name="asset">The asset containing the export.</param>
    /// <param name="export">The export to deserialize.</param>
    /// <returns>Deserialized properties, or empty bag if deserialization fails.</returns>
    public PropertyBag DeserializeExport(AssetGroup asset, AssetExport export)
    {
        var metadata = asset.Metadata;
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

        // UE serializes ObjectGuid for non-CDO objects before properties
        // Read and skip the ObjectGuid boolean (and GUID if present)
        if (!export.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
        {
            var hasGuid = ar.ReadInt32() != 0;
            if (hasGuid && ar.Position + 16 <= ar.Length)
            {
                ar.Position += 16; // Skip the 16-byte FGuid
            }
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
    /// Gets the profile used by this reader.
    /// </summary>
    public IProfile Profile => _profile;

    /// <summary>
    /// Creates a PropertyReadContext for deserializing an asset.
    /// Imports are pre-resolved during PreloadAllMetadata().
    /// </summary>
    /// <param name="metadata">The asset metadata.</param>
    public PropertyReadContext CreateReadContext(AssetMetadata metadata)
    {
        return new PropertyReadContext
        {
            NameTable = metadata.NameTable,
            TypeRegistry = TypeRegistry,
            PropertyReader = _profile.PropertyReader,
            Imports = metadata.Imports,
            Exports = metadata.Exports,
            PackagePath = GetPackagePath(metadata.Name),
            IsUnversioned = metadata.IsUnversioned
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
