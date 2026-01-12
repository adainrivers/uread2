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
    ulong PublicExportHash = 0
)
{
    /// <summary>
    /// The class name. Can be updated after resolution.
    /// </summary>
    public string ClassName { get; set; } = ClassName;
}
