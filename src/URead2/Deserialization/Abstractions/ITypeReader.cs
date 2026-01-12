using URead2.Assets.Models;
using URead2.Deserialization.Properties;
using URead2.IO;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Reads type-specific data for classes with native serialization.
/// </summary>
public interface ITypeReader
{
    /// <summary>
    /// Reads type-specific data into a PropertyBag.
    /// </summary>
    /// <param name="ar">Archive reader positioned at start of export data.</param>
    /// <param name="context">Context providing name table and type resolution.</param>
    /// <param name="export">The export being deserialized.</param>
    /// <returns>A property bag containing the read data.</returns>
    PropertyBag Read(ArchiveReader ar, PropertyReadContext context, AssetExport export);
}
