using URead2.Assets.Models;

namespace URead2.TypeResolution;

/// <summary>
/// Helper for exporting all types and enums from the registry.
/// </summary>
public static class TypeExporter
{
    /// <summary>
    /// Gets all types from the registry as a list of serializable TypeInfo objects.
    /// </summary>
    public static List<TypeInfo> GetAllTypes(TypeRegistry registry)
    {
        return registry.Types
            .Select(t => new TypeInfo
            {
                Name = GetFullyQualifiedName(t),
                SuperName = t.Super != null ? GetFullyQualifiedName(t.Super) : t.SuperName,
                Source = t.Source,
                Kind = t.Kind,
                Properties = t.Properties.Count > 0
                    ? t.Properties.Values.Select(ToPropertyInfo).OrderBy(p => p.Index).ToList()
                    : null
            })
            .ToList();
    }

    /// <summary>
    /// Gets BP types from asset exports as a list of serializable TypeInfo objects.
    /// </summary>
    public static List<TypeInfo> GetBlueprintTypes(IEnumerable<(string PackagePath, AssetExport Export)> typeDefiningExports)
    {
        return typeDefiningExports
            .Select(t => new TypeInfo
            {
                Name = $"{t.PackagePath}.{t.Export.Name}",
                SuperName = t.Export.SuperClassName != null
                    ? (t.Export.Super?.PackagePath != null
                        ? $"{t.Export.Super.PackagePath}.{t.Export.SuperClassName}"
                        : t.Export.SuperClassName)
                    : null,
                Source = TypeSource.Asset,
                Kind = TypeKind.Class,
                Properties = null // BP types don't have property definitions in metadata
            })
            .ToList();
    }

    /// <summary>
    /// Builds a fully qualified name for a type (PackagePath.Name or just Name for runtime types).
    /// </summary>
    private static string GetFullyQualifiedName(TypeDefinition type)
    {
        if (string.IsNullOrEmpty(type.PackagePath))
            return type.Name;

        return $"{type.PackagePath}.{type.Name}";
    }

    private static PropertyInfo ToPropertyInfo(PropertyDefinition prop)
    {
        return new PropertyInfo
        {
            Name = prop.Name,
            Index = prop.Index,
            ArrayIndex = prop.ArrayIndex,
            ArraySize = prop.ArraySize,
            Type = ToPropertyTypeInfo(prop.Type)
        };
    }

    private static PropertyTypeInfo ToPropertyTypeInfo(PropertyType type)
    {
        return new PropertyTypeInfo
        {
            Kind = type.Kind.ToString(),
            StructName = type.StructName,
            EnumName = type.EnumName,
            InnerType = type.InnerType != null ? ToPropertyTypeInfo(type.InnerType) : null,
            ValueType = type.ValueType != null ? ToPropertyTypeInfo(type.ValueType) : null
        };
    }

    /// <summary>
    /// Gets all enums from the registry as a list of serializable EnumInfo objects.
    /// </summary>
    public static List<EnumInfo> GetAllEnums(TypeRegistry registry)
    {
        return registry.Enums
            .Select(e => new EnumInfo
            {
                Name = e.Name,
                Source = e.Source,
                Values = e.Values
                    .Select(v => new EnumValueInfo { Name = v.Value, Value = v.Key })
                    .OrderBy(v => v.Value)
                    .ToList()
            })
            .ToList();
    }

    /// <summary>
    /// Exports all types and enums from the registry only.
    /// </summary>
    public static TypeRegistryExport Export(TypeRegistry registry)
    {
        return new TypeRegistryExport
        {
            Types = GetAllTypes(registry),
            Enums = GetAllEnums(registry)
        };
    }

    /// <summary>
    /// Exports all types (runtime + BP) and enums.
    /// </summary>
    public static TypeRegistryExport Export(
        TypeRegistry registry,
        IEnumerable<(string PackagePath, AssetExport Export)> typeDefiningExports)
    {
        var runtimeTypes = GetAllTypes(registry);
        var bpTypes = GetBlueprintTypes(typeDefiningExports);

        return new TypeRegistryExport
        {
            Types = runtimeTypes.Concat(bpTypes).ToList(),
            Enums = GetAllEnums(registry)
        };
    }
}
