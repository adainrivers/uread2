using System.Runtime.InteropServices;
using URead2.TypeResolution;

namespace URead2.Deserialization.Properties;

/// <summary>
/// Array property value.
/// </summary>
public sealed class ArrayProperty : PropertyValue<PropertyValue[]>
{
    /// <summary>
    /// The inner element type name.
    /// </summary>
    public string? InnerType { get; }

    public ArrayProperty(PropertyValue[] value, string? innerType = null)
    {
        Value = value;
        InnerType = innerType;
    }

    public override string ToString() => $"[{Value?.Length ?? 0} elements]";
}

/// <summary>
/// Set property value.
/// </summary>
public sealed class SetProperty : PropertyValue<PropertyValue[]>
{
    /// <summary>
    /// The element type name.
    /// </summary>
    public string? ElementType { get; }

    public SetProperty(PropertyValue[] value, string? elementType = null)
    {
        Value = value;
        ElementType = elementType;
    }

    public override string ToString() => $"Set[{Value?.Length ?? 0} elements]";
}

/// <summary>
/// Map property value.
/// </summary>
public sealed class MapProperty : PropertyValue<MapEntry[]>
{
    /// <summary>
    /// The key type name.
    /// </summary>
    public string? KeyType { get; }

    /// <summary>
    /// The value type name.
    /// </summary>
    public string? ValueType { get; }

    public MapProperty(MapEntry[] value, string? keyType = null, string? valueType = null)
    {
        Value = value;
        KeyType = keyType;
        ValueType = valueType;
    }

    public override string ToString() => $"Map[{Value?.Length ?? 0} entries]";
}

/// <summary>
/// A key-value entry in a map.
/// </summary>
public readonly record struct MapEntry(PropertyValue Key, PropertyValue Value);

/// <summary>
/// Struct property value - contains nested properties.
/// </summary>
public sealed class StructProperty : PropertyValue<PropertyBag>
{
    /// <summary>
    /// The struct type name.
    /// </summary>
    public string? StructType { get; }

    public StructProperty(PropertyBag value, string? structType = null)
    {
        Value = value;
        StructType = structType;
    }

    public override string ToString() => StructType ?? "Struct";
}

/// <summary>
/// Optional property value (UE5+).
/// </summary>
public sealed class OptionalProperty : PropertyValue<PropertyValue?>
{
    /// <summary>
    /// The inner type name.
    /// </summary>
    public string? InnerType { get; }

    /// <summary>
    /// True if the optional has a value.
    /// </summary>
    public bool HasValue => Value != null;

    public OptionalProperty(PropertyValue? value, string? innerType = null)
    {
        Value = value;
        InnerType = innerType;
    }

    public override string? ToString() => HasValue ? Value!.ToString() : null;
}

/// <summary>
/// Enum property value.
/// </summary>
public sealed class EnumProperty : PropertyValue<string?>
{
    /// <summary>
    /// The enum type name.
    /// </summary>
    public string? EnumType { get; }

    public EnumProperty(string? value, string? enumType = null)
    {
        Value = value;
        EnumType = enumType;
    }

    public override string ToString() => Value ?? "None";
}

/// <summary>
/// A bag of properties (name -> value).
/// </summary>
public sealed class PropertyBag
{
    private readonly Dictionary<string, PropertyValue> _properties;

    public PropertyBag()
    {
        _properties = new Dictionary<string, PropertyValue>(StringComparer.Ordinal);
    }

    public PropertyBag(int capacity)
    {
        _properties = new Dictionary<string, PropertyValue>(capacity, StringComparer.Ordinal);
    }

    /// <summary>
    /// The class or struct type name.
    /// </summary>
    public string? TypeName { get; set; }

    /// <summary>
    /// The type definition used for deserialization (for debugging).
    /// </summary>
    public TypeDefinition? TypeDef { get; set; }

    /// <summary>
    /// Gets all property names.
    /// </summary>
    public IEnumerable<string> Names => _properties.Keys;

    /// <summary>
    /// Gets all properties.
    /// </summary>
    public IEnumerable<KeyValuePair<string, PropertyValue>> Properties => _properties;

    /// <summary>
    /// Gets the number of properties.
    /// </summary>
    public int Count => _properties.Count;

    /// <summary>
    /// Gets a property by name.
    /// </summary>
    public PropertyValue? this[string name] => _properties.GetValueOrDefault(name);

    /// <summary>
    /// Gets a typed property value by name.
    /// </summary>
    public T? Get<T>(string name) where T : PropertyValue
    {
        return _properties.GetValueOrDefault(name) as T;
    }

    /// <summary>
    /// Gets the raw value of a property by name.
    /// </summary>
    public object? GetValue(string name)
    {
        return _properties.GetValueOrDefault(name)?.GenericValue;
    }

    /// <summary>
    /// Gets the typed raw value of a property by name.
    /// </summary>
    public T? GetValue<T>(string name)
    {
        var prop = _properties.GetValueOrDefault(name);
        if (prop?.GenericValue is T value)
            return value;
        return default;
    }

