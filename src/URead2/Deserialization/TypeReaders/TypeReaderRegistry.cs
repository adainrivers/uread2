using URead2.Deserialization.Abstractions;

namespace URead2.Deserialization.TypeReaders;

/// <summary>
/// Registry for custom type readers that handle classes with native serialization.
/// </summary>
public class TypeReaderRegistry
{
    private readonly Dictionary<string, ITypeReader> _readers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a type reader for the specified class name.
    /// </summary>
    public ITypeReader? GetReader(string className)
    {
        return _readers.GetValueOrDefault(className);
    }

    /// <summary>
    /// Registers a custom type reader for a class.
    /// </summary>
    public void Register(string className, ITypeReader reader)
    {
        _readers[className] = reader;
    }

    /// <summary>
    /// Registers a custom type reader for multiple class names.
    /// </summary>
    public void Register(IEnumerable<string> classNames, ITypeReader reader)
    {
        foreach (var className in classNames)
        {
            _readers[className] = reader;
        }
    }
}
