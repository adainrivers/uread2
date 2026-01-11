using URead2.IO;

namespace URead2.Deserialization.Properties;

/// <summary>
/// String property value (FString).
/// </summary>
public sealed class StrProperty : PropertyValue<string?>
{
    public StrProperty(string? value) => Value = value;

    public StrProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? null : ar.ReadFString();
    }
}

/// <summary>
/// Name property value (FName).
/// </summary>
public sealed class NameProperty : PropertyValue<string?>
{
    public NameProperty(string? value) => Value = value;

    public NameProperty(ArchiveReader ar, string[] nameTable, ReadContext context)
    {
        if (context == ReadContext.Zero)
        {
            Value = null;
            return;
        }

        int index = ar.ReadInt32();
        int number = ar.ReadInt32();

        if (index < 0 || index >= nameTable.Length)
        {
            Value = null;
            return;
        }

        var name = nameTable[index];
        Value = number > 0 ? $"{name}_{number - 1}" : name;
    }
}

/// <summary>
/// Text property value (FText).
/// </summary>
public sealed class TextProperty : PropertyValue<string?>
{
    public TextProperty(string? value) => Value = value;

    public TextProperty(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero)
        {
            Value = null;
            return;
        }

        // FText is complex - simplified reading
        // Flags + HistoryType + content varies by type
        var flags = ar.ReadUInt32();
        var historyType = ar.ReadByte();

        Value = historyType switch
        {
            0 => ReadCultureInvariantString(ar), // None - culture invariant
            _ => null // Other types need more complex handling
        };
    }

    private static string? ReadCultureInvariantString(ArchiveReader ar)
    {
        var hasCultureInvariant = ar.ReadInt32() != 0;
        return hasCultureInvariant ? ar.ReadFString() : null;
    }
}
