using URead2.Assets.Models;
using URead2.Deserialization.Abstractions;
using URead2.Deserialization.Properties;
using URead2.IO;

namespace URead2.Deserialization.TypeReaders;

/// <summary>
/// Type reader for DataTable assets that reads row data.
/// DataTable serialization format:
/// 1. UObject properties (including RowStruct reference)
/// 2. Unknown 4 bytes (always 0)
/// 3. Row count (int32)
/// 4. For each row: row name (FName) + row struct properties
/// </summary>
public class DataTableTypeReader : ITypeReader
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static DataTableTypeReader Instance { get; } = new();

    private DataTableTypeReader() { }

    public PropertyBag Read(ArchiveReader ar, PropertyReadContext context, AssetExport export)
    {
        // 1. Read base UObject properties
        var bag = context.PropertyReader.ReadProperties(ar, context, export.ClassName, context.IsUnversioned);

        // 2. Get RowStruct type name from properties
        var rowStructName = ResolveRowStructName(bag);

        // 3. Skip unknown 4 bytes (always 0) and read row count
        if (!ar.TryReadInt32(out _)) // Skip unknown field
            return bag;

        if (!ar.TryReadInt32(out var numRows))
            return bag;

        if (numRows < 0 || numRows > 1000000)
            return bag;

        // 4. Validate we can read rows - if struct name is missing or schema unavailable
        // in unversioned mode, skip reading rows to avoid stream desync
        var canReadRows = !string.IsNullOrEmpty(rowStructName);
        if (canReadRows && context.IsUnversioned)
        {
            canReadRows = context.TypeRegistry.GetType(rowStructName!) != null;
        }

        // 5. Read rows into dictionary
        var rows = new Dictionary<string, PropertyBag>(numRows, StringComparer.Ordinal);
        if (canReadRows)
        {
            try
            {
                for (int i = 0; i < numRows; i++)
                {
                    var rowName = ReadFName(ar, context);
                    if (rowName == null)
                        break;
                    var rowData = ReadRowStruct(ar, context, rowStructName);
                    rows[rowName] = rowData;
                }
            }
            catch (Exception ex)
            {
                // Row struct schema is incomplete or wrong - stop reading but don't fail
                Serilog.Log.Debug(ex, "Failed to read DataTable rows for struct {StructName}", rowStructName);
            }
        }

        // 6. Store rows in property bag
        bag.Add("Rows", new DataTableRowsProperty(rows, rowStructName));

        return bag;
    }

    /// <summary>
    /// Resolves the row struct type name from the RowStruct property.
    /// </summary>
    private static string? ResolveRowStructName(PropertyBag bag)
    {
        var rowStructProp = bag.Get<ObjectProperty>("RowStruct");
        if (rowStructProp == null)
            return null;

        var reference = rowStructProp.Value;
        if (reference == null || reference.IsNull)
            return null;

        return reference.Name;
    }

    /// <summary>
    /// Reads a row struct using the property reader.
    /// </summary>
    private static PropertyBag ReadRowStruct(ArchiveReader ar, PropertyReadContext context, string? structName)
    {
        if (string.IsNullOrEmpty(structName))
            return new PropertyBag();

        return context.PropertyReader.ReadProperties(ar, context, structName, context.IsUnversioned);
    }

    /// <summary>
    /// Reads an FName from the archive using the name table.
    /// </summary>
    private static string? ReadFName(ArchiveReader ar, PropertyReadContext context)
    {
        if (!ar.TryReadInt32(out int index) || !ar.TryReadInt32(out int number))
            return null;

        var nameTable = context.NameTable;
        if (index < 0 || index >= nameTable.Length)
            return "None";

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }
}
