using System.Text;
using URead2.Containers.Pak;
using URead2.Containers.Pak.Models;
using URead2.IO;

namespace URead2.Profiles.Games.DuneAwakening;

/// <summary>
/// Custom pak reader for Dune Awakening.
///
/// Format (261 bytes from end):
/// -261: Custom header (40 bytes)
///   - CustomMagic (4 bytes) = 0xA590ED1E
///   - IndexOffset (8 bytes) - CORRECT offset to use
///   - IndexSize (8 bytes) - CORRECT size to use
///   - IndexHash (20 bytes)
///
/// -221: Standard UE header (221 bytes)
///   - EncryptionKeyGuid (16 bytes)
///   - IsIndexEncrypted (1 byte)
///   - StandardMagic (4 bytes) = 0x5A6F12E1
///   - Version (4 bytes)
///   - IndexOffset (8 bytes) - CORRUPTED, don't use
///   - IndexSize (8 bytes) - CORRUPTED, don't use
///   - IndexHash (20 bytes)
///   - CompressionMethods (5 * 32 = 160 bytes)
/// </summary>
public class DunePakReader : PakReader
{
    private const uint DuneMagic = 0xA590ED1E;
    private const uint StandardMagic = 0x5A6F12E1;
    private const int DuneInfoSize = 261;
    private const int StandardInfoSize = 221;
    private const int IndexHashSize = 20;
    private const int CompressionMethodNameLength = 32;

    protected override PakInfo? ReadPakInfo(ArchiveReader archive)
    {
        if (archive.Length < DuneInfoSize)
            return base.ReadPakInfo(archive);

        // Check for custom header at -261
        archive.Seek(-DuneInfoSize, SeekOrigin.End);

        if (!archive.TryReadUInt32(out var customMagic))
            return base.ReadPakInfo(archive);

        if (customMagic != DuneMagic)
            return base.ReadPakInfo(archive);

        // Read correct offset/size from custom header
        if (!archive.TryReadInt64(out var correctIndexOffset) ||
            !archive.TryReadInt64(out var correctIndexSize))
            return base.ReadPakInfo(archive);

        if (!archive.TrySkip(IndexHashSize)) // index hash
            return base.ReadPakInfo(archive);

        // Read standard header at -221 to get version and encryption flag
        archive.Seek(-StandardInfoSize, SeekOrigin.End);

        if (!archive.TrySkip(16)) // encryption key guid
            return base.ReadPakInfo(archive);

        if (!archive.TryReadByte(out var isIndexEncrypted))
            return base.ReadPakInfo(archive);

        if (!archive.TryReadUInt32(out var standardMagic))
            return base.ReadPakInfo(archive);

        if (standardMagic != StandardMagic)
            return base.ReadPakInfo(archive);

        if (!archive.TryReadInt32(out var version))
            return base.ReadPakInfo(archive);

        // Skip corrupted offset/size and index hash in standard header
        if (!archive.TrySkip(8 + 8 + IndexHashSize))
            return base.ReadPakInfo(archive);

        // Read compression methods (5 methods, 32 bytes each)
        var compressionMethods = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            if (!archive.TryReadBytes(CompressionMethodNameLength, out var nameBytes))
                return base.ReadPakInfo(archive);

            int nullIndex = Array.IndexOf(nameBytes, (byte)0);
            if (nullIndex < 0) nullIndex = CompressionMethodNameLength;
            if (nullIndex > 0)
                compressionMethods.Add(Encoding.ASCII.GetString(nameBytes, 0, nullIndex));
        }

        return new PakInfo(version, correctIndexOffset, correctIndexSize, isIndexEncrypted != 0, compressionMethods.ToArray());
    }
}
