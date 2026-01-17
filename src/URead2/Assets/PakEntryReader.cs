using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Compression;
using URead2.Containers;
using URead2.Containers.Pak;
using URead2.Crypto;
using URead2.IO;

namespace URead2.Assets;

/// <summary>
/// Reads pak entry data with streaming decompression and decryption.
/// </summary>
public class PakEntryReader : IAssetEntryReader
{
    private readonly Decompressor _decompressor;
    private readonly IDecryptor _decryptor;

    public PakEntryReader(Decompressor decompressor, IDecryptor decryptor)
    {
        _decompressor = decompressor;
        _decryptor = decryptor;
    }

    public Stream OpenRead(IAssetEntry entry, byte[]? aesKey = null, MountedContainer? container = null)
    {
        if (entry is not PakEntry pakEntry)
            throw new ArgumentException("Entry must be a PakEntry", nameof(entry));

        if (container == null)
            throw new ArgumentNullException(nameof(container), "MountedContainer is required");

        var compressionMethod = ParseCompressionMethod(pakEntry.CompressionMethod);
        var blockProvider = new PakBlockProvider(pakEntry, compressionMethod, container);
        return new AssetStream(blockProvider, _decompressor, _decryptor, aesKey);
    }

    private static CompressionMethod ParseCompressionMethod(string? method)
    {
        if (string.IsNullOrEmpty(method))
            return CompressionMethod.None;

        if (string.Equals(method, "ZLIB", StringComparison.OrdinalIgnoreCase)) return CompressionMethod.Zlib;
        if (string.Equals(method, "GZIP", StringComparison.OrdinalIgnoreCase)) return CompressionMethod.Gzip;
        if (string.Equals(method, "OODLE", StringComparison.OrdinalIgnoreCase)) return CompressionMethod.Oodle;
        if (string.Equals(method, "LZ4", StringComparison.OrdinalIgnoreCase)) return CompressionMethod.LZ4;
        if (string.Equals(method, "ZSTD", StringComparison.OrdinalIgnoreCase)) return CompressionMethod.Zstd;

        return CompressionMethod.Unknown;
    }
}
