using BenchmarkDotNet.Attributes;
using System.IO.Compression;
using URead2.Compression;

namespace URead2.Benchmark;

[MemoryDiagnoser]
public class DecompressorBenchmark
{
    private Decompressor _decompressor;
    private byte[] _compressedData;
    private byte[] _uncompressedBuffer;

    [GlobalSetup]
    public void Setup()
    {
        _decompressor = new Decompressor();

        // Generate random data
        var randomData = new byte[1024 * 1024]; // 1MB
        new Random(42).NextBytes(randomData);
        _uncompressedBuffer = new byte[randomData.Length];

        // Compress it using ZLib
        using var memoryStream = new MemoryStream();
        using (var zlibStream = new ZLibStream(memoryStream, CompressionMode.Compress))
        {
            zlibStream.Write(randomData, 0, randomData.Length);
        }
        _compressedData = memoryStream.ToArray();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _decompressor.Dispose();
    }

    [Benchmark]
    public void DecompressZlib()
    {
        _decompressor.Decompress(_compressedData, _uncompressedBuffer, CompressionMethod.Zlib);
    }
}
