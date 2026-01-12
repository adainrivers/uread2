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
        var magic = archive.ReadUInt32();
        if (magic != PackageFileMagic)
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
            exports[i] = ReadExport(archive, nameTable, imports, summary);
        }

        return new AssetMetadata(name, nameTable, exports, imports, 0, summary.IsUnversioned);
    }

    /// <summary>
    /// Reads the package summary header. Override for custom formats.
    /// </summary>
    protected virtual PackageSummary? ReadSummary(ArchiveReader archive)
    {
        // Legacy version
        int legacyVersion = archive.ReadInt32();
        if (legacyVersion >= 0)
            return null; // UE3 not supported

        if (legacyVersion != -4)
            archive.Skip(4); // Legacy UE3 version

        int fileVersionUE4 = archive.ReadInt32();
        int fileVersionUE5 = legacyVersion <= -8 ? archive.ReadInt32() : 0;
        int fileVersionLicensee = archive.ReadInt32();

        // Unversioned packages - assume latest
        if (fileVersionUE4 == 0 && fileVersionUE5 == 0 && fileVersionLicensee == 0)
        {
            fileVersionUE4 = 522;
            fileVersionUE5 = 1008;
        }

        // Custom versions
        if (legacyVersion <= -2)
        {
            int customVersionCount = archive.ReadInt32();
            archive.Skip(customVersionCount * 20);
        }

        int totalHeaderSize = archive.ReadInt32();
        archive.ReadFString(); // PackageName
        uint packageFlags = archive.ReadUInt32();
        bool isFilterEditorOnly = (packageFlags & 0x80000000) != 0;
        bool isUnversioned = (packageFlags & 0x2000) != 0; // PKG_UnversionedProperties

        int nameCount = archive.ReadInt32();
        int nameOffset = archive.ReadInt32();

        // Soft object paths (UE5.1+)
        if (fileVersionUE5 >= 1000)
            archive.Skip(8);

        // Localization ID
        if (!isFilterEditorOnly && fileVersionUE4 >= 516)
            archive.ReadFString();

        // Gatherable text data
        if (fileVersionUE4 >= 459)
            archive.Skip(8);

        int exportCount = archive.ReadInt32();
        int exportOffset = archive.ReadInt32();
        int importCount = archive.ReadInt32();
        int importOffset = archive.ReadInt32();

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
        var name = archive.ReadFString();

        // Skip hash (UE4.14+)
        if (summary.FileVersionUE4 >= 504)
            archive.Skip(4);

        return name;
    }

    /// <summary>
    /// Reads an import entry. Override for custom import formats.
    /// </summary>
    protected virtual AssetImport ReadImport(ArchiveReader archive, string[] nameTable, PackageSummary summary)
    {
        var classPackage = ReadFName(archive, nameTable);
        var className = ReadFName(archive, nameTable);
        archive.Skip(4); // OuterIndex
        var objectName = ReadFName(archive, nameTable);

        string packageName = "";
        if (summary.FileVersionUE4 >= 522)
            packageName = ReadFName(archive, nameTable);

        // Optional flag (UE5+)
        if (summary.FileVersionUE5 >= 1000)
            archive.Skip(1);

        return new AssetImport(objectName, className, packageName);
    }

    /// <summary>
    /// Reads an export entry. Override for custom export formats.
    /// </summary>
    protected virtual AssetExport ReadExport(ArchiveReader archive, string[] nameTable, AssetImport[] imports, PackageSummary summary)
    {
        int classIndex = archive.ReadInt32();
        archive.Skip(4); // SuperIndex

        if (summary.FileVersionUE4 >= 378)
            archive.Skip(4); // TemplateIndex

        int outerIndex = archive.ReadInt32();
        var objectName = ReadFName(archive, nameTable);
        uint objectFlags = archive.ReadUInt32();
        bool isPublic = (objectFlags & 1) != 0;

        long serialSize, serialOffset;
        if (summary.FileVersionUE4 < 511)
        {
            serialSize = archive.ReadInt32();
            serialOffset = archive.ReadInt32();
        }
        else
        {
            serialSize = archive.ReadInt64();
            serialOffset = archive.ReadInt64();
        }

        // Booleans in export table are serialized as 4-byte integers
        archive.Skip(12); // ForcedExport (4), NotForClient (4), NotForServer (4)

        // Package GUID (removed in UE5)
        if (summary.FileVersionUE5 < 1000)
            archive.Skip(16);

        // IsInheritedInstance (UE5+) - 4 bytes
        if (summary.FileVersionUE5 >= 1000)
            archive.Skip(4);

        archive.Skip(4); // PackageFlags

        // NotAlwaysLoadedForEditorGame - 4 bytes
        if (summary.FileVersionUE4 >= 465)
            archive.Skip(4);

        // IsAsset - 4 bytes
        if (summary.FileVersionUE4 >= 485)
            archive.Skip(4);

        // GeneratePublicHash (UE5+) - 4 bytes
        if (summary.FileVersionUE5 >= 1000)
            archive.Skip(4);

        // Preload dependencies
        if (summary.FileVersionUE4 >= 507)
            archive.Skip(20);

        // Resolve class name from imports
        string className = ResolveClassName(classIndex, imports);

        return new AssetExport(objectName, className, serialOffset, serialSize, outerIndex, isPublic);
    }

    private static string ReadFName(ArchiveReader archive, string[] nameTable)
    {
        int index = archive.ReadInt32();
        int number = archive.ReadInt32();

        if (index < 0 || index >= nameTable.Length)
            return "None";

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }

    private static string ResolveClassName(int classIndex, AssetImport[] imports)
    {
        if (classIndex == 0)
            return "Object";

        // Negative = import, Positive = export
        if (classIndex < 0)
        {
            int importIndex = -classIndex - 1;
            if (importIndex < imports.Length)
                return imports[importIndex].Name; // Name is the actual class, ClassName is its type
        }

        return "Unknown";
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
