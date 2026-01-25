using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Buffers;
using System.IO.Compression;
using URead2.Compression;
using URead2.IO;

namespace URead2.Benchmark;

[MemoryDiagnoser]
public class AssetStreamBenchmark
{
    private MockBlockProvider _blockProvider;
    private Decompressor _decompressor;
    private MockDecryptor _decryptor;
    private AssetStream _stream;
    private byte[] _buffer;

    [GlobalSetup]
    public void Setup()
    {
        _blockProvider = new MockBlockProvider(64 * 1024, 100); // 64KB blocks, 100 blocks
        _decompressor = new Decompressor();
        _decryptor = new MockDecryptor();
        _stream = new AssetStream(_blockProvider, _decompressor, _decryptor, null);
        _buffer = new byte[4096];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stream.Dispose();
        _decompressor.Dispose();
        _blockProvider.Dispose();
    }

    [Benchmark]
    public int ReadStream()
    {
        _stream.Position = 0;
        int totalRead = 0;
        while (true)
        {
            int read = _stream.Read(_buffer, 0, _buffer.Length);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        // Verify correctness before running benchmark
        VerifyCorrectness();
        VerifyDecompression();

        // var summary = BenchmarkRunner.Run<AssetStreamBenchmark>();
        var summary = BenchmarkRunner.Run<DecompressorBenchmark>();
    }

    private static void VerifyCorrectness()
    {
        var benchmark = new AssetStreamBenchmark();
        benchmark.Setup();

        try
        {
            int totalRead = benchmark.ReadStream();
            Console.WriteLine($"Read {totalRead} bytes.");
            // In real scenario we would verify the content too, but here we just check we read everything without crashing
            if (totalRead != 64 * 1024 * 100)
            {
                throw new Exception($"Verification failed. Expected {64 * 1024 * 100} bytes, got {totalRead}");
            }
            Console.WriteLine("Verification passed.");
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    private static void VerifyDecompression()
    {
        Console.WriteLine("Verifying Decompression...");
        var decompressor = new Decompressor();

        // Generate random data
        var randomData = new byte[1024];
        new Random(123).NextBytes(randomData);
        var uncompressedBuffer = new byte[randomData.Length];

        // Compress it using ZLib
        using var memoryStream = new MemoryStream();
        using (var zlibStream = new ZLibStream(memoryStream, CompressionMode.Compress))
        {
            zlibStream.Write(randomData, 0, randomData.Length);
        }
        var compressedData = memoryStream.ToArray();

        // Decompress using the library
        decompressor.Decompress(compressedData, uncompressedBuffer, CompressionMethod.Zlib);

        // Verify content
        for (int i = 0; i < randomData.Length; i++)
        {
            if (randomData[i] != uncompressedBuffer[i])
            {
                throw new Exception($"Decompression Verification Failed at index {i}. Expected {randomData[i]}, got {uncompressedBuffer[i]}");
            }
        }
        Console.WriteLine("Decompression Verification Passed.");
    }
}
