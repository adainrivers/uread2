using System.Text;
using Serilog;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers.Abstractions;
using URead2.Containers.IoStore.Models;
using URead2.IO;
using URead2.Profiles.Abstractions;

namespace URead2.Containers.IoStore;

/// <summary>
/// Reads IO Store containers (.utoc/.ucas) and exposes their entries.
/// </summary>
public class IoStoreReader : IContainerReader
{
    // Format constants
    private const string TocMagic = "-==--==--==--==-";
    private const int TocMagicLength = 16;
    private const int MinTocFileSize = 144;
    private const int ChunkIdSize = 12;
    private const int ChunkOffsetLengthSize = 10;
    private const uint InvalidIndex = 0xFFFFFFFF;

    // Header reserved field sizes
    private const int HeaderReserved0Size = 1;
    private const int HeaderReserved1Size = 2;
    private const int HeaderReserved3Size = 1;
    private const int HeaderReserved4Size = 2;
    private const int HeaderReserved7Size = 4;
    private const int HeaderReserved8Size = 40;

    public IEnumerable<IAssetEntry> ReadEntries(string filePath, IProfile profile, byte[]? aesKey = null)
    {
        Log.Verbose("Reading IO Store TOC: {FilePath}", filePath);

        using var archive = new ArchiveReader(filePath);

        if (archive.Length < MinTocFileSize)
        {
            Log.Warning("IO Store TOC too small: {FilePath}", filePath);
            yield break;
        }

        var header = ReadHeader(archive);
        if (header == null)
        {
            Log.Warning("Invalid IO Store TOC (no valid header): {FilePath}", filePath);
            yield break;
        }

        Log.Verbose("IO Store version {Version}, EntryCount={EntryCount}, Encrypted={Encrypted}, Indexed={Indexed}",
            header.Version, header.EntryCount, header.IsEncrypted, header.IsIndexed);

        var casFilePath = Path.ChangeExtension(filePath, ".ucas");

        foreach (var entry in ReadIndex(archive, header, casFilePath, aesKey))
        {
            yield return entry;
        }
    }

    protected virtual TocHeader? ReadHeader(ArchiveReader archive)
    {
        archive.Seek(0);

        if (!archive.TryReadBytes(TocMagicLength, out var magicBytes))
            return null;

        var magic = Encoding.ASCII.GetString(magicBytes);
        if (magic != TocMagic)
            return null;

        if (!archive.TryReadByte(out var version))
            return null;

        if (!archive.TrySkip(HeaderReserved0Size) ||
            !archive.TrySkip(HeaderReserved1Size))
            return null;

        if (!archive.TryReadInt32(out var headerSize) ||
            !archive.TryReadInt32(out var entryCount) ||
            !archive.TryReadInt32(out var compressedBlockCount) ||
            !archive.TryReadInt32(out var compressedBlockEntrySize) ||
            !archive.TryReadInt32(out var compressionMethodCount) ||
            !archive.TryReadInt32(out var compressionMethodLength) ||
            !archive.TryReadInt32(out var compressionBlockSize) ||
            !archive.TryReadInt32(out var directoryIndexSize) ||
            !archive.TryReadInt32(out var partitionCount) ||
            !archive.TryReadUInt64(out var containerId) ||
            !archive.TryReadGuid(out var encryptionKeyGuid) ||
            !archive.TryReadByte(out var containerFlags))
            return null;

        if (!archive.TrySkip(HeaderReserved3Size) ||
            !archive.TrySkip(HeaderReserved4Size))
            return null;

        if (!archive.TryReadInt32(out var perfectHashSeedsCount) ||
            !archive.TryReadUInt64(out var partitionSize) ||
            !archive.TryReadInt32(out var chunksWithoutPerfectHashCount))
            return null;

        if (!archive.TrySkip(HeaderReserved7Size) ||
            !archive.TrySkip(HeaderReserved8Size))
            return null;

        var isIndexed = (containerFlags & 0x01) != 0;
        var isEncrypted = (containerFlags & 0x02) != 0;
        var isSigned = (containerFlags & 0x04) != 0;

        return new TocHeader(
            version, headerSize, entryCount, compressedBlockCount,
            compressionMethodCount, compressionMethodLength, compressionBlockSize,
            directoryIndexSize, perfectHashSeedsCount, chunksWithoutPerfectHashCount,
            isIndexed, isEncrypted, isSigned);
    }

