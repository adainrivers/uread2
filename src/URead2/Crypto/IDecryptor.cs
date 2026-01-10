namespace URead2.Crypto;

/// <summary>
/// Interface for decrypting data.
/// </summary>
public interface IDecryptor
{
    void Decrypt(Span<byte> data, byte[] key);
}
