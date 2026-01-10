using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Compression;
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

    public Stream OpenRead(IAssetEntry entry, byte[]? aesKey = null)
    {
        if (entry is not PakEntry pakEntry)
            throw new ArgumentException("Entry must be a PakEntry", nameof(entry));

        var compressionMethod = ParseCompressionMethod(pakEntry.CompressionMethod);

        var blockProvider = new PakBlockProvider(pakEntry, compressionMethod);
        return new AssetStream(blockProvider, _decompressor, _decryptor, aesKey);
    }

    private static CompressionMethod ParseCompressionMethod(string? method)
    {
        if (string.IsNullOrEmpty(method))
            return CompressionMethod.None;

        return method.ToUpperInvariant() switch
        {
            "ZLIB" => CompressionMethod.Zlib,
            "GZIP" => CompressionMethod.Gzip,
            "OODLE" => CompressionMethod.Oodle,
            "LZ4" => CompressionMethod.LZ4,
            "ZSTD" => CompressionMethod.Zstd,
            _ => CompressionMethod.Unknown
        };
    }
}
