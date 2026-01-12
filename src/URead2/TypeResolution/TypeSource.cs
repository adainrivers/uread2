namespace URead2.TypeResolution;

/// <summary>
/// Source of a type definition.
/// </summary>
public enum TypeSource
{
    /// <summary>
    /// Type from game runtime memory (loaded from .usmap file).
    /// </summary>
    Runtime,

    /// <summary>
    /// Type resolved from asset export (Blueprint types).
    /// </summary>
    Asset,

    /// <summary>
    /// Type registered manually (game-specific overrides).
    /// </summary>
    Manual
}
