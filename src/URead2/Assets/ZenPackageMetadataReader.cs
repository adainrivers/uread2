using System.Text;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.Containers.IoStore;
using URead2.IO;

namespace URead2.Assets;

/// <summary>
/// Reads metadata from Zen package format (IO Store files).
/// </summary>
public class ZenPackageMetadataReader : IAssetMetadataReader
{
    private const int ExportEntrySize = 72;
    private const int ImportEntrySize = 8;

    /// <summary>
    /// Script object index for resolving script class names.
    /// </summary>
    public ScriptObjectIndex? ScriptObjectIndex { get; set; }

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

        // Read ImportedPublicExportHashes
        ulong[]? importedPublicExportHashes = null;
        int hashCount = (summary.ImportMapOffset - summary.ImportedPublicExportHashesOffset) / 8;
        if (hashCount > 0 && hashCount < 1000000 && summary.ImportedPublicExportHashesOffset < streamLength)
        {
            archive.Position = summary.ImportedPublicExportHashesOffset;
            importedPublicExportHashes = new ulong[hashCount];
            for (int i = 0; i < hashCount && archive.Position + 8 <= streamLength; i++)
            {
                importedPublicExportHashes[i] = archive.ReadUInt64();
            }
        }

        // Read ImportedPackageNames (UE5.3+)
        string[]? importedPackageNames = null;
        if (summary.ImportedPackageNamesOffset > 0 && summary.ImportedPackageNamesOffset < streamLength)
        {
            archive.Position = summary.ImportedPackageNamesOffset;
            importedPackageNames = ReadImportedPackageNames(archive, summary.CookedHeaderSize - summary.ImportedPackageNamesOffset);
        }

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
                imports[i] = ReadImport(archive, importedPackageNames);
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

        return new AssetMetadata(name, nameTable, exports, imports, summary.CookedHeaderSize, summary.IsUnversioned, importedPublicExportHashes, importedPackageNames);
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
        uint packageFlags = archive.ReadUInt32();
        bool isUnversioned = (packageFlags & 0x2000) != 0; // PKG_UnversionedProperties
        archive.Skip(4); // CookedHeaderSize

        int importedPublicExportHashesOffset = archive.ReadInt32();
        int importMapOffset = archive.ReadInt32();
        int exportMapOffset = archive.ReadInt32();
        int exportBundleEntriesOffset = archive.ReadInt32();

        // Read remaining fields to advance position correctly
        // This is either:
        // - UE5.3+: DependencyBundleHeadersOffset (4), DependencyBundleEntriesOffset (4), ImportedPackageNamesOffset (4)
        // - UE5.0-5.2: GraphDataOffset (4)
        // We try to detect the format and skip appropriately
        int field1 = archive.ReadInt32(); // GraphDataOffset or DependencyBundleHeadersOffset
        int importedPackageNamesOffset = -1;

        // Check if we have more data for the new format (UE5.3+)
        long currentPos = archive.Position;
        if (currentPos + 8 <= archive.Length)
        {
            int field2 = archive.ReadInt32(); // DependencyBundleEntriesOffset
            int field3 = archive.ReadInt32(); // ImportedPackageNamesOffset

            // Heuristic: In new format (UE5.3+), the fields should form a valid sequence
            // field1 = DependencyBundleHeadersOffset (after ExportBundleEntriesOffset)
            // field2 = DependencyBundleEntriesOffset (after field1)
            // field3 = ImportedPackageNamesOffset (after field2, before headerSize)
            if (field1 >= exportBundleEntriesOffset && field2 >= field1 && field3 >= field2 && field3 < (int)headerSize)
            {
                importedPackageNamesOffset = field3;
            }
            else
            {
                archive.Position = currentPos;
            }
        }

        // Use HeaderSize as the actual header size (where export data starts)
        return new ZenSummary(hasVersioningInfo, importedPublicExportHashesOffset, importMapOffset, exportMapOffset, exportBundleEntriesOffset, (int)headerSize, isUnversioned, importedPackageNamesOffset);
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
    /// Reads imported package names from the header (UE5.3+).
    /// Format: Count (4 bytes), then Count strings (each is FString).
    /// </summary>
    protected virtual string[] ReadImportedPackageNames(ArchiveReader archive, int maxSize)
    {
        if (archive.Position + 4 > archive.Length)
            return [];

        int count = archive.ReadInt32();
        if (count <= 0 || count > 100000)
            return [];

        var names = new string[count];
        for (int i = 0; i < count && archive.Position < archive.Length; i++)
        {
            if (!archive.TryReadFString(out var name))
                break;
            names[i] = name;
        }

        return names;
    }

