using URead2.Assets.Models;
using URead2.Deserialization.TypeMappings;

namespace URead2.Deserialization.Abstractions;

/// <summary>
/// Reads type schemas from asset exports (BlueprintGeneratedClass, UserDefinedStruct, UserDefinedEnum).
/// </summary>
public interface IAssetSchemaReader
{
    /// <summary>
    /// Reads all type schemas from an asset and registers them with the resolver.
    /// </summary>
    /// <param name="metadata">Asset metadata containing exports.</param>
    /// <param name="assetStream">Stream containing the asset data.</param>
    /// <param name="resolver">Resolver to register schemas with.</param>
    void ReadSchemas(AssetMetadata metadata, Stream assetStream, TypeResolver resolver);

    /// <summary>
    /// Reads a specific export as a schema if it's a type definition.
    /// </summary>
    /// <param name="metadata">Asset metadata.</param>
    /// <param name="assetStream">Stream containing the asset data.</param>
    /// <param name="exportIndex">Index of the export to read.</param>
    /// <returns>Schema if the export is a type definition, null otherwise.</returns>
    UsmapSchema? ReadSchemaFromExport(AssetMetadata metadata, Stream assetStream, int exportIndex);

    /// <summary>
    /// Reads a specific export as an enum if it's an enum definition.
    /// </summary>
    /// <param name="metadata">Asset metadata.</param>
    /// <param name="assetStream">Stream containing the asset data.</param>
    /// <param name="exportIndex">Index of the export to read.</param>
    /// <returns>Enum if the export is an enum definition, null otherwise.</returns>
    UsmapEnum? ReadEnumFromExport(AssetMetadata metadata, Stream assetStream, int exportIndex);
}
