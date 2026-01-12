namespace URead2.Assets.Models;

/// <summary>
/// An object defined in this asset (UObject export).
/// </summary>
public record AssetExport(
    string Name,
    string ClassName,
    long SerialOffset,
    long SerialSize,
    int OuterIndex = -1,
    bool IsPublic = false,
    ulong PublicExportHash = 0,
    string? SuperClassName = null
)
{
    /// <summary>
    /// The class name. Can be updated after resolution.
    /// </summary>
    public string ClassName { get; set; } = ClassName;

    /// <summary>
    /// The super class name for class/struct exports. Can be updated after resolution.
    /// </summary>
    public string? SuperClassName { get; set; } = SuperClassName;

    /// <summary>
    /// Raw class reference for deferred resolution (Zen packages only).
    /// </summary>
    public PackageObjectIndex? ClassRef { get; init; }

    /// <summary>
    /// Raw super reference for deferred resolution (Zen packages only).
    /// </summary>
    public PackageObjectIndex? SuperRef { get; init; }
}

/// <summary>
/// Reference to an object in a package (local export, script import, or package import).
/// </summary>
public readonly record struct PackageObjectIndex(PackageObjectType Type, ulong Value)
{
    public static PackageObjectIndex? FromRaw(ulong raw)
    {
        if (raw == ~0UL)
            return null;

        var type = (PackageObjectType)(raw >> 62);
        var value = raw & 0x3FFFFFFFFFFFFFFFUL;
        return new PackageObjectIndex(type, value);
    }

    /// <summary>
    /// For local exports, gets the export index.
    /// </summary>
    public int ExportIndex => Type == PackageObjectType.Export ? (int)Value : -1;

    /// <summary>
    /// For package imports, gets the hash index.
    /// </summary>
    public uint HashIndex => Type == PackageObjectType.PackageImport ? (uint)Value : 0;
}

/// <summary>
/// Type of package object reference.
/// </summary>
public enum PackageObjectType
{
    Export = 0,
    ScriptImport = 1,
    PackageImport = 2
}
