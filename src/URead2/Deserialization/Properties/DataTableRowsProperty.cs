namespace URead2.Deserialization.Properties;

/// <summary>
/// Property value containing DataTable rows as a dictionary of row name to row data.
/// </summary>
public sealed class DataTableRowsProperty : PropertyValue<Dictionary<string, PropertyBag>>
{
    /// <summary>
    /// The row struct type name.
    /// </summary>
    public string? RowStructType { get; }

    public DataTableRowsProperty(Dictionary<string, PropertyBag> rows, string? rowStructType = null)
    {
        Value = rows;
        RowStructType = rowStructType;
    }

    public override string ToString() => $"DataTableRows[{Value?.Count ?? 0} rows]";
}
