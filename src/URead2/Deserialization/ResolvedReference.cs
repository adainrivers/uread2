using URead2.Assets.Models;

namespace URead2.Deserialization;

/// <summary>
/// A fully resolved cross-package reference.
/// </summary>
public record ResolvedReference
{
    /// <summary>
    /// The object's class type.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// The object's name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The package path containing this object.
    /// </summary>
    public string? PackagePath { get; init; }

    /// <summary>
    /// The resolved export metadata.
    /// </summary>
    public AssetExport? Export { get; init; }

    /// <summary>
    /// The package metadata containing this export.
    /// </summary>
    public AssetMetadata? Metadata { get; init; }

    /// <summary>
    /// True if the reference was successfully resolved.
    /// </summary>
    public bool IsResolved { get; init; }
}
