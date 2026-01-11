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
}
