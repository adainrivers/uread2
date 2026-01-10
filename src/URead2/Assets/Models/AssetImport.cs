namespace URead2.Assets.Models;

/// <summary>
/// A reference to an object in another package (import).
/// </summary>
public record AssetImport(
    string Name,
    string ClassName,
    string PackageName
);
