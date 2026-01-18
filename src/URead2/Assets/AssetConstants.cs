namespace URead2.Assets;

/// <summary>
/// Shared constants for asset processing.
/// </summary>
public static class AssetConstants
{
    /// <summary>
    /// Class names that indicate an export defines a type (Blueprint classes, structs, enums).
    /// </summary>
    public static readonly HashSet<string> TypeDefiningClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "BlueprintGeneratedClass",
        "WidgetBlueprintGeneratedClass",
        "AnimBlueprintGeneratedClass",
        "ScriptStruct",
        "UserDefinedStruct",
        "UserDefinedEnum"
    };
}
