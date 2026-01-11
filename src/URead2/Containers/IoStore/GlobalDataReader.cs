using System.Text;
using URead2.IO;

namespace URead2.Containers.IoStore;

/// <summary>
/// Reads global data from IO Store (script objects, global names).
/// </summary>
public class GlobalDataReader
{
    /// <summary>
    /// Script object entry from global data.
    /// </summary>
    public record ScriptObjectEntry(
        string ObjectName,
        ulong GlobalIndex,
        ulong OuterIndex,
        ulong CDOClassIndex
    );

    /// <summary>
    /// Global data containing script object mappings.
    /// </summary>
    public record GlobalData(
        string[] GlobalNameMap,
        Dictionary<ulong, ScriptObjectEntry> ScriptObjects
    );

    /// <summary>
    /// Reads global data from a global.ucas file using chunk info from global.utoc.
    /// </summary>
    public GlobalData? Read(string globalTocPath, byte[]? aesKey = null)
    {
        var globalCasPath = Path.ChangeExtension(globalTocPath, ".ucas");
        if (!File.Exists(globalCasPath))
            return null;

        // Read TOC to find script objects chunk
        using var tocArchive = new ArchiveReader(globalTocPath);

        var header = ReadTocHeader(tocArchive);
        if (header == null)
            return null;

        // For UE5+, chunk type for ScriptObjects is 0x0B (11)
        // We need to find the chunk and read it from the .ucas file

        // Read chunk IDs to find ScriptObjects
        tocArchive.Seek(header.HeaderSize);

        var chunkIds = new List<(ulong Id, byte Type)>(header.EntryCount);
        for (int i = 0; i < header.EntryCount; i++)
        {
            var chunkIdLow = tocArchive.ReadUInt64();
            var chunkIdHigh = tocArchive.ReadUInt32();
            // Type is in the high 8 bits of chunkIdHigh
            var chunkType = (byte)(chunkIdHigh >> 24);
            chunkIds.Add((chunkIdLow, chunkType));
        }

        // Read chunk offset/lengths
        var chunkOffsetLengths = new List<(long Offset, long Length)>(header.EntryCount);
        for (int i = 0; i < header.EntryCount; i++)
        {
            var data = tocArchive.ReadBytes(10);
            var offset = ReadUInt40BE(data, 0);
            var length = ReadUInt40BE(data, 5);
            chunkOffsetLengths.Add((offset, length));
        }

        // Find ScriptObjects chunk (type 5 for UE5 - EIoChunkType5.ScriptObjects)
        int scriptObjectsIndex = -1;
        for (int i = 0; i < chunkIds.Count; i++)
        {
            if (chunkIds[i].Type == 5) // ScriptObjects (EIoChunkType5)
            {
                scriptObjectsIndex = i;
                break;
            }
        }

        if (scriptObjectsIndex < 0)
            return null;

        var (chunkOffset, chunkLength) = chunkOffsetLengths[scriptObjectsIndex];

        // Read the chunk from .ucas
        using var casStream = File.OpenRead(globalCasPath);
        casStream.Seek(chunkOffset, SeekOrigin.Begin);

        var chunkData = new byte[chunkLength];
        casStream.ReadExactly(chunkData);

        // Decrypt if needed
        if (header.IsEncrypted && aesKey != null)
        {
            chunkData = Crypto.AesDecryptor.Decrypt(chunkData, aesKey);
        }

        using var chunkArchive = new ArchiveReader(new MemoryStream(chunkData), leaveOpen: false);

        // Read name batch (global names)
        var globalNameMap = ReadNameBatch(chunkArchive);

        // Read script objects - stored by GlobalIndex for lookup
        var numScriptObjects = chunkArchive.ReadInt32();
        var scriptObjects = new Dictionary<ulong, ScriptObjectEntry>(numScriptObjects);

        for (int i = 0; i < numScriptObjects; i++)
        {
            // FMappedName (8 bytes)
            var nameIndexRaw = chunkArchive.ReadUInt32();
            var extraIndex = chunkArchive.ReadUInt32();

            var nameIndex = nameIndexRaw & 0x3FFFFFFF;
            var objectName = nameIndex < globalNameMap.Length
                ? (extraIndex > 0 ? $"{globalNameMap[nameIndex]}_{extraIndex - 1}" : globalNameMap[nameIndex])
                : $"Name_{nameIndex}";

            // FPackageObjectIndex x 3 (8 bytes each)
            var globalIndex = chunkArchive.ReadUInt64();
            var outerIndex = chunkArchive.ReadUInt64();
            var cdoClassIndex = chunkArchive.ReadUInt64();

            var entry = new ScriptObjectEntry(objectName, globalIndex, outerIndex, cdoClassIndex);
            scriptObjects[globalIndex] = entry;
        }

        return new GlobalData(globalNameMap, scriptObjects);
    }

    /// <summary>
    /// Resolves a script import (FPackageObjectIndex) to its class name.
    /// </summary>
    public static string? ResolveScriptImport(GlobalData? globalData, ulong packageObjectIndex)
    {
        if (globalData == null)
            return null;

        // Extract type from top 2 bits
        var type = (int)(packageObjectIndex >> 62);
        if (type != 1) // Not a ScriptImport
            return null;

        // Look up in script objects by GlobalIndex
        if (globalData.ScriptObjects.TryGetValue(packageObjectIndex, out var entry))
            return entry.ObjectName;

        return null;
    }

    private record TocHeader(
        int HeaderSize,
        int EntryCount,
        bool IsEncrypted
    );

    private TocHeader? ReadTocHeader(ArchiveReader archive)
    {
        var magic = Encoding.ASCII.GetString(archive.ReadBytes(16));
        if (magic != "-==--==--==--==-")
            return null;

        var version = archive.ReadByte();
        archive.Skip(1 + 2); // Reserved
        var headerSize = archive.ReadInt32();
        var entryCount = archive.ReadInt32();
        archive.Skip(4 * 6); // Various counts
        archive.Skip(8); // ContainerId
        archive.Skip(16); // EncryptionKeyGuid
        var containerFlags = archive.ReadByte();
        var isEncrypted = (containerFlags & 0x02) != 0;

        return new TocHeader(headerSize, entryCount, isEncrypted);
    }

    private static string[] ReadNameBatch(ArchiveReader archive)
    {
        var numNames = archive.ReadInt32();
        if (numNames <= 0 || numNames > 1000000)
            return [];

        var numStringBytes = archive.ReadInt32();
        archive.Skip(8); // hashVersion

        // Skip hashes
        archive.Skip((long)numNames * 8);

        // Read headers
        var headerBytes = archive.ReadBytes(numNames * 2);
        var names = new string[numNames];

        for (int i = 0; i < numNames; i++)
        {
            byte b0 = headerBytes[i * 2];
            byte b1 = headerBytes[i * 2 + 1];
            bool isUtf16 = (b0 & 0x80) != 0;
            int length = (b0 & 0x7F) << 8 | b1;

            if (length < 0 || length > 10000)
            {
                names[i] = "";
                continue;
            }

            if (isUtf16)
            {
                var bytes = archive.ReadBytes(length * 2);
                names[i] = Encoding.Unicode.GetString(bytes);
            }
            else
            {
                var bytes = archive.ReadBytes(length);
                names[i] = Encoding.UTF8.GetString(bytes);
            }
        }

        return names;
    }

    private static long ReadUInt40BE(byte[] data, int offset)
    {
        return data[offset + 4]
            | (long)data[offset + 3] << 8
            | (long)data[offset + 2] << 16
            | (long)data[offset + 1] << 24
            | (long)data[offset + 0] << 32;
    }
}
