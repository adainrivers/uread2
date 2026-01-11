using URead2.Assets.Models;

namespace URead2.Deserialization.Properties;

/// <summary>
/// A resolved object reference with type, name, path, and index information.
/// </summary>
public record ObjectReference
{
    /// <summary>
    /// The object's class type (e.g., "Texture2D", "StaticMesh").
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// The object's name (e.g., "T_UI_item_icon").
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The package path (e.g., "/Game/_PD/UX/Inventory/Icons/T_UI_item_icon").
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// The raw package index. Negative = import, positive = export, zero = null.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// The resolved export metadata, if cross-package resolution succeeded.
    /// </summary>
    public AssetExport? ResolvedExport { get; init; }

    /// <summary>
    /// The resolved package metadata, if cross-package resolution succeeded.
    /// </summary>
    public AssetMetadata? ResolvedMetadata { get; init; }

    /// <summary>
    /// True if this reference was fully resolved to its target export.
    /// </summary>
    public bool IsFullyResolved { get; init; }

    // Computed helpers

    public static ObjectReference Null => new() { Index = 0 };

    public bool IsNull => Index == 0;
    public bool IsImport => Index < 0;
    public bool IsExport => Index > 0;
    public int ImportIndex => IsImport ? -Index - 1 : -1;
    public int ExportIndex => IsExport ? Index - 1 : -1;

    public override string ToString()
    {
        if (IsNull)
            return "None";

        if (Type != null && Path != null && Name != null)
            return $"{Type}'{Path}.{Name}'";

        if (Path != null && Name != null)
            return $"{Path}.{Name}";

        if (Name != null)
            return Name;

        return Index.ToString();
    }
}
