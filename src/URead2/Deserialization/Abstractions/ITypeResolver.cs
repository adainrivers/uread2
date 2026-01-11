using URead2.Deserialization.TypeMappings;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Resolves type schemas for property deserialization.
/// Implementations can source schemas from .usmap files, asset-defined types
/// (BlueprintGeneratedClass, UserDefinedStruct, UserDefinedEnum), or other sources.
/// </summary>
public interface ITypeResolver
{
    /// <summary>
    /// Gets the schema for a class or struct type.
    /// </summary>
    /// <param name="typeName">The type name (e.g., "Actor", "MyBlueprintClass_C").</param>
    /// <returns>The schema, or null if not found.</returns>
    UsmapSchema? GetSchema(string typeName);

    /// <summary>
    /// Gets an enum definition.
    /// </summary>
    /// <param name="enumName">The enum name.</param>
    /// <returns>The enum definition, or null if not found.</returns>
    UsmapEnum? GetEnum(string enumName);

    /// <summary>
    /// Checks if this resolver has a schema for the given type.
    /// </summary>
    bool HasSchema(string typeName);

    /// <summary>
    /// Checks if this resolver has the given enum.
    /// </summary>
    bool HasEnum(string enumName);
}
