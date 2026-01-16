namespace URead2.Containers.IoStore;

/// <summary>
/// Index of script objects from global.utoc for resolving script imports.
/// </summary>
public record ScriptObjectIndex(
    string[] NameMap,
    Dictionary<ulong, ScriptObjectEntry> Objects
)
{
    /// <summary>
    /// Resolves a script import (FPackageObjectIndex) to its class name.
    /// </summary>
    public string? ResolveImport(ulong packageObjectIndex)
    {
        // Extract type from top 2 bits
        var type = (int)(packageObjectIndex >> 62);
        if (type != 1) // Not a ScriptImport
            return null;

        if (Objects.TryGetValue(packageObjectIndex, out var entry))
            return entry.ObjectName;

        return null;
    }

    /// <summary>
    /// Resolves a script import to its full path including module.
    /// Returns (ObjectName, ModuleName) tuple.
    /// </summary>
    public (string ObjectName, string? ModuleName)? ResolveImportWithModule(ulong packageObjectIndex)
    {
        // Extract type from top 2 bits
        var type = (int)(packageObjectIndex >> 62);
        if (type != 1) // Not a ScriptImport
            return null;

        if (!Objects.TryGetValue(packageObjectIndex, out var entry))
            return null;

        // Walk the outer chain to find the module (top-level package)
        var moduleName = FindModuleName(entry.OuterIndex);
        return (entry.ObjectName, moduleName);
    }

    /// <summary>
    /// Finds the module name for a script object.
    /// For classes like SceneComponent, the immediate outer is typically the module (Engine).
    /// </summary>
    private string? FindModuleName(ulong outerIndex)
    {
        // Null outer means this is already top-level (is itself a module)
        if (outerIndex == ~0UL || outerIndex == 0)
            return null;

        // Get the immediate outer - for classes this is typically the module package
        if (!Objects.TryGetValue(outerIndex, out var outer))
            return null;

        // The immediate outer's name is the module name (e.g., "Engine" for SceneComponent)
        return outer.ObjectName;
    }
}
