using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.IO;

namespace URead2.Assets;

/// <summary>
/// Reads metadata from traditional .uasset files (PAK format).
/// </summary>
public class UAssetMetadataReader : IAssetMetadataReader
{
    private const uint PackageFileMagic = 0x9E2A83C1;

    public virtual AssetMetadata? ReadMetadata(Stream stream, string name)
    {
        using var archive = new ArchiveReader(stream, leaveOpen: true);

        // Read and validate magic
        if (!archive.TryReadUInt32(out var magic) || magic != PackageFileMagic)
            return null;

        // Read summary
        var summary = ReadSummary(archive);
        if (summary == null)
            return null;

        // Read name table
        archive.Position = summary.NameOffset;
        var nameTable = new string[summary.NameCount];
        for (int i = 0; i < summary.NameCount; i++)
        {
            nameTable[i] = ReadNameEntry(archive, summary);
        }

        // Read imports
        archive.Position = summary.ImportOffset;
        var imports = new AssetImport[summary.ImportCount];
        for (int i = 0; i < summary.ImportCount; i++)
        {
            imports[i] = ReadImport(archive, nameTable, summary);
        }

        // Read exports
        archive.Position = summary.ExportOffset;
        var exports = new AssetExport[summary.ExportCount];
        for (int i = 0; i < summary.ExportCount; i++)
        {
            exports[i] = ReadExport(archive, nameTable, imports, exports, summary, i);
        }

        return new AssetMetadata(name, nameTable, exports, imports, 0, summary.IsUnversioned);
    }

    /// <summary>
    /// Reads the package summary header. Override for custom formats.
    /// </summary>
    protected virtual PackageSummary? ReadSummary(ArchiveReader archive)
    {
        // Legacy version
        if (!archive.TryReadInt32(out int legacyVersion))
            return null;

        if (legacyVersion >= 0)
            return null; // UE3 not supported

        if (legacyVersion != -4)
        {
            if (!archive.TrySkip(4)) // Legacy UE3 version
                return null;
        }

        if (!archive.TryReadInt32(out int fileVersionUE4))
            return null;

        int fileVersionUE5 = 0;
        if (legacyVersion <= -8)
        {
            if (!archive.TryReadInt32(out fileVersionUE5))
                return null;
        }

        if (!archive.TryReadInt32(out int fileVersionLicensee))
            return null;

        // Unversioned packages - assume latest
        if (fileVersionUE4 == 0 && fileVersionUE5 == 0 && fileVersionLicensee == 0)
        {
            fileVersionUE4 = 522;
            fileVersionUE5 = 1008;
        }

        // Custom versions
        if (legacyVersion <= -2)
        {
            if (!archive.TryReadInt32(out int customVersionCount))
                return null;
            if (!archive.TrySkip(customVersionCount * 20))
                return null;
        }

        if (!archive.TryReadInt32(out int totalHeaderSize))
            return null;

        if (!archive.TryReadFString(out _)) // PackageName
            return null;

        if (!archive.TryReadUInt32(out uint packageFlags))
            return null;

        bool isFilterEditorOnly = (packageFlags & 0x80000000) != 0;
        bool isUnversioned = (packageFlags & 0x2000) != 0; // PKG_UnversionedProperties

        if (!archive.TryReadInt32(out int nameCount) || !archive.TryReadInt32(out int nameOffset))
            return null;

        // Soft object paths (UE5.1+)
        if (fileVersionUE5 >= 1000)
        {
            if (!archive.TrySkip(8))
                return null;
        }

        // Localization ID
        if (!isFilterEditorOnly && fileVersionUE4 >= 516)
        {
            if (!archive.TryReadFString(out _))
                return null;
        }

        // Gatherable text data
        if (fileVersionUE4 >= 459)
        {
            if (!archive.TrySkip(8))
                return null;
        }

        if (!archive.TryReadInt32(out int exportCount) || !archive.TryReadInt32(out int exportOffset))
            return null;

        if (!archive.TryReadInt32(out int importCount) || !archive.TryReadInt32(out int importOffset))
            return null;

        return new PackageSummary(
            fileVersionUE4, fileVersionUE5,
            nameCount, nameOffset,
            importCount, importOffset,
            exportCount, exportOffset,
            isFilterEditorOnly,
            isUnversioned);
    }

    /// <summary>
    /// Reads a name entry from the name table. Override for custom name formats.
    /// </summary>
    protected virtual string ReadNameEntry(ArchiveReader archive, PackageSummary summary)
    {
        if (!archive.TryReadFString(out var name))
            return string.Empty;

        // Skip hash (UE4.14+)
        if (summary.FileVersionUE4 >= 504)
            archive.TrySkip(4);

        return name;
    }

    /// <summary>
    /// Reads an import entry. Override for custom import formats.
    /// </summary>
    protected virtual AssetImport ReadImport(ArchiveReader archive, string[] nameTable, PackageSummary summary)
    {
        var classPackage = ReadFName(archive, nameTable);
        var className = ReadFName(archive, nameTable);
        archive.TrySkip(4); // OuterIndex
        var objectName = ReadFName(archive, nameTable);

        string packageName = "";
        if (summary.FileVersionUE4 >= 522)
            packageName = ReadFName(archive, nameTable);

        // Optional flag (UE5+)
        if (summary.FileVersionUE5 >= 1000)
            archive.TrySkip(1);

        return new AssetImport(objectName, className, packageName);
    }