    /// <summary>
    /// Adds a property.
    /// </summary>
    public void Add(string name, PropertyValue value)
    {
        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_properties, name, out _);
        slot = value;
    }

    /// <summary>
    /// Tries to get a property by name.
    /// </summary>
    public bool TryGet(string name, out PropertyValue? value)
    {
        return _properties.TryGetValue(name, out value);
    }

    /// <summary>
    /// Converts the property bag to a dictionary, recursively converting nested structures.
    /// </summary>
    public Dictionary<string, object?> ToDictionary()
    {
        var result = new Dictionary<string, object?>(_properties.Count);
        foreach (var (key, value) in _properties)
        {
            result[key] = ConvertValue(value);
        }
        return result;
    }

    private static object? ConvertValue(PropertyValue? value)
    {
        return value switch
        {
            null => null,
            StructProperty sp => sp.Value?.ToDictionary(),
            ArrayProperty ap => ConvertArray(ap.Value),
            SetProperty setp => ConvertArray(setp.Value),
            MapProperty mp => ConvertMap(mp.Value),
            DataTableRowsProperty dtp => ConvertDataTableRows(dtp.Value),
            ObjectProperty op => ConvertObjectReference(op.Value),
            EnumProperty ep => FormatEnumValue(ep),
            _ => ConvertGenericValue(value.GenericValue)
        };
    }

    private static Dictionary<string, object?> ConvertDataTableRows(Dictionary<string, PropertyBag>? rows)
    {
        if (rows == null || rows.Count == 0)
            return new Dictionary<string, object?>();

        var result = new Dictionary<string, object?>(rows.Count);
        foreach (var (rowName, rowData) in rows)
        {
            result[rowName] = rowData.ToDictionary();
        }
        return result;
    }

    private static string? FormatEnumValue(EnumProperty ep)
    {
        if (ep.Value == null)
            return null;

        // Format as EnumType::Value (e.g., "ECollisionResponse::ECR_Block")
        if (!string.IsNullOrEmpty(ep.EnumType))
            return $"{ep.EnumType}::{ep.Value}";

        return ep.Value;
    }

    private static object? ConvertObjectReference(ObjectReference? reference)
    {
        if (reference == null || reference.IsNull)
            return null;

        // CUE4Parse format: { "ObjectName": "Type'Name'", "ObjectPath": "/Path.Index" }
        var result = new Dictionary<string, object?>();

        // Build ObjectName: Type'Name'
        if (reference.Type != null && reference.Name != null)
        {
            result["ObjectName"] = $"{reference.Type}'{reference.Name}'";
        }
        else if (reference.Name != null)
        {
            result["ObjectName"] = reference.Name;
        }

        // Build ObjectPath based on reference type
        string? objectPath = BuildObjectPath(reference);
        if (objectPath != null)
        {
            result["ObjectPath"] = objectPath;
        }

        return result;
    }

    private static string? BuildObjectPath(ObjectReference reference)
    {
        // Script imports: /Script/Module paths are returned as-is (no index)
        if (reference.Path != null && reference.Path.StartsWith("/Script/", StringComparison.OrdinalIgnoreCase))
        {
            return reference.Path;
        }

        // Legacy: /Script without module - append name if available
        if (reference.Path != null && reference.Path.Equals("/Script", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(reference.Name))
                return $"/Script/{reference.Name}";
            return "/Script";
        }

        // Package imports (Type == "Package"): just the path, no index
        if (reference.Type != null && reference.Type.Equals("Package", StringComparison.OrdinalIgnoreCase))
        {
            if (reference.Path != null)
                return FormatObjectPath(reference.Path);
            return null;
        }

        // Unresolved placeholder paths - return as-is
        if (reference.Path != null && reference.Path.StartsWith("/Package_"))
        {
            return reference.Path;
        }

        // Local exports: /Path.ExportIndex
        if (reference.IsExport && reference.Path != null)
        {
            var formattedPath = FormatObjectPath(reference.Path);
            return $"{formattedPath}.{reference.ExportIndex}";
        }

        // Imports with resolved path: /Path.0
        if (reference.IsImport && reference.Path != null)
        {
            var formattedPath = FormatObjectPath(reference.Path);
            return $"{formattedPath}.0";
        }

        return null;
    }

    private static string FormatObjectPath(string path)
    {
        // Remove .uasset extension if present
        if (path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
            path = path[..^7];

        return path;
    }

    private static object? ConvertGenericValue(object? value)
    {
        return value switch
        {
            PropertyBag bag => bag.ToDictionary(),
            PropertyValue pv => ConvertValue(pv),
            float f when float.IsNaN(f) => "NaN",
            float f when float.IsPositiveInfinity(f) => "Infinity",
            float f when float.IsNegativeInfinity(f) => "-Infinity",
            double d when double.IsNaN(d) => "NaN",
            double d when double.IsPositiveInfinity(d) => "Infinity",
            double d when double.IsNegativeInfinity(d) => "-Infinity",
            _ => value
        };
    }

    private static object?[] ConvertArray(PropertyValue[]? values)
    {
        if (values == null || values.Length == 0)
            return [];

        var result = new object?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = ConvertValue(values[i]);
        }
        return result;
    }

    private static List<KeyValuePair<object?, object?>> ConvertMap(MapEntry[]? entries)
    {
        if (entries == null || entries.Length == 0)
            return [];

        var result = new List<KeyValuePair<object?, object?>>(entries.Length);
        foreach (var entry in entries)
        {
            var key = ConvertValue(entry.Key);
            var value = ConvertValue(entry.Value);
            result.Add(new KeyValuePair<object?, object?>(key, value));
        }
        return result;
    }
}
