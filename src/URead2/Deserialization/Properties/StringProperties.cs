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
/// Text history type for FText serialization.
/// </summary>
public enum ETextHistoryType : sbyte
{
    None = -1,
    Base = 0,
    NamedFormat = 1,
    OrderedFormat = 2,
    ArgumentFormat = 3,
    AsNumber = 4,
    AsPercent = 5,
    AsCurrency = 6,
    AsDate = 7,
    AsTime = 8,
    AsDateTime = 9,
    Transform = 10,
    StringTableEntry = 11,
    TextGenerator = 12,
}

/// <summary>
/// Format argument type for FText.
/// </summary>
public enum EFormatArgumentType : sbyte
{
    Int = 0,
    UInt = 1,
    Float = 2,
    Double = 3,
    Text = 4,
    Gender = 5,
}

/// <summary>
/// Text property data for localization resolution.
/// </summary>
public sealed class TextData
{
    public ETextHistoryType HistoryType { get; init; }

    /// <summary>Namespace for Base history type (used with locres).</summary>
    public string? Namespace { get; init; }

    /// <summary>Key for Base/StringTableEntry history types.</summary>
    public string? Key { get; init; }

    /// <summary>Source string for Base history type.</summary>
    public string? SourceString { get; init; }

    /// <summary>Culture invariant string for None history type.</summary>
    public string? CultureInvariantString { get; init; }

    /// <summary>Table ID for StringTableEntry history type.</summary>
    public string? TableId { get; init; }

    /// <summary>Source format text for format history types (NamedFormat, OrderedFormat, etc.).</summary>
    public TextData? SourceFormat { get; init; }

    /// <summary>
    /// Gets the best available text representation.
    /// </summary>
    public string? Text => SourceString ?? CultureInvariantString ?? Key ?? SourceFormat?.Text;

    public override string ToString() => Text ?? $"[{HistoryType}]";
}

/// <summary>
/// Text property value (FText).
/// </summary>
public sealed class TextProperty : PropertyValue<TextData?>
{
    public TextProperty(TextData? value) => Value = value;

    /// <summary>
    /// Gets the text string for simple access.
    /// </summary>
    public override object? GenericValue => Value?.Text;

    public static TextProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new TextProperty(null);