    protected virtual IEnumerable<IoStoreEntry> ReadIndex(ArchiveReader archive, TocHeader header, string casFilePath, byte[]? aesKey)
    {
        if (header.IsEncrypted && aesKey == null)
            throw new NotSupportedException("Encrypted IO Store requires AES key");

        if (!header.IsIndexed || header.DirectoryIndexSize <= 0)
            yield break;

        archive.Seek(header.HeaderSize);

        // Skip chunk IDs
        if (!archive.TrySkip(header.EntryCount * ChunkIdSize))
            yield break;

        // Read chunk offset/lengths
        var chunkOffsetLengths = new List<(long Offset, long Length)>(header.EntryCount);
        for (var i = 0; i < header.EntryCount; i++)
        {
            if (!archive.TryReadBytes(ChunkOffsetLengthSize, out var data))
                yield break;
            var offset = ReadUInt40BE(data, 0);
            var length = ReadUInt40BE(data, 5);
            chunkOffsetLengths.Add((offset, length));
        }

        // Skip perfect hash seeds
        if (header.Version >= 4 && header.PerfectHashSeedsCount > 0)
        {
            if (!archive.TrySkip(header.PerfectHashSeedsCount * 4))
                yield break;
        }

        // Skip chunks without perfect hash
        if (header.Version >= 5 && header.ChunksWithoutPerfectHashCount > 0)
        {
            if (!archive.TrySkip(header.ChunksWithoutPerfectHashCount * 4))
                yield break;
        }

        // Read compression blocks
        var compressionBlocks = new List<IoStoreCompressionBlock>(header.CompressedBlockCount);
        for (int i = 0; i < header.CompressedBlockCount; i++)
        {
            if (!archive.TryReadBytes(12, out var data))
                yield break;

            ulong word0 = BitConverter.ToUInt64(data, 0);
            uint word1 = BitConverter.ToUInt32(data, 8);

            compressionBlocks.Add(new IoStoreCompressionBlock(
                CompressedOffset: (long)(word0 & 0xFFFFFFFFFF),      // Lower 40 bits
                CompressedSize: (int)(word0 >> 40 & 0xFFFFFF),    // Next 24 bits
                UncompressedSize: (int)(word1 & 0xFFFFFF),          // Lower 24 bits
                CompressionMethodIndex: (byte)(word1 >> 24)));       // Upper 8 bits
        }

        // Read compression method names
        var compressionMethods = new List<string> { "None" };
        for (int i = 0; i < header.CompressionMethodCount; i++)
        {
            if (!archive.TryReadBytes(header.CompressionMethodLength, out var nameBytes))
                yield break;

            int nullIndex = Array.IndexOf(nameBytes, (byte)0);
            if (nullIndex < 0) nullIndex = nameBytes.Length;
            if (nullIndex > 0)
                compressionMethods.Add(Encoding.ASCII.GetString(nameBytes, 0, nullIndex));
        }

        // Create shared TOC info
        var tocInfo = new IoStoreTocInfo(
            header.CompressionBlockSize,
            header.IsEncrypted,
            compressionBlocks,
            compressionMethods);

        // Skip signing data
        if (header.IsSigned)
        {
            if (!archive.TryReadInt32(out var hashSize))
                yield break;
            if (!archive.TrySkip(hashSize + hashSize + header.CompressedBlockCount * 20))
                yield break;
        }

        // Read directory index (may be encrypted)
        var (indexArchive, shouldDisposeArchive) = CreateIndexReader(archive, header, aesKey);

        try
        {
            if (!indexArchive.TryReadFString(out var mountPoint))
                yield break;

            mountPoint = NormalizeMountPoint(mountPoint);

            if (!indexArchive.TryReadInt32(out var dirCount))
                yield break;

            var directories = new List<(uint NameIdx, uint FirstChild, uint NextSibling, uint FirstFile)>(dirCount);
            for (var i = 0; i < dirCount; i++)
            {
                if (!indexArchive.TryReadUInt32(out var nameIdx) ||
                    !indexArchive.TryReadUInt32(out var firstChild) ||
                    !indexArchive.TryReadUInt32(out var nextSibling) ||
                    !indexArchive.TryReadUInt32(out var firstFile))
                    yield break;

                directories.Add((nameIdx, firstChild, nextSibling, firstFile));
            }

            if (!indexArchive.TryReadInt32(out var fileCount))
                yield break;

            var files = new List<(uint NameIdx, uint NextFile, uint ChunkIdx)>(fileCount);
            for (var i = 0; i < fileCount; i++)
            {
                if (!indexArchive.TryReadUInt32(out var nameIdx) ||
                    !indexArchive.TryReadUInt32(out var nextFile) ||
                    !indexArchive.TryReadUInt32(out var chunkIdx))
                    yield break;

                files.Add((nameIdx, nextFile, chunkIdx));
            }

            if (!indexArchive.TryReadInt32(out var stringCount))
                yield break;

            var strings = new List<string>(stringCount);
            for (var i = 0; i < stringCount; i++)
            {
                if (!indexArchive.TryReadFString(out var str))
                    yield break;
                strings.Add(str);
            }

            // Build paths
            foreach (var entry in BuildPaths(0, mountPoint, directories, files, strings, chunkOffsetLengths, casFilePath, tocInfo))
            {
                yield return entry;
            }
        }
        finally
        {
            if (shouldDisposeArchive)
                indexArchive.Dispose();
        }
    }

