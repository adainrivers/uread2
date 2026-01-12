namespace URead2.TypeResolution;

/// <summary>
/// Describes the type of a property, including nested types for containers.
/// </summary>
public sealed class PropertyType
{
    /// <summary>
    /// The property kind.
    /// </summary>
    public PropertyKind Kind { get; }

    /// <summary>
    /// For StructProperty: the struct type name.
    /// </summary>
    public string? StructName { get; init; }

    /// <summary>
    /// For EnumProperty: the enum name.
    /// </summary>
    public string? EnumName { get; init; }

    /// <summary>
    /// For ArrayProperty, SetProperty, OptionalProperty, EnumProperty: the inner type.
    /// For MapProperty: the key type.
    /// </summary>
    public PropertyType? InnerType { get; init; }

    /// <summary>
    /// For MapProperty: the value type.
    /// </summary>
    public PropertyType? ValueType { get; init; }

    public PropertyType(PropertyKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Creates a simple property type.
    /// </summary>
    public static PropertyType Simple(PropertyKind kind) => new(kind);

    /// <summary>
    /// Creates a struct property type.
    /// </summary>
    public static PropertyType Struct(string structName) => new(PropertyKind.StructProperty)
    {
        StructName = structName
    };

    /// <summary>
    /// Creates an enum property type.
    /// </summary>
    public static PropertyType Enum(string enumName, PropertyType? innerType = null) => new(PropertyKind.EnumProperty)
    {
        EnumName = enumName,
        InnerType = innerType ?? Simple(PropertyKind.ByteProperty)
    };

    /// <summary>
    /// Creates an array property type.
    /// </summary>
    public static PropertyType Array(PropertyType innerType) => new(PropertyKind.ArrayProperty)
    {
        InnerType = innerType
    };

    /// <summary>
    /// Creates a set property type.
    /// </summary>
    public static PropertyType Set(PropertyType innerType) => new(PropertyKind.SetProperty)
    {
        InnerType = innerType
    };

    /// <summary>
    /// Creates a map property type.
    /// </summary>
    public static PropertyType Map(PropertyType keyType, PropertyType valueType) => new(PropertyKind.MapProperty)
    {
        InnerType = keyType,
        ValueType = valueType
    };

    /// <summary>
    /// Creates an optional property type.
    /// </summary>
    public static PropertyType Optional(PropertyType innerType) => new(PropertyKind.OptionalProperty)
    {
        InnerType = innerType
    };

    public override string ToString()
    {
        return Kind switch
        {
            PropertyKind.StructProperty => $"Struct<{StructName}>",
            PropertyKind.EnumProperty => $"Enum<{EnumName}>",
            PropertyKind.ArrayProperty => $"Array<{InnerType}>",
            PropertyKind.SetProperty => $"Set<{InnerType}>",
            PropertyKind.MapProperty => $"Map<{InnerType}, {ValueType}>",
            PropertyKind.OptionalProperty => $"Optional<{InnerType}>",
            _ => Kind.ToString()
        };
    }
}
