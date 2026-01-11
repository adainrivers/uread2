using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Compression;
using URead2.Containers;
using URead2.Containers.IoStore;
using URead2.Crypto;
using URead2.IO;

namespace URead2.Assets;

/// <summary>
/// Reads IO Store entry data with streaming decompression and decryption.
/// </summary>
public class IoStoreEntryReader : IAssetEntryReader
{
    private readonly Decompressor _decompressor;
    private readonly IDecryptor _decryptor;

    public IoStoreEntryReader(Decompressor decompressor, IDecryptor decryptor)
    {
        _decompressor = decompressor;
        _decryptor = decryptor;
    }

    public Stream OpenRead(IAssetEntry entry, byte[]? aesKey = null, MountedContainer? container = null)
    {
        if (entry is not IoStoreEntry ioEntry)
            throw new ArgumentException("Entry must be an IoStoreEntry", nameof(entry));

        if (container == null)
            throw new ArgumentNullException(nameof(container), "MountedContainer is required");

        var blockProvider = new IoStoreBlockProvider(ioEntry, container);
        return new AssetStream(blockProvider, _decompressor, _decryptor, aesKey);
    }
}
