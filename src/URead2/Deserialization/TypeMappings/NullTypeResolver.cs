using URead2.Deserialization.Abstractions;

namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// A type resolver that returns nothing. Used as a default when no mappings are available.
/// </summary>
public sealed class NullTypeResolver : ITypeResolver
{
    public static readonly NullTypeResolver Instance = new();

    private NullTypeResolver() { }

    public UsmapSchema? GetSchema(string typeName) => null;
    public UsmapEnum? GetEnum(string enumName) => null;
    public bool HasSchema(string typeName) => false;
    public bool HasEnum(string enumName) => false;
}
