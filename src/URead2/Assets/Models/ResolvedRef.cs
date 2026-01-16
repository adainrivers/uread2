namespace URead2.Assets.Models;

/// <summary>
/// A reference to an object, formatted to match CUE4Parse output.
/// </summary>
public record ResolvedRef
{
    /// <summary>
    /// The class name of the referenced object.
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    /// The object name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The package path (without extension).
    /// </summary>
    public required string PackagePath { get; init; }

    /// <summary>
    /// The export index within the package.
    /// </summary>
    public int ExportIndex { get; init; }

    /// <summary>
    /// Formatted as "ClassName'ObjectName'" for CUE4Parse compatibility.
    /// </summary>
    public string ObjectName => $"{ClassName}'{Name}'";

    /// <summary>
    /// Formatted as "PackagePath.ExportIndex" for CUE4Parse compatibility.
    /// </summary>
    public string ObjectPath => $"{PackagePath}.{ExportIndex}";

    /// <summary>
    /// Creates a ResolvedRef from resolved data.
    /// </summary>
    public static ResolvedRef Create(string className, string name, string packagePath, int exportIndex)
    {
        return new ResolvedRef
        {
            ClassName = className,
            Name = name,
            PackagePath = packagePath,
            ExportIndex = exportIndex
        };
    }
}
