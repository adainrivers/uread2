using URead2.Assets.Abstractions;
using URead2.Compression;
using URead2.Containers.Abstractions;
using URead2.Crypto;

namespace URead2.Profiles.Abstractions;

/// <summary>
/// Profile defines behavior (readers, compression, decryption logic).
/// Runtime config (paths, keys) is passed separately via RuntimeConfig.
/// </summary>
public interface IProfile
{
    IContainerReader? PakReader { get; }
    IContainerReader? IoStoreReader { get; }
    IAssetEntryReader? PakEntryReader { get; }
    IAssetEntryReader? IoStoreEntryReader { get; }
    Decompressor Decompressor { get; }
    IDecryptor Decryptor { get; }

    /// <summary>
    /// Reader for traditional .uasset format (PAK files).
    /// </summary>
    IAssetMetadataReader? UAssetReader { get; }

    /// <summary>
    /// Reader for Zen package format (IO Store files).
    /// </summary>
    IAssetMetadataReader? ZenPackageReader { get; }

    /// <summary>
    /// Reader for export binary data.
    /// </summary>
    IExportDataReader? ExportDataReader { get; }

    /// <summary>
    /// Reader for FByteBulkData structures.
    /// </summary>
    IBulkDataReader? BulkDataReader { get; }
}
