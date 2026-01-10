using System.Text;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.IO;

namespace URead2.Assets;

/// <summary>
/// Reads metadata from Zen package format (IO Store files).
/// </summary>
public class ZenPackageMetadataReader : IAssetMetadataReader
{
    private const int ExportEntrySize = 72;
    private const int ImportEntrySize = 8;

    public virtual AssetMetadata? ReadMetadata(Stream stream, string name)
    {
        using var archive = new ArchiveReader(stream, leaveOpen: true);

        // Need at least 8 bytes for basic header
        if (archive.Length < 8)
            return null;

        // Read summary
        var summary = ReadSummary(archive);
        if (summary == null)
            return null;

        // Validate offsets are within stream bounds
        long streamLength = archive.Length;
        if (summary.ImportMapOffset < 0 || summary.ImportMapOffset > streamLength ||
            summary.ExportMapOffset < 0 || summary.ExportMapOffset > streamLength ||
            summary.ExportBundleEntriesOffset < 0 || summary.ExportBundleEntriesOffset > streamLength)
            return null;

        // Skip versioning info if present
        if (summary.HasVersioningInfo)
            SkipVersioningInfo(archive);

        // Read name batch (local names)
        var nameTable = ReadNameBatch(archive);

        // Read imports
        int importCount = (summary.ExportMapOffset - summary.ImportMapOffset) / ImportEntrySize;
        if (importCount < 0 || importCount > 1000000)
            importCount = 0;

        var imports = new AssetImport[importCount];
        if (importCount > 0 && summary.ImportMapOffset < streamLength)
        {
            archive.Position = summary.ImportMapOffset;
            for (int i = 0; i < importCount && archive.Position + ImportEntrySize <= streamLength; i++)
            {
                imports[i] = ReadImport(archive);
            }
        }

        // Read exports
        int exportCount = (summary.ExportBundleEntriesOffset - summary.ExportMapOffset) / ExportEntrySize;
        if (exportCount < 0 || exportCount > 1000000)
            exportCount = 0;

        var exports = new AssetExport[exportCount];
        if (exportCount > 0 && summary.ExportMapOffset < streamLength)
        {
            archive.Position = summary.ExportMapOffset;
            for (int i = 0; i < exportCount && archive.Position + ExportEntrySize <= streamLength; i++)
            {
                exports[i] = ReadExport(archive, nameTable, imports);
            }
        }

        return new AssetMetadata(name, nameTable, exports, imports);
    }

    /// <summary>
    /// Reads the Zen package summary. Override for custom formats.
    /// </summary>
    protected virtual ZenSummary? ReadSummary(ArchiveReader archive)
    {
        bool hasVersioningInfo = archive.ReadUInt32() != 0;
        uint headerSize = archive.ReadUInt32();

        // Validate header size is reasonable (large maps can have headers up to 100MB+)
        if (headerSize == 0 || headerSize > 500 * 1024 * 1024)
            return null;

        archive.Skip(8); // Name (FMappedName)
        archive.Skip(4); // PackageFlags
        archive.Skip(4); // CookedHeaderSize

        archive.Skip(4); // ImportedPublicExportHashesOffset
        int importMapOffset = archive.ReadInt32();
        int exportMapOffset = archive.ReadInt32();
        int exportBundleEntriesOffset = archive.ReadInt32();

        // Skip remaining summary fields:
        // - DependencyBundleHeadersOffset (4)
        // - DependencyBundleEntriesOffset (4)
        // - ImportedPackageNamesOffset (4)
        archive.Skip(12);

        return new ZenSummary(hasVersioningInfo, importMapOffset, exportMapOffset, exportBundleEntriesOffset);
    }

    /// <summary>
    /// Skips versioning info section.
    /// </summary>
    protected virtual void SkipVersioningInfo(ArchiveReader archive)
    {
        long streamLength = archive.Length;

        // ZenVersion + PackageVersion + LicenseeVersion = 16 bytes
        if (archive.Position + 16 > streamLength)
            return;

        archive.Skip(4); // ZenVersion
        archive.Skip(8); // PackageVersion (FileVersionUE4 + FileVersionUE5)
        archive.Skip(4); // LicenseeVersion

        if (archive.Position + 4 > streamLength)
            return;

        int customVersionCount = archive.ReadInt32();
        if (customVersionCount < 0 || customVersionCount > 10000)
            return;

        long skipSize = (long)customVersionCount * 20;
        if (archive.Position + skipSize > streamLength)
            return;

        archive.Skip(skipSize); // Each: GUID (16) + Version (4)
    }

