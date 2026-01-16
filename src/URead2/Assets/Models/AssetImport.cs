namespace URead2.Assets.Models;

/// <summary>
/// A reference to an object in another package (import).
/// </summary>
public class AssetImport
{
    /// <summary>
    /// Object name (may be placeholder until resolved).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Class name (may be placeholder until resolved).
    /// </summary>
    public string ClassName { get; set; }

    /// <summary>
    /// Package name/path (may be placeholder until resolved).
    /// </summary>
    public string PackageName { get; set; }

    /// <summary>
    /// For PackageImport type: index into ImportedPublicExportHashes array.
    /// -1 if not applicable.
    /// </summary>
    public int PublicExportHashIndex { get; init; }

    /// <summary>
    /// True if this import has been resolved to its target export.
    /// </summary>
    public bool IsResolved { get; set; }

    public AssetImport(string name, string className, string packageName, int publicExportHashIndex = -1)
    {
        Name = name;
        ClassName = className;
        PackageName = packageName;
        PublicExportHashIndex = publicExportHashIndex;
    }
}
