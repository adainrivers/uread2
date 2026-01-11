using URead2.Deserialization.Abstractions;

namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// Chains multiple type resolvers, checking each in order until a match is found.
/// Useful for combining usmap schemas with asset-defined types.
/// </summary>
public class CompositeTypeResolver : ITypeResolver
{
    private readonly List<ITypeResolver> _resolvers = [];

    public CompositeTypeResolver() { }

    public CompositeTypeResolver(params ITypeResolver[] resolvers)
    {
        _resolvers.AddRange(resolvers);
    }

    public CompositeTypeResolver(IEnumerable<ITypeResolver> resolvers)
    {
        _resolvers.AddRange(resolvers);
    }

    /// <summary>
    /// Adds a resolver to the chain.
    /// </summary>
    public CompositeTypeResolver Add(ITypeResolver resolver)
    {
        _resolvers.Add(resolver);
        return this;
    }

    public UsmapSchema? GetSchema(string typeName)
    {
        foreach (var resolver in _resolvers)
        {
            var schema = resolver.GetSchema(typeName);
            if (schema != null)
                return schema;
        }
        return null;
    }

    public UsmapEnum? GetEnum(string enumName)
    {
        foreach (var resolver in _resolvers)
        {
            var enumDef = resolver.GetEnum(enumName);
            if (enumDef != null)
                return enumDef;
        }
        return null;
    }

    public bool HasSchema(string typeName)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.HasSchema(typeName))
                return true;
        }
        return false;
    }

    public bool HasEnum(string enumName)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.HasEnum(enumName))
                return true;
        }
        return false;
    }
}
