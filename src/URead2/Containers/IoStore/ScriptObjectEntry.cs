namespace URead2.Containers.IoStore;

/// <summary>
/// Script object entry from global.utoc.
/// </summary>
public record ScriptObjectEntry(
    string ObjectName,
    ulong GlobalIndex,
    ulong OuterIndex,
    ulong CDOClassIndex
);