    private static (ArchiveReader Archive, bool ShouldDispose) CreateIndexReader(
        ArchiveReader archive,
        TocHeader header,
        byte[]? aesKey)
    {
        if (!header.IsEncrypted || aesKey == null)
            return (archive, false);

        Log.Verbose("Decrypting IO Store directory index");

        int alignedSize = Crypto.AesDecryptor.Align16(header.DirectoryIndexSize);
        if (!archive.TryReadBytes(alignedSize, out var encryptedData))
            throw new InvalidDataException("Failed to read encrypted directory index");

        var decryptedData = Crypto.AesDecryptor.Decrypt(encryptedData, aesKey);

        // Validate decryption by checking mount point length
        int mountPointLength = BitConverter.ToInt32(decryptedData, 0);
        if (mountPointLength < 0 || mountPointLength > 1024)
        {
            throw new InvalidDataException(
                $"Decryption failed - invalid mount point length: {mountPointLength}. " +
                "The AES key may be incorrect.");
        }

        var decryptedStream = new MemoryStream(decryptedData, 0, header.DirectoryIndexSize);
        return (new ArchiveReader(decryptedStream, leaveOpen: false), true);
    }

    private IEnumerable<IoStoreEntry> BuildPaths(
        uint startDirIdx,
        string startPath,
        List<(uint NameIdx, uint FirstChild, uint NextSibling, uint FirstFile)> directories,
        List<(uint NameIdx, uint NextFile, uint ChunkIdx)> files,
        List<string> strings,
        List<(long Offset, long Length)> chunkOffsetLengths,
        string casFilePath,
        IoStoreTocInfo tocInfo)
    {
        if (startDirIdx == InvalidIndex || startDirIdx >= directories.Count)
            yield break;

        // Use explicit stack to avoid stack overflow on deep directory hierarchies
        var stack = new Stack<(uint DirIdx, string ParentPath)>();
        stack.Push((startDirIdx, startPath));

        while (stack.Count > 0)
        {
            var (dirIdx, parentPath) = stack.Pop();

            if (dirIdx == InvalidIndex || dirIdx >= directories.Count)
                continue;

            var (nameIdx, firstChild, nextSibling, firstFile) = directories[(int)dirIdx];
            var dirName = nameIdx != InvalidIndex && nameIdx < strings.Count ? strings[(int)nameIdx] : "";
            var currentPath = !string.IsNullOrEmpty(dirName) ? parentPath + dirName + "/" : parentPath;

            // Files in this directory
            var fileIdx = firstFile;
            while (fileIdx != InvalidIndex && fileIdx < files.Count)
            {
                var (fnIdx, nextFile, chunkIdx) = files[(int)fileIdx];
                var fileName = fnIdx != InvalidIndex && fnIdx < strings.Count ? strings[(int)fnIdx] : "";

                if (!string.IsNullOrEmpty(fileName) && chunkIdx < chunkOffsetLengths.Count)
                {
                    var (offset, length) = chunkOffsetLengths[(int)chunkIdx];
                    yield return new IoStoreEntry(casFilePath, currentPath + fileName, offset, length, tocInfo);
                }

                fileIdx = nextFile;
            }

            // Push siblings first (processed last due to LIFO), then children
            if (nextSibling != InvalidIndex && nextSibling < directories.Count)
                stack.Push((nextSibling, parentPath));

            if (firstChild != InvalidIndex && firstChild < directories.Count)
                stack.Push((firstChild, currentPath));
        }
    }

    private static long ReadUInt40BE(byte[] data, int offset)
    {
        return data[offset + 4]
            | (long)data[offset + 3] << 8
            | (long)data[offset + 2] << 16
            | (long)data[offset + 1] << 24
            | (long)data[offset + 0] << 32;
    }

    private static string NormalizeMountPoint(string mountPoint)
    {
        if (mountPoint.StartsWith("../../../"))
            return mountPoint[9..];
        return mountPoint;
    }
}
