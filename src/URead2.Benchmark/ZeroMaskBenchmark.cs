using BenchmarkDotNet.Attributes;
using URead2.Deserialization.Properties;
using URead2.Deserialization.Abstractions;
using URead2.IO;
using URead2.TypeResolution;

namespace URead2.Benchmark;

[MemoryDiagnoser]
public class ZeroMaskBenchmark
{
    private byte[] _data;
    private ArchiveReader _reader;
    private PropertyReadContext _context;
    private MemoryStream _memoryStream;

    // TestPropertyReader exposes protected static methods
    private class TestPropertyReader : PropertyReader
    {
        public static byte[] TestReadZeroMask(ArchiveReader ar, PropertyReadContext context, int numBits)
        {
            return ReadZeroMask(ar, context, numBits);
        }
    }

    [Params(32, 256, 4096)]
    public int NumBits { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        int byteCount = NumBits <= 8 ? 1 : NumBits <= 16 ? 2 : (NumBits + 31) / 32 * 4;
        _data = new byte[byteCount];
        new Random(42).NextBytes(_data);

        _memoryStream = new MemoryStream(_data);
        _reader = new ArchiveReader(_memoryStream, leaveOpen: true);

        _context = new PropertyReadContext
        {
            NameTable = [],
            TypeRegistry = new TypeRegistry(),
            PropertyReader = new PropertyReader()
        };
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _memoryStream.Position = 0;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader.Dispose();
        _memoryStream.Dispose();
    }

    [Benchmark]
    public byte[] ReadZeroMask()
    {
        _memoryStream.Position = 0;
        return TestPropertyReader.TestReadZeroMask(_reader, _context, NumBits);
    }

    public void Verify()
    {
        Setup();
        IterationSetup();
        var result = ReadZeroMask();

        for (int i = 0; i < NumBits; i++)
        {
            int byteIndex = i / 8;
            int bitIndex = i % 8;
            bool expected = false;
            if (byteIndex < _data.Length)
                expected = (_data[byteIndex] & (1 << bitIndex)) != 0;

            bool actual = false;
            if (byteIndex < result.Length)
                actual = (result[byteIndex] & (1 << bitIndex)) != 0;

            if (expected != actual)
                throw new Exception($"Mismatch at bit {i}. Expected {expected}, got {actual}");
        }
        Cleanup();
        Console.WriteLine($"Verification passed for {NumBits} bits.");
    }
}
