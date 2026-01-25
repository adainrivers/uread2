using BenchmarkDotNet.Attributes;
using URead2.Compression;
using ZstdSharp;

namespace URead2.Benchmark;

[MemoryDiagnoser]
public class ZstdBenchmark
{
    private byte[] _compressed;
    private byte[] _uncompressedBuffer;
    private URead2.Compression.Decompressor _decompressor;
    private const int DataSize = 64 * 1024; // 64 KB

    public byte[] Compressed => _compressed;
    public int OriginalSize => DataSize;

    [GlobalSetup]
    public void Setup()
    {
        var originalData = new byte[DataSize];
        new Random(42).NextBytes(originalData);

        using var compressor = new Compressor();
        _compressed = compressor.Wrap(originalData).ToArray();
        _uncompressedBuffer = new byte[DataSize];

        _decompressor = new URead2.Compression.Decompressor();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _decompressor.Dispose();
    }

    [Benchmark]
    public void DecompressZstd()
    {
        _decompressor.Decompress(_compressed, _uncompressedBuffer, CompressionMethod.Zstd);
    }
}
