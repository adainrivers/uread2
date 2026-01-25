using System.Text;
using Serilog;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers.Abstractions;
using URead2.Containers.Pak.Models;
using URead2.IO;
using URead2.Profiles.Abstractions;

namespace URead2.Containers.Pak;

/// <summary>
/// Reads pak files and exposes their entries.
/// </summary>
public class PakReader : IContainerReader
{
    // Format constants
    private const uint Magic = 0x5A6F12E1;
    private const int InfoSizeV9 = 222;
    private const int InfoSizeV8a = 221;
    private const int InfoSizeV8 = 189;
    private const int InfoSizeBase = 61;
    private static readonly int[] InfoSizesToTry = [InfoSizeV9, InfoSizeV8a, InfoSizeV8, InfoSizeBase];
    private const int MagicOffsetFromInfoStart = 17;
    private const int IndexHashSize = 20;
    private const int CompressionMethodNameLength = 32;

    public IEnumerable<IAssetEntry> ReadEntries(string filePath, IProfile profile, byte[]? aesKey = null)
    {
        Log.Verbose("Reading pak file: {FilePath}", filePath);

        using var archive = new ArchiveReader(filePath);

        var info = ReadPakInfo(archive);
        if (info == null)
        {
            Log.Warning("Invalid pak file (no valid header): {FilePath}", filePath);
            yield break;
        }

        Log.Verbose("Pak version {Version}, IndexOffset={IndexOffset}, IndexSize={IndexSize}, Encrypted={Encrypted}",
            info.Version, info.IndexOffset, info.IndexSize, info.IsIndexEncrypted);

        foreach (var entry in ReadIndex(archive, info, filePath, aesKey))
        {
            yield return entry;
        }
    }

    protected virtual PakInfo? ReadPakInfo(ArchiveReader archive)
    {
        foreach (var size in InfoSizesToTry)
        {
            if (archive.Length < size)
                continue;

            archive.Seek(-size, SeekOrigin.End);
            if (!archive.TrySkip(MagicOffsetFromInfoStart))
                continue;

            if (!archive.TryReadUInt32(out var magic))
                continue;

            if (magic != Magic)
                continue;

            // Found valid magic, read the rest
            archive.Seek(-size, SeekOrigin.End);

            if (!archive.TryReadGuid(out _)) // encryptionKeyGuid
                continue;

            if (!archive.TryReadByte(out var isIndexEncryptedByte))
                continue;

            bool isIndexEncrypted = isIndexEncryptedByte != 0;
            if (!archive.TrySkip(4)) // magic already validated
                continue;

            if (!archive.TryReadInt32(out var version) ||
                !archive.TryReadInt64(out var indexOffset) ||
                !archive.TryReadInt64(out var indexSize))
                continue;

            if (!archive.TrySkip(IndexHashSize)) // indexHash
                continue;

            // Read compression methods (5 methods, 32 bytes each)
            var compressionMethods = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                if (!archive.TryReadBytes(CompressionMethodNameLength, out var nameBytes))
                    break;

                int nullIndex = Array.IndexOf(nameBytes, (byte)0);
                if (nullIndex < 0) nullIndex = CompressionMethodNameLength;
                if (nullIndex > 0)
                    compressionMethods.Add(Encoding.ASCII.GetString(nameBytes, 0, nullIndex));
            }

            return new PakInfo(version, indexOffset, indexSize, isIndexEncrypted, compressionMethods.ToArray());
        }