    /// <summary>
    /// Reads an export entry. Override for custom export formats.
    /// </summary>
    protected virtual AssetExport ReadExport(ArchiveReader archive, string[] nameTable, AssetImport[] imports, AssetExport[] exports, PackageSummary summary, int exportIndex)
    {
        if (!archive.TryReadInt32(out int classIndex))
            return new AssetExport("Unknown", 0, 0, -1, false, 0);

        if (!archive.TryReadInt32(out int superIndex))
            superIndex = 0;

        int templateIndex = 0;
        if (summary.FileVersionUE4 >= 378)
            archive.TryReadInt32(out templateIndex);

        if (!archive.TryReadInt32(out int outerIndex))
            outerIndex = 0;

        var objectName = ReadFName(archive, nameTable);

        if (!archive.TryReadUInt32(out uint objectFlags))
            objectFlags = 0;

        bool isPublic = (objectFlags & 1) != 0;

        long serialSize, serialOffset;
        if (summary.FileVersionUE4 < 511)
        {
            if (!archive.TryReadInt32(out var ss) || !archive.TryReadInt32(out var so))
            {
                serialSize = 0;
                serialOffset = 0;
            }
            else
            {
                serialSize = ss;
                serialOffset = so;
            }
        }
        else
        {
            if (!archive.TryReadInt64(out serialSize) || !archive.TryReadInt64(out serialOffset))
            {
                serialSize = 0;
                serialOffset = 0;
            }
        }

        // Booleans in export table are serialized as 4-byte integers
        archive.TrySkip(12); // ForcedExport (4), NotForClient (4), NotForServer (4)

        // Package GUID (removed in UE5)
        if (summary.FileVersionUE5 < 1000)
            archive.TrySkip(16);

        // IsInheritedInstance (UE5+) - 4 bytes
        if (summary.FileVersionUE5 >= 1000)
            archive.TrySkip(4);

        archive.TrySkip(4); // PackageFlags

        // NotAlwaysLoadedForEditorGame - 4 bytes
        if (summary.FileVersionUE4 >= 465)
            archive.TrySkip(4);

        // IsAsset - 4 bytes
        if (summary.FileVersionUE4 >= 485)
            archive.TrySkip(4);

        // GeneratePublicHash (UE5+) - 4 bytes
        if (summary.FileVersionUE5 >= 1000)
            archive.TrySkip(4);

        // Preload dependencies
        if (summary.FileVersionUE4 >= 507)
            archive.TrySkip(20);

        // Create export - resolve references to ResolvedRef objects
        var export = new AssetExport(objectName, serialOffset, serialSize, outerIndex, isPublic, 0);

        // Resolve class reference
        export.Class = ResolveToRef(classIndex, imports, exports, nameTable);

        // Resolve super reference
        export.Super = ResolveToRef(superIndex, imports, exports, nameTable);

        // Resolve template reference
        export.Template = ResolveToRef(templateIndex, imports, exports, nameTable);

        return export;
    }

    private static string ReadFName(ArchiveReader archive, string[] nameTable)
    {
        if (!archive.TryReadInt32(out int index) || !archive.TryReadInt32(out int number))
            return "None";

        if (index < 0 || index >= nameTable.Length)
            return "None";

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }

    /// <summary>
    /// Resolves a package index to a ResolvedRef.
    /// For imports, creates full reference. For local exports, creates partial reference.
    /// </summary>
    private static ResolvedRef? ResolveToRef(int packageIndex, AssetImport[] imports, AssetExport[] exports, string[] nameTable)
    {
        if (packageIndex == 0)
            return null;

        // Negative = import
        if (packageIndex < 0)
        {
            int importIndex = -packageIndex - 1;
            if (importIndex < imports.Length)
            {
                var import = imports[importIndex];
                return new ResolvedRef
                {
                    ClassName = import.ClassName,
                    Name = import.Name,
                    PackagePath = import.PackageName,
                    ExportIndex = -1 // Unknown for imports
                };
            }
        }
        else
        {
            // Positive = local export (may not be read yet)
            int exportIndex = packageIndex - 1;
            if (exportIndex < exports.Length && exports[exportIndex] != null)
            {
                var exp = exports[exportIndex];
                return new ResolvedRef
                {
                    ClassName = exp.ClassName,
                    Name = exp.Name,
                    PackagePath = "", // Will be filled in by AssetRegistry
                    ExportIndex = exportIndex
                };
            }
            else
            {
                // Export not yet read - create placeholder
                return new ResolvedRef
                {
                    ClassName = "Object",
                    Name = $"Export_{exportIndex}",
                    PackagePath = "",
                    ExportIndex = exportIndex
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Package summary with offsets and counts.
    /// </summary>
    protected record PackageSummary(
        int FileVersionUE4,
        int FileVersionUE5,
        int NameCount,
        int NameOffset,
        int ImportCount,
        int ImportOffset,
        int ExportCount,
        int ExportOffset,
        bool IsFilterEditorOnly,
        bool IsUnversioned
    );
}
