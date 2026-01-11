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
    /// True if this is a null reference.
    /// </summary>
    public bool IsNull => Index == 0;

    /// <summary>
    /// True if this references an import (external object).
    /// </summary>
    public bool IsImport => Index < 0;

    /// <summary>
    /// True if this references an export (local object).
    /// </summary>
    public bool IsExport => Index > 0;

    /// <summary>
    /// Gets the import array index (0-based) if this is an import.
    /// </summary>
    public int ImportIndex => IsImport ? -Index - 1 : -1;

    /// <summary>
    /// Gets the export array index (0-based) if this is an export.
    /// </summary>
    public int ExportIndex => IsExport ? Index - 1 : -1;

    /// <summary>
    /// Returns the reference in Unreal's standard format: Type'Path.Name'
    /// </summary>
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

    /// <summary>
    /// Creates a null reference.
    /// </summary>
    public static ObjectReference Null => new() { Index = 0 };
}