        return null;
    }

    protected virtual IEnumerable<PakEntry> ReadIndex(ArchiveReader archive, PakInfo info, string filePath, byte[]? aesKey)
    {
        if (info.IsIndexEncrypted && aesKey == null)
            throw new NotSupportedException("Encrypted pak index requires AES key");

        archive.Seek(info.IndexOffset);

        // Read and optionally decrypt the index
        ArchiveReader indexArchive;
        MemoryStream? decryptedStream = null;

        if (info.IsIndexEncrypted && aesKey != null)
        {
            Log.Verbose("Decrypting pak index");

            int alignedSize = Crypto.AesDecryptor.Align16((int)info.IndexSize);
            if (!archive.TryReadBytes(alignedSize, out var encryptedData))
                yield break;

            var decryptedData = Crypto.AesDecryptor.Decrypt(encryptedData, aesKey);

            decryptedStream = new MemoryStream(decryptedData, 0, (int)info.IndexSize);
            indexArchive = new ArchiveReader(decryptedStream, leaveOpen: false);
        }
        else
        {
            indexArchive = archive;
        }

        try
        {
            if (!indexArchive.TryReadFString(out var mountPoint))
                yield break;

            mountPoint = NormalizeMountPoint(mountPoint);

            if (!indexArchive.TryReadInt32(out var entryCount))
                yield break;

            if (!indexArchive.TrySkip(8)) // path hash seed
                yield break;

            if (!indexArchive.TryReadInt32(out var hasPathHashIndexRaw))
                yield break;

            var hasPathHashIndex = hasPathHashIndexRaw != 0;
            if (hasPathHashIndex)
            {
                if (!indexArchive.TrySkip(8 + 8 + 20))
                    yield break;
            }

            if (!indexArchive.TryReadInt32(out var hasFullDirectoryIndexRaw))
                yield break;

            var hasFullDirectoryIndex = hasFullDirectoryIndexRaw != 0;
            if (!hasFullDirectoryIndex)
            {
                Log.Warning("Pak file does not have full directory index");
                yield break;
            }

            if (!indexArchive.TryReadInt64(out var directoryIndexOffset) ||
                !indexArchive.TryReadInt64(out var directoryIndexSize))
                yield break;

            if (!indexArchive.TrySkip(20)) // directory index hash
                yield break;

            if (!indexArchive.TryReadInt32(out var encodedEntriesSize))
                yield break;

            if (!indexArchive.TryReadBytes(encodedEntriesSize, out var encodedEntries))
                yield break;

            Log.Verbose("DirectoryIndexOffset={DirectoryIndexOffset}, IndexOffset={IndexOffset}, IndexSize={IndexSize}, EncodedEntriesSize={EncodedEntriesSize}, CurrentPos={CurrentPos}",
                directoryIndexOffset, info.IndexOffset, info.IndexSize, encodedEntriesSize, indexArchive.Position);

            // Read directory index - stored at directoryIndexOffset in the pak file
            // For encrypted paks, the directory index itself may also be encrypted
            archive.Seek(directoryIndexOffset);

            Dictionary<string, Dictionary<string, int>>? directories;
            if (info.IsIndexEncrypted && aesKey != null)
            {
                int alignedSize = Crypto.AesDecryptor.Align16((int)directoryIndexSize);
                if (!archive.TryReadBytes(alignedSize, out var encryptedDirData))
                    yield break;

                var decryptedDirData = Crypto.AesDecryptor.Decrypt(encryptedDirData, aesKey);

                using var dirStream = new MemoryStream(decryptedDirData, 0, (int)directoryIndexSize);
                using var dirArchive = new ArchiveReader(dirStream, leaveOpen: false);
                directories = ReadDirectoryIndex(dirArchive);
            }
            else
            {
                directories = ReadDirectoryIndex(archive);
            }

            if (directories == null)
                yield break;

            // Decode entries
            foreach (var entry in DecodeEntries(encodedEntries, directories, mountPoint, filePath, info.CompressionMethods))
            {
                yield return entry;
            }
        }
        finally
        {
            if (info.IsIndexEncrypted && decryptedStream != null)
            {
                indexArchive.Dispose();
            }
        }
    }

    private Dictionary<string, Dictionary<string, int>>? ReadDirectoryIndex(ArchiveReader archive)
    {
        if (!archive.TryReadInt32(out var directoryCount))
            return null;

        var directories = new Dictionary<string, Dictionary<string, int>>(directoryCount);

        for (var i = 0; i < directoryCount; i++)
        {
            if (!archive.TryReadFString(out var directoryName))
                return directories;

            if (!archive.TryReadInt32(out var fileCount))
                return directories;

            var files = new Dictionary<string, int>(fileCount);

            for (var j = 0; j < fileCount; j++)
            {
                if (!archive.TryReadFString(out var fileName))
                    break;

                if (!archive.TryReadInt32(out var encodedOffset))
                    break;

                files[fileName] = encodedOffset;
            }

            directories[directoryName] = files;
        }

        return directories;
    }

    private IEnumerable<PakEntry> DecodeEntries(
        byte[] encodedData,
        Dictionary<string, Dictionary<string, int>> directories,
        string mountPoint,
        string pakFilePath,
        string[] compressionMethods)
    {
        using var stream = new MemoryStream(encodedData);
        using var archive = new ArchiveReader(stream, leaveOpen: true);

        foreach (var (directory, files) in directories)
        {
            var directoryPath = string.IsNullOrEmpty(directory) ? mountPoint : mountPoint + directory;

            foreach (var (fileName, encodedOffset) in files)
            {
                archive.Position = encodedOffset;

                var path = directoryPath + fileName;
                var entry = DecodeEntry(archive, path, pakFilePath, compressionMethods);

                if (entry != null)
                    yield return entry;
            }
        }
    }

    private PakEntry? DecodeEntry(ArchiveReader archive, string path, string pakFilePath, string[] compressionMethods)
    {
        if (!archive.TryReadUInt32(out var flags))
            return null;

        var is32BitOffset = flags >> 31 != 0;
        var is32BitUncompressed = (flags >> 30 & 1) != 0;
        var is32BitCompressed = (flags >> 29 & 1) != 0;
        var compressionMethodIndex = (int)(flags >> 23 & 0x3F);
        var isEncrypted = (flags >> 22 & 1) != 0;
        var blockCount = (int)(flags >> 6 & 0xFFFF);
        var compressionBlockSizeField = flags & 0x3F;

        // Look up compression method name (index 0 = no compression, 1+ = index into methods array)
        string? compressionMethod = compressionMethodIndex > 0 && compressionMethodIndex <= compressionMethods.Length
            ? compressionMethods[compressionMethodIndex - 1]
            : null;

        // Compression block size
        uint compressionBlockSize;
        if (compressionBlockSizeField == 0x3F)
        {
            if (!archive.TryReadUInt32(out compressionBlockSize))
                return null;
        }
        else
        {
            compressionBlockSize = compressionBlockSizeField << 11;
        }

        long offset;
        if (is32BitOffset)
        {
            if (!archive.TryReadUInt32(out var offset32))
                return null;
            offset = offset32;
        }
        else
        {
            if (!archive.TryReadInt64(out offset))
                return null;
        }

        long uncompressedSize;
        if (is32BitUncompressed)
        {
            if (!archive.TryReadUInt32(out var uncompressed32))
                return null;
            uncompressedSize = uncompressed32;
        }
        else
        {
            if (!archive.TryReadInt64(out uncompressedSize))
                return null;
        }

        long compressedSize;
        if (compressionMethodIndex != 0)
        {
            if (is32BitCompressed)
            {
                if (!archive.TryReadUInt32(out var compressed32))
                    return null;
                compressedSize = compressed32;
            }
            else
            {
                if (!archive.TryReadInt64(out compressedSize))
                    return null;
            }
        }
        else
        {
            compressedSize = uncompressedSize;
        }

        // Fix compression block size for single block
        if (blockCount == 1 && compressionBlockSize == 0)
            compressionBlockSize = (uint)uncompressedSize;

        // Calculate struct size prepended to each file in the pak:
        // - Base: 53 bytes
        // - If compressed: + 4 + (blockCount * 16) bytes
        const int baseStructSize = 53;
        int structSize = baseStructSize;
        if (compressionMethodIndex != 0)
            structSize += 4 + blockCount * 16;

        var blocks = Array.Empty<PakCompressionBlock>();

        if (blockCount == 1 && !isEncrypted)
        {
            // Single unencrypted block - derive from entry
            blocks = [new PakCompressionBlock(structSize, structSize + compressedSize)];
        }
        else if (blockCount > 0)
        {
            // Multiple blocks - read sizes, compute offsets
            blocks = new PakCompressionBlock[blockCount];
            long blockOffset = structSize;

            for (int i = 0; i < blockCount; i++)
            {
                if (!archive.TryReadUInt32(out var blockSize))
                    return null;

                blocks[i] = new PakCompressionBlock(blockOffset, blockOffset + blockSize);
                blockOffset += isEncrypted ? blockSize + 15 & ~15u : blockSize;
            }
        }

        return new PakEntry(path, pakFilePath, offset, uncompressedSize, compressedSize,
            isEncrypted, compressionMethod, compressionBlockSize, blocks);
    }

    private static string NormalizeMountPoint(string mountPoint)
    {
        if (mountPoint.StartsWith("../../../"))
            return mountPoint[9..];
        return mountPoint;
    }
}
