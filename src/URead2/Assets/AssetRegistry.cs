using System.Buffers;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Profiles.Abstractions;

namespace URead2.Assets;

/// <summary>
/// Registry that provides asset-level operations: reading, metadata, and export data.
/// </summary>
public class AssetRegistry
{
    private readonly RuntimeConfig _config;
    private readonly IProfile _profile;

    public AssetRegistry(RuntimeConfig config, IProfile profile)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    /// <summary>
    /// Groups entries with their companion files (.uexp, .ubulk).
    /// </summary>
    public IEnumerable<AssetGroup> GroupAssets(IEnumerable<IAssetEntry> entries)
    {
        var entriesList = entries.ToList();
        var byBasePath = new Dictionary<string, (IAssetEntry? Asset, IAssetEntry? UExp, IAssetEntry? UBulk, bool IsMap)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entriesList)
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

        foreach (var (basePath, group) in byBasePath)
        {
            if (group.Asset == null)
                continue; // Skip orphan .uexp/.ubulk without primary asset

            yield return new AssetGroup(basePath, group.Asset, group.IsMap, group.UExp, group.UBulk);
        }
    }

    /// <summary>
    /// Opens a stream to read the entry's content.
    /// </summary>
    public Stream OpenRead(IAssetEntry entry)
    {
        return entry switch
        {
            PakEntry => _profile.PakEntryReader?.OpenRead(entry, _config.AesKey)
                ?? throw new InvalidOperationException("No pak entry reader configured"),
            IoStoreEntry => _profile.IoStoreEntryReader?.OpenRead(entry, _config.AesKey)
                ?? throw new InvalidOperationException("No IO Store entry reader configured"),
            _ => throw new NotSupportedException($"Unknown entry type: {entry.GetType().Name}")
        };
    }

    /// <summary>
    /// Reads asset metadata (names, imports, exports) from an entry.
    /// Only works for .uasset/.umap files.
    /// </summary>
    public AssetMetadata? ReadMetadata(IAssetEntry entry)
    {
        using var stream = OpenRead(entry);

        return entry switch
        {
            PakEntry => _profile.UAssetReader?.ReadMetadata(stream, entry.Path),
            IoStoreEntry => _profile.ZenPackageReader?.ReadMetadata(stream, entry.Path),
            _ => null
        };
    }

    /// <summary>
    /// Reads asset metadata (names, imports, exports) from an asset group.
    /// </summary>
    public AssetMetadata? ReadMetadata(AssetGroup asset)
    {
        return ReadMetadata(asset.Asset);
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
        if (_profile.ExportDataReader == null)
            throw new InvalidOperationException("No export data reader configured");

        if (export.SerialSize <= 0 || export.SerialSize > int.MaxValue)
            throw new ArgumentException($"Invalid SerialSize: {export.SerialSize}", nameof(export));

        var buffer = ArrayPool<byte>.Shared.Rent((int)export.SerialSize);
        try
        {
            using var stream = OpenRead(entry);
            _profile.ExportDataReader.ReadExportData(export, stream, buffer);
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
    /// <returns>Export data. Dispose when done to return buffer to pool.</returns>
    public ExportData ReadExportData(AssetGroup asset, AssetExport export)
    {
        if (_profile.ExportDataReader == null)
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
                _profile.ExportDataReader.ReadExportData(export, stream, buffer);
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
