namespace URead2.Assets.Models;

/// <summary>
/// Unreal Engine object flags.
/// </summary>
[Flags]
public enum EObjectFlags : uint
{
    None = 0,
    RF_Public = 0x00000001,
    RF_Standalone = 0x00000002,
    RF_MarkAsNative = 0x00000004,
    RF_Transactional = 0x00000008,
    RF_ClassDefaultObject = 0x00000010,
    RF_ArchetypeObject = 0x00000020,
    RF_Transient = 0x00000040,
    RF_MarkAsRootSet = 0x00000080,
    RF_TagGarbageTemp = 0x00000100,
    RF_NeedInitialization = 0x00000200,
    RF_NeedLoad = 0x00000400,
    RF_KeepForCooker = 0x00000800,
    RF_NeedPostLoad = 0x00001000,
    RF_NeedPostLoadSubobjects = 0x00002000,
    RF_NewerVersionExists = 0x00004000,
    RF_BeginDestroyed = 0x00008000,
    RF_FinishDestroyed = 0x00010000,
    RF_BeingRegenerated = 0x00020000,
    RF_DefaultSubObject = 0x00040000,
    RF_WasLoaded = 0x00080000,
    RF_TextExportTransient = 0x00100000,
    RF_LoadCompleted = 0x00200000,
    RF_InheritableComponentTemplate = 0x00400000,
    RF_DuplicateTransient = 0x00800000,
    RF_StrongRefOnFrame = 0x01000000,
    RF_NonPIEDuplicateTransient = 0x02000000,
    RF_Dynamic = 0x04000000,
    RF_WillBeLoaded = 0x08000000,
    RF_HasExternalPackage = 0x10000000,
    RF_PendingKill = 0x20000000,
    RF_Garbage = 0x40000000,
    RF_AllocatedInSharedPage = 0x80000000
}

/// <summary>
/// An object defined in this asset (UObject export).
/// </summary>
public record AssetExport(
    string Name,
    long SerialOffset,
    long SerialSize,
    int OuterIndex = -1,
    bool IsPublic = false,
    ulong PublicExportHash = 0,
    EObjectFlags ObjectFlags = EObjectFlags.None
)
{
    /// <summary>
    /// Reference to this export's class.
    /// </summary>
    public ResolvedRef? Class { get; set; }

    /// <summary>
    /// Reference to the super class (for class/struct exports).
    /// </summary>
    public ResolvedRef? Super { get; set; }

    /// <summary>
    /// Reference to the template/archetype.
    /// </summary>
    public ResolvedRef? Template { get; set; }

    /// <summary>
    /// Raw class reference for deferred resolution (Zen packages only).
    /// </summary>
    public PackageObjectIndex? ClassRef { get; init; }

    /// <summary>
    /// Raw super reference for deferred resolution (Zen packages only).
    /// </summary>
    public PackageObjectIndex? SuperRef { get; init; }

    /// <summary>
    /// Raw template reference for deferred resolution (Zen packages only).
    /// </summary>
    public PackageObjectIndex? TemplateRef { get; init; }

    // Convenience accessors for class name strings

    /// <summary>
    /// The class name (convenience accessor for Class?.Name).
    /// </summary>
    public string ClassName => Class?.Name ?? "Object";

    /// <summary>
    /// The super class name (convenience accessor for Super?.Name).
    /// </summary>
    public string? SuperClassName => Super?.Name;

    /// <summary>
    /// The template name (convenience accessor for Template?.Name).
    /// </summary>
    public string? TemplateName => Template?.Name;
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
