using System;
using URead2.Crypto;

namespace URead2.Benchmark;

public class MockDecryptor : IDecryptor
{
    public void Decrypt(Span<byte> data, byte[] key)
    {
        // No-op
    }
}
