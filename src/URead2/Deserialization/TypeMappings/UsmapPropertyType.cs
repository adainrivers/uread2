namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// Describes the type of a property, including nested types for containers.
/// </summary>
public class UsmapPropertyType
{
    /// <summary>
    /// The property type.
    /// </summary>
    public EPropertyType Type { get; }

    /// <summary>
    /// For StructProperty: the struct type name.
    /// </summary>
    public string? StructType { get; init; }

    /// <summary>
    /// For EnumProperty: the enum name.
    /// </summary>
    public string? EnumName { get; init; }

    /// <summary>
    /// For ArrayProperty, SetProperty, OptionalProperty, EnumProperty: the inner type.
    /// For MapProperty: the key type.
    /// </summary>
    public UsmapPropertyType? InnerType { get; init; }

    /// <summary>
    /// For MapProperty: the value type.
    /// </summary>
    public UsmapPropertyType? ValueType { get; init; }

    public UsmapPropertyType(EPropertyType type)
    {
        Type = type;
    }

    public override string ToString()
    {
        return Type switch
        {
            EPropertyType.StructProperty => $"StructProperty<{StructType}>",
            EPropertyType.EnumProperty => $"EnumProperty<{EnumName}>",
            EPropertyType.ArrayProperty => $"ArrayProperty<{InnerType}>",
            EPropertyType.SetProperty => $"SetProperty<{InnerType}>",
            EPropertyType.MapProperty => $"MapProperty<{InnerType}, {ValueType}>",
            EPropertyType.OptionalProperty => $"OptionalProperty<{InnerType}>",
            _ => Type.ToString()
        };
    }
}
