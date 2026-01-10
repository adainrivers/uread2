namespace URead2.Assets.Abstractions;

/// <summary>
/// Reads asset entry data with decompression and decryption.
/// </summary>
public interface IAssetEntryReader
{
    Stream OpenRead(IAssetEntry entry, byte[]? aesKey = null);
}