    /// <summary>
    /// Reads the name batch (local name map). Override for custom formats.
    /// </summary>
    protected virtual string[] ReadNameBatch(ArchiveReader archive)
    {
        long streamLength = archive.Length;

        int numNames = archive.ReadInt32();
        if (numNames <= 0 || numNames > 1000000) // Sanity check
            return [];

        int numStringBytes = archive.ReadInt32();
        if (numStringBytes < 0 || numStringBytes > streamLength)
            return [];

        archive.Skip(8); // hashVersion

        // Check we have enough room for hashes
        long hashesSize = (long)numNames * 8;
        if (archive.Position + hashesSize > streamLength)
            return [];

        // Skip hashes (8 bytes each)
        archive.Skip(hashesSize);

        // Check we have enough room for headers
        long headersSize = (long)numNames * 2;
        if (archive.Position + headersSize > streamLength)
            return [];

        // Read headers and strings
        var headerBytes = archive.ReadBytes(numNames * 2);
        var names = new string[numNames];

        for (int i = 0; i < numNames; i++)
        {
            byte b0 = headerBytes[i * 2];
            byte b1 = headerBytes[i * 2 + 1];
            bool isUtf16 = (b0 & 0x80) != 0;
            int length = (b0 & 0x7F) << 8 | b1;

            if (length < 0 || length > 10000) // Sanity check
            {
                names[i] = "";
                continue;
            }

            int byteLength = isUtf16 ? length * 2 : length;
            if (archive.Position + byteLength > streamLength)
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

    /// <summary>
    /// Reads an import entry. Override for custom formats.
    /// </summary>
    protected virtual AssetImport ReadImport(ArchiveReader archive)
    {
        ulong typeAndId = archive.ReadUInt64();

        // Decode type from top 2 bits
        int type = (int)(typeAndId >> 62);
        ulong value = typeAndId & 0x3FFFFFFFFFFFFFFFUL;

        string name, className, packageName;

        switch (type)
        {
            case 1: // ScriptImport
                name = $"ScriptImport_0x{value:X}";
                className = "ScriptObject";
                packageName = "/Script";
                break;
            case 2: // PackageImport
                uint pkgIdx = (uint)(value >> 32);
                uint hashIdx = (uint)value;
                name = $"PackageImport_{pkgIdx}_{hashIdx}";
                className = "Object";
                packageName = $"Package_{pkgIdx}";
                break;
            default:
                name = $"Import_0x{value:X}";
                className = "Unknown";
                packageName = "";
                break;
        }

        return new AssetImport(name, className, packageName);
    }

    /// <summary>
    /// Reads an export entry. Override for custom formats.
    /// </summary>
    protected virtual AssetExport ReadExport(ArchiveReader archive, string[] nameTable, AssetImport[] imports)
    {
        long startPos = archive.Position;

        ulong cookedSerialOffset = archive.ReadUInt64();
        ulong cookedSerialSize = archive.ReadUInt64();

        // ObjectName (FMappedName: 8 bytes)
        uint nameIndexRaw = archive.ReadUInt32();
        uint extraIndex = archive.ReadUInt32();

        uint nameIndex = nameIndexRaw & 0x3FFFFFFF;
        bool isGlobal = nameIndexRaw >> 30 != 0;

        string objectName;
        if (!isGlobal && nameIndex < nameTable.Length)
        {
            objectName = nameTable[nameIndex];
            if (extraIndex > 0)
                objectName = $"{objectName}_{extraIndex - 1}";
        }
        else
        {
            objectName = $"Name_{nameIndex}";
        }

        // OuterIndex (FPackageObjectIndex: 8 bytes)
        ulong outerRaw = archive.ReadUInt64();
        int outerIndex = outerRaw == ~0UL ? -1 : (int)(outerRaw & 0xFFFFFFFF);

        // ClassIndex (FPackageObjectIndex: 8 bytes)
        ulong classRaw = archive.ReadUInt64();
        string className = ResolveClassName(classRaw, imports);

        // Skip remaining fields (SuperIndex, TemplateIndex, PublicExportHash, ObjectFlags, FilterFlags)
        archive.Position = startPos + ExportEntrySize;

        // Read ObjectFlags for IsPublic
        archive.Position = startPos + 64; // Position of ObjectFlags
        uint objectFlags = archive.ReadUInt32();
        bool isPublic = (objectFlags & 1) != 0;

        archive.Position = startPos + ExportEntrySize;

        return new AssetExport(objectName, className, (long)cookedSerialOffset, (long)cookedSerialSize, outerIndex, isPublic);
    }

    private static string ResolveClassName(ulong classRaw, AssetImport[] imports)
    {
        if (classRaw == ~0UL)
            return "Object";

        int type = (int)(classRaw >> 62);

        switch (type)
        {
            case 0: // Export - class is in this package
                return "LocalClass";
            case 1: // ScriptImport
                return "ScriptClass";
            case 2: // PackageImport
                uint pkgIdx = (uint)((classRaw & 0x3FFFFFFFFFFFFFFFUL) >> 32);
                return $"ExternalClass_{pkgIdx}";
            default:
                return "Unknown";
        }
    }

    /// <summary>
    /// Zen package summary with offsets.
    /// </summary>
    protected record ZenSummary(
        bool HasVersioningInfo,
        int ImportMapOffset,
        int ExportMapOffset,
        int ExportBundleEntriesOffset
    );
}
