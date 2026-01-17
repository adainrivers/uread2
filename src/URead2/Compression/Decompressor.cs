using System.IO.Compression;
using K4os.Compression.LZ4;
using OodleDotNet;
using Serilog;
using ZlibngDotNet;

namespace URead2.Compression;

/// <summary>
/// Decompresses data using various compression methods.
/// Thread-safe for concurrent Decompress calls after initialization.
/// Initialize Oodle/ZlibNg once at startup before any concurrent access.
/// </summary>
public class Decompressor : IDisposable
{
    private Oodle? _oodle;
    private Zlibng? _zlibng;
    private bool _disposed;

    public void InitializeOodle(string dllPath)
    {
        _oodle?.Dispose();

        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Oodle DLL not found: {dllPath}");

        _oodle = new Oodle(dllPath);
        Log.Information("Oodle initialized from {DllPath}", dllPath);
    }

    public void InitializeZlibNg(string dllPath)
    {
        _zlibng?.Dispose();

        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Zlib-ng DLL not found: {dllPath}");

        _zlibng = new Zlibng(dllPath);
        Log.Information("Zlib-ng initialized from {DllPath}", dllPath);
    }

    public void Decompress(
        ReadOnlySpan<byte> compressed,
        Span<byte> uncompressed,
        CompressionMethod method)
    {
        switch (method)
        {
            case CompressionMethod.None:
                compressed.CopyTo(uncompressed);
                break;

            case CompressionMethod.Zlib:
                DecompressZlib(compressed, uncompressed);
                break;

            case CompressionMethod.Gzip:
                DecompressGzip(compressed, uncompressed);
                break;

            case CompressionMethod.Oodle:
                DecompressOodle(compressed, uncompressed);
                break;

            case CompressionMethod.LZ4:
                DecompressLZ4(compressed, uncompressed);
                break;

            case CompressionMethod.Zstd:
                DecompressZstd(compressed, uncompressed);
                break;

            default:
                throw new NotSupportedException($"Compression method '{method}' is not supported");
        }
    }

    private void DecompressZlib(ReadOnlySpan<byte> compressed, Span<byte> uncompressed)
    {
        if (_zlibng != null)
        {
            var result = _zlibng.Uncompress(uncompressed, compressed, out int _);
            if (result != ZlibngCompressionResult.Ok)
                throw new InvalidDataException($"Zlib decompression failed: {result}");
        }
        else
        {
            using var srcStream = new MemoryStream(compressed.ToArray());
            using var zlibStream = new ZLibStream(srcStream, CompressionMode.Decompress);
            zlibStream.ReadExactly(uncompressed);
        }
    }

    private static void DecompressGzip(ReadOnlySpan<byte> compressed, Span<byte> uncompressed)
    {
        // BCL GZipStream requires Stream, no span-based API available
        // Gzip is rarely used in UE games, so this allocation is acceptable
        using var srcStream = new MemoryStream(compressed.ToArray());
        using var gzipStream = new GZipStream(srcStream, CompressionMode.Decompress);
        gzipStream.ReadExactly(uncompressed);
    }

    private void DecompressOodle(ReadOnlySpan<byte> compressed, Span<byte> uncompressed)
    {
        if (_oodle == null)
            throw new InvalidOperationException("Oodle not initialized. Call InitializeOodle first.");

        var result = _oodle.Decompress(compressed, uncompressed);
        if (result <= 0)
            throw new InvalidDataException($"Oodle decompression failed: {result}");
    }

    private static void DecompressLZ4(ReadOnlySpan<byte> compressed, Span<byte> uncompressed)
    {
        LZ4Codec.Decode(compressed, uncompressed);
    }

    private static void DecompressZstd(ReadOnlySpan<byte> compressed, Span<byte> uncompressed)
    {
        using var zstd = new ZstdSharp.Decompressor();
        zstd.Unwrap(compressed, uncompressed);
    }

    public static CompressionMethod ParseMethod(string? methodName)
    {
        if (string.IsNullOrEmpty(methodName))
            return CompressionMethod.None;

        return methodName.ToLowerInvariant() switch
        {
            "none" or "" => CompressionMethod.None,
            "zlib" => CompressionMethod.Zlib,
            "gzip" => CompressionMethod.Gzip,
            "oodle" => CompressionMethod.Oodle,
            "lz4" => CompressionMethod.LZ4,
            "zstd" => CompressionMethod.Zstd,
            _ => CompressionMethod.Unknown
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _oodle?.Dispose();
        _zlibng?.Dispose();
        _oodle = null;
        _zlibng = null;
        _disposed = true;
    }
}

public enum CompressionMethod
{
    None,
    Zlib,
    Gzip,
    Oodle,
    LZ4,
    Zstd,
    Unknown
}