        var value = ReadFText(ar, ctx);
        return new TextProperty(value);
    }

    /// <summary>
    /// Reads an FText from the archive.
    /// </summary>
    private static TextData? ReadFText(ArchiveReader ar, PropertyReadContext ctx)
    {
        // FText: Flags (uint32) + HistoryType (sbyte) + history-specific data
        if (!ar.TryReadUInt32(out _) || !ar.TryReadSByte(out var historyTypeByte))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        var historyType = (ETextHistoryType)historyTypeByte;
        return historyType switch
        {
            ETextHistoryType.None => ReadNone(ar, ctx),
            ETextHistoryType.Base => ReadBase(ar, ctx),
            ETextHistoryType.NamedFormat => ReadNamedFormat(ar, ctx),
            ETextHistoryType.OrderedFormat => ReadOrderedFormat(ar, ctx),
            ETextHistoryType.ArgumentFormat => ReadArgumentFormat(ar, ctx),
            ETextHistoryType.AsNumber => ReadFormatNumber(ar, ctx, historyType),
            ETextHistoryType.AsPercent => ReadFormatNumber(ar, ctx, historyType),
            ETextHistoryType.AsCurrency => ReadFormatNumber(ar, ctx, historyType),
            ETextHistoryType.AsDate => ReadAsDate(ar, ctx),
            ETextHistoryType.AsTime => ReadAsTime(ar, ctx),
            ETextHistoryType.AsDateTime => ReadAsDateTime(ar, ctx),
            ETextHistoryType.Transform => ReadTransform(ar, ctx),
            ETextHistoryType.StringTableEntry => ReadStringTableEntry(ar, ctx),
            ETextHistoryType.TextGenerator => ReadTextGenerator(ar, ctx),
            _ => ReadUnsupportedHistoryType(ar, ctx, historyType)
        };
    }

    /// <summary>
    /// None: optional culture invariant string.
    /// </summary>
    private static TextData? ReadNone(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TryReadInt32(out var hasCultureInvariant))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        string? cultureInvariantString = null;
        if (hasCultureInvariant != 0)
        {
            if (!ar.TryReadFString(out cultureInvariantString))
            {
                ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return null;
            }
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.None,
            CultureInvariantString = cultureInvariantString
        };
    }

    /// <summary>
    /// Base: namespace + key + sourceString.
    /// </summary>
    private static TextData? ReadBase(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TryReadFString(out var ns) ||
            !ar.TryReadFString(out var key) ||
            !ar.TryReadFString(out var sourceString))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.Base,
            Namespace = ns,
            Key = key,
            SourceString = sourceString
        };
    }

    /// <summary>
    /// NamedFormat: FText + dictionary of named arguments.
    /// </summary>
    private static TextData? ReadNamedFormat(ArchiveReader ar, PropertyReadContext ctx)
    {
        var sourceFmt = ReadFText(ar, ctx);
        if (ctx.HasFatalError) return null;

        if (!ar.TryReadInt32(out var argCount))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        for (int i = 0; i < argCount && !ctx.HasFatalError; i++)
        {
            if (!ar.TryReadFString(out _))
            {
                ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return null;
            }
            SkipFormatArgumentValue(ar, ctx);
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.NamedFormat,
            SourceFormat = sourceFmt
        };
    }

    /// <summary>
    /// OrderedFormat: FText + array of arguments.
    /// </summary>
    private static TextData? ReadOrderedFormat(ArchiveReader ar, PropertyReadContext ctx)
    {
        var sourceFmt = ReadFText(ar, ctx);
        if (ctx.HasFatalError) return null;

        if (!ar.TryReadInt32(out var argCount))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        for (int i = 0; i < argCount && !ctx.HasFatalError; i++)
        {
            SkipFormatArgumentValue(ar, ctx);
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.OrderedFormat,
            SourceFormat = sourceFmt
        };
    }

    /// <summary>
    /// ArgumentFormat: FText + array of argument data (name + value).
    /// </summary>
    private static TextData? ReadArgumentFormat(ArchiveReader ar, PropertyReadContext ctx)
    {
        var sourceFmt = ReadFText(ar, ctx);
        if (ctx.HasFatalError) return null;

        if (!ar.TryReadInt32(out var argCount))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        for (int i = 0; i < argCount && !ctx.HasFatalError; i++)
        {
            if (!ar.TryReadFString(out _))
            {
                ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return null;
            }
            SkipFormatArgumentValue(ar, ctx);
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.ArgumentFormat,
            SourceFormat = sourceFmt
        };
    }

    /// <summary>
    /// Skips a format argument value.
    /// </summary>
    private static void SkipFormatArgumentValue(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TryReadSByte(out var typeByte))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return;
        }

        var argType = (EFormatArgumentType)typeByte;
        switch (argType)
        {
            case EFormatArgumentType.Int:
                ar.TrySkip(8);
                break;
            case EFormatArgumentType.UInt:
                ar.TrySkip(8);
                break;
            case EFormatArgumentType.Float:
                ar.TrySkip(4);
                break;
            case EFormatArgumentType.Double:
                ar.TrySkip(8);
                break;
            case EFormatArgumentType.Text:
                ReadFText(ar, ctx);
                break;
            case EFormatArgumentType.Gender:
                ar.TrySkip(4);
                break;
        }
    }

    /// <summary>
    /// AsNumber/AsPercent/AsCurrency: optional currency + source value + optional format options + target culture.
    /// </summary>
    private static TextData? ReadFormatNumber(ArchiveReader ar, PropertyReadContext ctx, ETextHistoryType historyType)
    {
        if (historyType == ETextHistoryType.AsCurrency)
        {
            if (!ar.TryReadFString(out _))
            {
                ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return null;
            }
        }

        SkipFormatArgumentValue(ar, ctx);
        if (ctx.HasFatalError) return null;

        if (!ar.TryReadInt32(out var hasFormatOptions))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        if (hasFormatOptions != 0)
        {
            if (!ar.TrySkip(1 + 1 + 1 + 4 * 4))
            {
                ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return null;
            }
        }

        if (!ar.TryReadFString(out _))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData { HistoryType = historyType };
    }

    /// <summary>
    /// AsDate: FDateTime + DateStyle + optional TimeZone + TargetCulture.
    /// </summary>
    private static TextData? ReadAsDate(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TrySkip(8 + 1))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        if (!ar.TryReadFString(out _) || !ar.TryReadFString(out _))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData { HistoryType = ETextHistoryType.AsDate };
    }

    /// <summary>
    /// AsTime: FDateTime + TimeStyle + TimeZone + TargetCulture.
    /// </summary>
    private static TextData? ReadAsTime(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TrySkip(8 + 1))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        if (!ar.TryReadFString(out _) || !ar.TryReadFString(out _))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData { HistoryType = ETextHistoryType.AsTime };
    }

    /// <summary>
    /// AsDateTime: FDateTime + DateStyle + TimeStyle + TimeZone + TargetCulture.
    /// </summary>
    private static TextData? ReadAsDateTime(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TrySkip(8 + 1 + 1))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        if (!ar.TryReadFString(out _) || !ar.TryReadFString(out _))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData { HistoryType = ETextHistoryType.AsDateTime };
    }

    /// <summary>
    /// Transform: FText + TransformType.
    /// </summary>
    private static TextData? ReadTransform(ArchiveReader ar, PropertyReadContext ctx)
    {
        var sourceText = ReadFText(ar, ctx);
        if (ctx.HasFatalError) return null;

        if (!ar.TrySkip(1))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.Transform,
            SourceFormat = sourceText
        };
    }

    /// <summary>
    /// StringTableEntry: FName tableId + FString key.
    /// </summary>
    private static TextData? ReadStringTableEntry(ArchiveReader ar, PropertyReadContext ctx)
    {
        // Read TableId as FName
        if (!ar.TryReadInt32(out var tableIndex) || !ar.TryReadInt32(out var tableNumber))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        string? tableId = null;
        var nameTable = ctx.NameTable;
        if (tableIndex >= 0 && tableIndex < nameTable.Length)
        {
            tableId = nameTable[tableIndex];
            if (tableNumber > 0)
                tableId = $"{tableId}_{tableNumber - 1}";
        }

        if (!ar.TryReadFString(out var key))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData
        {
            HistoryType = ETextHistoryType.StringTableEntry,
            TableId = tableId,
            Key = key
        };
    }

    /// <summary>
    /// TextGenerator: FName generatorTypeID + optional contents.
    /// </summary>
    private static TextData? ReadTextGenerator(ArchiveReader ar, PropertyReadContext ctx)
    {
        if (!ar.TrySkip(8))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return null;
        }

        return new TextData { HistoryType = ETextHistoryType.TextGenerator };
    }

    /// <summary>
    /// Unsupported history types - warn and return null.
    /// </summary>
    private static TextData? ReadUnsupportedHistoryType(ArchiveReader ar, PropertyReadContext ctx, ETextHistoryType historyType)
    {
        ctx.Warn(DiagnosticCode.UnsupportedTextHistoryType, ar.Position, historyType.ToString());
        return null;
    }
}
