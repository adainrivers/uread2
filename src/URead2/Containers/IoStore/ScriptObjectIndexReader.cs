using System.Text;
using URead2.IO;

namespace URead2.Containers.IoStore;

/// <summary>
/// Reads script object index from global.utoc/global.ucas.
/// </summary>
public class ScriptObjectIndexReader
{
    /// <summary>
    /// Reads script object index from global.utoc file.
    /// </summary>
    public ScriptObjectIndex? Read(string globalTocPath, byte[]? aesKey = null)
    {
        var globalCasPath = Path.ChangeExtension(globalTocPath, ".ucas");
        if (!File.Exists(globalCasPath))
            return null;

        using var tocArchive = new ArchiveReader(globalTocPath);

        var header = ReadTocHeader(tocArchive);
        if (header == null)
            return null;

        tocArchive.Seek(header.HeaderSize);

        var chunkIds = new List<(ulong Id, byte Type)>(header.EntryCount);
        for (int i = 0; i < header.EntryCount; i++)
        {
            if (!tocArchive.TryReadUInt64(out var chunkIdLow) ||
                !tocArchive.TryReadUInt32(out var chunkIdHigh))
                return null;

            var chunkType = (byte)(chunkIdHigh >> 24);
            chunkIds.Add((chunkIdLow, chunkType));
        }

        var chunkOffsetLengths = new List<(long Offset, long Length)>(header.EntryCount);
        for (int i = 0; i < header.EntryCount; i++)
        {
            if (!tocArchive.TryReadBytes(10, out var data))
                return null;

            var offset = ReadUInt40BE(data, 0);
            var length = ReadUInt40BE(data, 5);
            chunkOffsetLengths.Add((offset, length));
        }

        // Find ScriptObjects chunk (type 5 - EIoChunkType5.ScriptObjects)
        int scriptObjectsIndex = -1;
        for (int i = 0; i < chunkIds.Count; i++)
        {
            if (chunkIds[i].Type == 5)
            {
                scriptObjectsIndex = i;
                break;
            }
        }

        if (scriptObjectsIndex < 0)
            return null;

        var (chunkOffset, chunkLength) = chunkOffsetLengths[scriptObjectsIndex];

        using var casStream = File.OpenRead(globalCasPath);
        casStream.Seek(chunkOffset, SeekOrigin.Begin);

        var chunkData = new byte[chunkLength];
        casStream.ReadExactly(chunkData);

        if (header.IsEncrypted && aesKey != null)
        {
            chunkData = Crypto.AesDecryptor.Decrypt(chunkData, aesKey);
        }

        using var chunkArchive = new ArchiveReader(new MemoryStream(chunkData), leaveOpen: false);

        var nameMap = ReadNameBatch(chunkArchive);

        if (!chunkArchive.TryReadInt32(out var numScriptObjects))
            return null;

        var scriptObjects = new Dictionary<ulong, ScriptObjectEntry>(numScriptObjects);

        for (int i = 0; i < numScriptObjects; i++)
        {
            if (!chunkArchive.TryReadUInt32(out var nameIndexRaw) ||
                !chunkArchive.TryReadUInt32(out var extraIndex))
                break;

            var nameIndex = nameIndexRaw & 0x3FFFFFFF;
            var objectName = nameIndex < nameMap.Length
                ? (extraIndex > 0 ? $"{nameMap[nameIndex]}_{extraIndex - 1}" : nameMap[nameIndex])
                : $"Name_{nameIndex}";

            if (!chunkArchive.TryReadUInt64(out var globalIndex) ||
                !chunkArchive.TryReadUInt64(out var outerIndex) ||
                !chunkArchive.TryReadUInt64(out var cdoClassIndex))
                break;

            var entry = new ScriptObjectEntry(objectName, globalIndex, outerIndex, cdoClassIndex);
            scriptObjects[globalIndex] = entry;
        }

        return new ScriptObjectIndex(nameMap, scriptObjects);
    }

    private record TocHeader(int HeaderSize, int EntryCount, bool IsEncrypted);

    private TocHeader? ReadTocHeader(ArchiveReader archive)
    {
        if (!archive.TryReadBytes(16, out var magicBytes))
            return null;

        var magic = Encoding.ASCII.GetString(magicBytes);
        if (magic != "-==--==--==--==-")
            return null;

        if (!archive.TryReadByte(out _)) // version
            return null;

        if (!archive.TrySkip(1 + 2)) // Reserved
            return null;

        if (!archive.TryReadInt32(out var headerSize) ||
            !archive.TryReadInt32(out var entryCount))
            return null;

        if (!archive.TrySkip(4 * 6)) // Various counts
            return null;

        if (!archive.TrySkip(8)) // ContainerId
            return null;

        if (!archive.TrySkip(16)) // EncryptionKeyGuid
            return null;

        if (!archive.TryReadByte(out var containerFlags))
            return null;

        var isEncrypted = (containerFlags & 0x02) != 0;

        return new TocHeader(headerSize, entryCount, isEncrypted);
    }

    private static string[] ReadNameBatch(ArchiveReader archive)
    {
        if (!archive.TryReadInt32(out var numNames))
            return [];

        if (numNames <= 0 || numNames > 1000000)
            return [];

        if (!archive.TryReadInt32(out _)) // numStringBytes
            return [];

        if (!archive.TrySkip(8)) // hashVersion
            return [];

        if (!archive.TrySkip((long)numNames * 8)) // hashes
            return [];

        if (!archive.TryReadBytes(numNames * 2, out var headerBytes))
            return [];

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
                if (!archive.TryReadBytes(length * 2, out var bytes))
                {
                    names[i] = "";
                    continue;
                }
                names[i] = Encoding.Unicode.GetString(bytes);
            }
            else
            {
                if (!archive.TryReadBytes(length, out var bytes))
                {
                    names[i] = "";
                    continue;
                }
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
