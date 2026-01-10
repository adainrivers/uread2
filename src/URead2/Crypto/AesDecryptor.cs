using System.Security.Cryptography;

namespace URead2.Crypto;

/// <summary>
/// AES-256-ECB decryptor used by Unreal Engine.
/// </summary>
public class AesDecryptor : IDecryptor
{
    public void Decrypt(Span<byte> data, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES key must be 32 bytes (256 bits)", nameof(key));

        if (data.Length % 16 != 0)
            throw new ArgumentException("Data length must be a multiple of 16 bytes", nameof(data));

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        var temp = data.ToArray();
        var decrypted = decryptor.TransformFinalBlock(temp, 0, temp.Length);
        decrypted.CopyTo(data);
    }

    public static int Align16(int size) => size + 15 & ~15;

    public static byte[] Decrypt(byte[] data, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES key must be 32 bytes (256 bits)", nameof(key));

        if (data.Length % 16 != 0)
            throw new ArgumentException("Data length must be a multiple of 16 bytes", nameof(data));

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] ParseHexKey(string hexKey)
    {
        if (string.IsNullOrWhiteSpace(hexKey))
            throw new ArgumentException("Key cannot be empty", nameof(hexKey));

        if (hexKey.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hexKey = hexKey[2..];

        hexKey = hexKey.Replace(" ", "").Replace("-", "");

        if (hexKey.Length != 64)
            throw new ArgumentException($"AES-256 key must be 64 hex characters (got {hexKey.Length})", nameof(hexKey));

        var key = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            key[i] = Convert.ToByte(hexKey.Substring(i * 2, 2), 16);
        }

        return key;
    }
}
