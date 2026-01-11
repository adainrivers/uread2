using URead2.Compression;
using URead2.Deserialization.Abstractions;

namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// Type resolver that uses .usmap file data.
/// </summary>
public class UsmapTypeResolver : ITypeResolver
{
    private readonly TypeMappings? _mappings;

    public UsmapTypeResolver(TypeMappings? mappings = null)
    {
        _mappings = mappings;
    }

    /// <summary>
    /// Creates a resolver by reading a .usmap file.
    /// </summary>
    /// <param name="path">Path to the .usmap file.</param>
    /// <param name="decompressor">Optional decompressor for Oodle-compressed files.</param>
    public static UsmapTypeResolver FromFile(string path, Decompressor? decompressor = null)
    {
        var reader = new UsmapReader(decompressor);
        var mappings = reader.Read(path);
        return new UsmapTypeResolver(mappings);
    }

    /// <summary>
    /// Creates a resolver by reading a .usmap from a stream.
    /// </summary>
    /// <param name="stream">Stream containing the .usmap data.</param>
    /// <param name="decompressor">Optional decompressor for Oodle-compressed files.</param>
    public static UsmapTypeResolver FromStream(Stream stream, Decompressor? decompressor = null)
    {
        var reader = new UsmapReader(decompressor);
        var mappings = reader.Read(stream);
        return new UsmapTypeResolver(mappings);
    }

    public UsmapSchema? GetSchema(string typeName)
    {
        return _mappings?.GetSchema(typeName);
    }

    public UsmapEnum? GetEnum(string enumName)
    {
        return _mappings?.GetEnum(enumName);
    }

    public bool HasSchema(string typeName)
    {
        return _mappings?.GetSchema(typeName) != null;
    }

    public bool HasEnum(string enumName)
    {
        return _mappings?.GetEnum(enumName) != null;
    }
}
