namespace URead2.Assets.Models;

/// <summary>
/// An entry in the global export index, mapping a full export path to its location.
/// </summary>
public record ExportIndexEntry(
    AssetMetadata Metadata,
    int ExportIndex
);
