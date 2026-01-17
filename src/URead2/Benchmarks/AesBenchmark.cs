using System.Diagnostics;
using System.Security.Cryptography;
using URead2.Crypto;

namespace URead2.Benchmarks;

public static class AesBenchmark
{
    private const int Iterations = 1000;
    private const int DataSize = 16 * 1024; // 16 KB
    private static readonly byte[] Key = new byte[32];
    private static readonly byte[] Data = new byte[DataSize];

    public static void Run()
    {
        // Setup
        Random.Shared.NextBytes(Key);
        Random.Shared.NextBytes(Data);

        Console.WriteLine($"Running benchmark with {Iterations} iterations on {DataSize} bytes...");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        Console.WriteLine($"Runtime: {Environment.Version}");

        var decryptor = new AesDecryptor();

        // Warmup
        decryptor.Decrypt(new Span<byte>(Data), Key);

        // Measure
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long startAlloc = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < Iterations; i++)
        {
            decryptor.Decrypt(new Span<byte>(Data), Key);
        }

        stopwatch.Stop();
        long endAlloc = GC.GetAllocatedBytesForCurrentThread();

        Console.WriteLine($"Time: {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine($"Total Allocated: {(endAlloc - startAlloc) / 1024.0:F2} KB");
        Console.WriteLine($"Allocated per op: {(endAlloc - startAlloc) / (double)Iterations:F0} bytes");
    }
}