    /// <summary>
    /// Reads an import entry. Override for custom formats.
    /// </summary>
    protected virtual AssetImport ReadImport(ArchiveReader archive, string[]? importedPackageNames = null)
    {
        ulong typeAndId = archive.ReadUInt64();

        // Decode type from top 2 bits
        int type = (int)(typeAndId >> 62);
        ulong value = typeAndId & 0x3FFFFFFFFFFFFFFFUL;

        string name, className, packageName;
        int exportHashIdx = -1;

        switch (type)
        {
            case 1: // ScriptImport - resolve from global script objects
                var resolved = ScriptObjectIndex?.ResolveImportWithModule(typeAndId);
                if (resolved.HasValue)
                {
                    name = resolved.Value.ObjectName;
                    className = "Class"; // Script imports are typically class references
                    var module = resolved.Value.ModuleName;
                    if (module != null)
                    {
                        // Module name might already include /Script/ prefix
                        packageName = module.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase)
                            ? module
                            : $"/Script/{module}";
                    }
                    else
                    {
                        packageName = "/Script";
                    }
                }
                else
                {
                    name = $"ScriptImport_0x{value:X}";
                    className = "ScriptObject";
                    packageName = "/Script";
                }
                break;

            case 2: // PackageImport - resolve from imported package names
                // The value contains: ImportedPackageIndex (bits 32-61) and ImportedPublicExportHashIndex (bits 0-31)
                uint pkgIdx = (uint)(value >> 32);
                exportHashIdx = (int)(value & 0xFFFFFFFF);

                // Try to get package name from imported package names array
                if (importedPackageNames != null && pkgIdx < importedPackageNames.Length &&
                    !string.IsNullOrEmpty(importedPackageNames[pkgIdx]))
                {
                    packageName = importedPackageNames[pkgIdx];
                    // Extract asset name from package path (last part after /)
                    var lastSlash = packageName.LastIndexOf('/');
                    name = lastSlash >= 0 ? packageName[(lastSlash + 1)..] : packageName;
                    className = "Object"; // Type will be resolved via PublicExportHash later
                }
                else
                {
                    // Store placeholder - will be resolved using PublicExportHash index
                    name = $"PackageImport_{pkgIdx}_{exportHashIdx:X8}";
                    className = "Object";
                    packageName = $"/Package_{pkgIdx}";
                }
                break;

            default:
                name = $"Import_0x{value:X}";
                className = "Unknown";
                packageName = "";
                break;
        }

        return new AssetImport(name, className, packageName, exportHashIdx);
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
        var classRef = PackageObjectIndex.FromRaw(classRaw);

        // SuperIndex (FPackageObjectIndex: 8 bytes)
        ulong superRaw = archive.ReadUInt64();
        var superRef = PackageObjectIndex.FromRaw(superRaw);

        // TemplateIndex (FPackageObjectIndex: 8 bytes)
        ulong templateRaw = archive.ReadUInt64();
        var templateRef = PackageObjectIndex.FromRaw(templateRaw);

        // PublicExportHash (8 bytes)
        ulong publicExportHash = archive.ReadUInt64();

        // Read ObjectFlags
        uint objectFlagsRaw = archive.ReadUInt32();
        var objectFlags = (EObjectFlags)objectFlagsRaw;
        bool isPublic = (objectFlagsRaw & 1) != 0;

        archive.Position = startPos + ExportEntrySize;

        // Create export with raw refs - resolution happens later in AssetRegistry
        return new AssetExport(objectName, (long)cookedSerialOffset, (long)cookedSerialSize, outerIndex, isPublic, publicExportHash, objectFlags)
        {
            ClassRef = classRef,
            SuperRef = superRef,
            TemplateRef = templateRef
        };
    }

    /// <summary>
    /// Zen package summary with offsets.
    /// </summary>
    protected record ZenSummary(
        bool HasVersioningInfo,
        int ImportedPublicExportHashesOffset,
        int ImportMapOffset,
        int ExportMapOffset,
        int ExportBundleEntriesOffset,
        int CookedHeaderSize,
        bool IsUnversioned,
        int ImportedPackageNamesOffset = -1
    );
}
