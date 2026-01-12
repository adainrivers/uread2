using URead2.Assets.Models;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Properties;
using URead2.IO;

namespace URead2.Deserialization.TypeReaders;

/// <summary>
/// Type reader that skips deserialization and returns an empty PropertyBag.
/// Used for classes with native serialization that we don't need to parse.
/// </summary>
public class SkipTypeReader : ITypeReader
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static SkipTypeReader Instance { get; } = new();

    private SkipTypeReader() { }

    public PropertyBag Read(ArchiveReader ar, PropertyReadContext context, AssetExport export)
    {
        // Return empty bag - we don't read anything
        return new PropertyBag { TypeName = export.ClassName };
    }
}
