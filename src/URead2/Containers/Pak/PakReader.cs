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
            archive.Skip(MagicOffsetFromInfoStart);

            var magic = archive.ReadUInt32();
            if (magic != Magic)
                continue;

            // Found valid magic, read the rest
            archive.Seek(-size, SeekOrigin.End);

            var encryptionKeyGuid = archive.ReadGuid();
            var isIndexEncrypted = archive.ReadBool();
            archive.Skip(4); // magic already read

            var version = archive.ReadInt32();
            var indexOffset = archive.ReadInt64();
            var indexSize = archive.ReadInt64();
            var indexHash = archive.ReadBytes(IndexHashSize);

            // Read compression methods (5 methods, 32 bytes each)
            var compressionMethods = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                var nameBytes = archive.ReadBytes(CompressionMethodNameLength);
                int nullIndex = Array.IndexOf(nameBytes, (byte)0);
                if (nullIndex < 0) nullIndex = CompressionMethodNameLength;
                if (nullIndex > 0)
                    compressionMethods.Add(System.Text.Encoding.ASCII.GetString(nameBytes, 0, nullIndex));
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
            var encryptedData = archive.ReadBytes(alignedSize);
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
            var mountPoint = indexArchive.ReadFString();
            mountPoint = NormalizeMountPoint(mountPoint);

            var entryCount = indexArchive.ReadInt32();
            indexArchive.Skip(8); // path hash seed

            var hasPathHashIndex = indexArchive.ReadInt32() != 0;
            if (hasPathHashIndex)
                indexArchive.Skip(8 + 8 + 20);

            var hasFullDirectoryIndex = indexArchive.ReadInt32() != 0;
            if (!hasFullDirectoryIndex)
                throw new InvalidDataException("Pak file does not have full directory index");

            var directoryIndexOffset = indexArchive.ReadInt64();
            var directoryIndexSize = indexArchive.ReadInt64();
            indexArchive.Skip(20); // directory index hash

            var encodedEntriesSize = indexArchive.ReadInt32();
            var encodedEntries = indexArchive.ReadBytes(encodedEntriesSize);

            Log.Verbose("DirectoryIndexOffset={DirectoryIndexOffset}, IndexOffset={IndexOffset}, IndexSize={IndexSize}, EncodedEntriesSize={EncodedEntriesSize}, CurrentPos={CurrentPos}",
                directoryIndexOffset, info.IndexOffset, info.IndexSize, encodedEntriesSize, indexArchive.Position);

            // Read directory index - stored at directoryIndexOffset in the pak file
            // For encrypted paks, the directory index itself may also be encrypted
            archive.Seek(directoryIndexOffset);

            Dictionary<string, Dictionary<string, int>> directories;
            if (info.IsIndexEncrypted && aesKey != null)
            {
                int alignedSize = Crypto.AesDecryptor.Align16((int)directoryIndexSize);
                var encryptedDirData = archive.ReadBytes(alignedSize);
                var decryptedDirData = Crypto.AesDecryptor.Decrypt(encryptedDirData, aesKey);

                using var dirStream = new MemoryStream(decryptedDirData, 0, (int)directoryIndexSize);
                using var dirArchive = new ArchiveReader(dirStream, leaveOpen: false);
                directories = ReadDirectoryIndex(dirArchive);
            }
            else
            {
                directories = ReadDirectoryIndex(archive);
            }

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

    private Dictionary<string, Dictionary<string, int>> ReadDirectoryIndex(ArchiveReader archive)
    {
        var directoryCount = archive.ReadInt32();
        var directories = new Dictionary<string, Dictionary<string, int>>(directoryCount);

        for (var i = 0; i < directoryCount; i++)
        {
            var directoryName = archive.ReadFString();
            var fileCount = archive.ReadInt32();
            var files = new Dictionary<string, int>(fileCount);

            for (var j = 0; j < fileCount; j++)
            {
                var fileName = archive.ReadFString();
                var encodedOffset = archive.ReadInt32();
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
            foreach (var (fileName, encodedOffset) in files)
            {
                archive.Position = encodedOffset;

                var path = CombinePaths(mountPoint, directory, fileName);
                var entry = DecodeEntry(archive, path, pakFilePath, compressionMethods);

                yield return entry;
            }
        }
    }

    private PakEntry DecodeEntry(ArchiveReader archive, string path, string pakFilePath, string[] compressionMethods)
    {
        var flags = archive.ReadUInt32();

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
            compressionBlockSize = archive.ReadUInt32();
        else
            compressionBlockSize = compressionBlockSizeField << 11;

        var offset = is32BitOffset ? archive.ReadUInt32() : archive.ReadInt64();
        var uncompressedSize = is32BitUncompressed ? archive.ReadUInt32() : archive.ReadInt64();
        var compressedSize = compressionMethodIndex != 0
            ? is32BitCompressed ? archive.ReadUInt32() : archive.ReadInt64()
            : uncompressedSize;

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
                var blockSize = archive.ReadUInt32();
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

    private static string CombinePaths(string mountPoint, string directory, string fileName)
    {
        if (string.IsNullOrEmpty(directory))
            return mountPoint + fileName;
        return mountPoint + directory + fileName;
    }
}
