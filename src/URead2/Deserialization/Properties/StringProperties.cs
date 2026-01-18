using URead2.Deserialization.Abstractions;
using URead2.IO;

namespace URead2.Deserialization.Properties;

/// <summary>
/// String property value (FString).
/// </summary>
public sealed class StrProperty : PropertyValue<string?>
{
    public StrProperty(string? value) => Value = value;

    public static StrProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new StrProperty(null);

        if (!ar.TryReadFString(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new StrProperty(null);
        }
        return new StrProperty(value);
    }
}

/// <summary>
/// Name property value (FName).
/// </summary>
public sealed class NameProperty : PropertyValue<string?>
{
    public NameProperty(string? value) => Value = value;

    public static NameProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new NameProperty(null);

        if (!ar.TryReadInt32(out int index) || !ar.TryReadInt32(out int number))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new NameProperty(null);
        }

        var nameTable = ctx.NameTable;
        if (index < 0 || index >= nameTable.Length)
        {
            ctx.Warn(DiagnosticCode.InvalidFNameIndex, ar.Position - 8, $"index={index}");
            return new NameProperty(null);
        }

        var name = nameTable[index];
        var value = number > 0 ? $"{name}_{number - 1}" : name;
        return new NameProperty(value);
    }
}

/// <summary>
/// Text property value (FText).
/// </summary>
public sealed class TextProperty : PropertyValue<string?>
{
    public TextProperty(string? value) => Value = value;

    public static TextProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new TextProperty(null);

        // FText is complex - simplified reading
        // Flags + HistoryType + content varies by type
        if (!ar.TryReadUInt32(out _) || !ar.TryReadByte(out var historyType))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new TextProperty(null);
        }

        var value = historyType switch
        {
            0 => ReadCultureInvariantString(ar, ctx), // None - culture invariant
            _ => null // Other types need more complex handling
        };

        return new TextProperty(value);
    }

    private static string? ReadCultureInvariantString(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TryReadInt32(out var hasCultureInvariant))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        if (hasCultureInvariant == 0)
            return null;

        if (!ar.TryReadFString(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return value;
    }
}
