using URead2.Deserialization.Abstractions;
using URead2.IO;

namespace URead2.Deserialization.Properties;

/// <summary>
/// Boolean property value.
/// </summary>
public sealed class BoolProperty : PropertyValue<bool>
{
    public static readonly BoolProperty Zero = new(false);
    public static readonly BoolProperty True = new(true);

    public BoolProperty(bool value) => Value = value;

    public static BoolProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadBool(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value ? True : Zero;
    }
}

/// <summary>
/// 8-bit signed integer property value.
/// </summary>
public sealed class Int8Property : PropertyValue<sbyte>
{
    public static readonly Int8Property Zero = new(0);

    public Int8Property(sbyte value) => Value = value;

    public static Int8Property Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadByte(out var b))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        var value = (sbyte)b;
        return value == 0 ? Zero : new Int8Property(value);
    }
}

/// <summary>
/// 8-bit unsigned integer (byte) property value.
/// </summary>
public sealed class ByteProperty : PropertyValue<byte>
{
    public static readonly ByteProperty Zero = new(0);

    public ByteProperty(byte value) => Value = value;

    public static ByteProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadByte(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new ByteProperty(value);
    }
}

/// <summary>
/// 16-bit signed integer property value.
/// </summary>
public sealed class Int16Property : PropertyValue<short>
{
    public static readonly Int16Property Zero = new(0);

    public Int16Property(short value) => Value = value;

    public static Int16Property Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadInt16(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new Int16Property(value);
    }
}

/// <summary>
/// 16-bit unsigned integer property value.
/// </summary>
public sealed class UInt16Property : PropertyValue<ushort>
{
    public static readonly UInt16Property Zero = new(0);

    public UInt16Property(ushort value) => Value = value;

    public static UInt16Property Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadUInt16(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new UInt16Property(value);
    }
}

/// <summary>
/// 32-bit signed integer property value.
/// </summary>
public sealed class IntProperty : PropertyValue<int>
{
    public static readonly IntProperty Zero = new(0);

    public IntProperty(int value) => Value = value;

    public static IntProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadInt32(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new IntProperty(value);
    }
}

/// <summary>
/// 32-bit unsigned integer property value.
/// </summary>
public sealed class UInt32Property : PropertyValue<uint>
{
    public static readonly UInt32Property Zero = new(0);

    public UInt32Property(uint value) => Value = value;

    public static UInt32Property Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadUInt32(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new UInt32Property(value);
    }
}

/// <summary>
/// 64-bit signed integer property value.
/// </summary>
public sealed class Int64Property : PropertyValue<long>
{
    public static readonly Int64Property Zero = new(0);

    public Int64Property(long value) => Value = value;

    public static Int64Property Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadInt64(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new Int64Property(value);
    }
}

/// <summary>
/// 64-bit unsigned integer property value.
/// </summary>
public sealed class UInt64Property : PropertyValue<ulong>
{
    public static readonly UInt64Property Zero = new(0);

    public UInt64Property(ulong value) => Value = value;

    public static UInt64Property Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadUInt64(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0 ? Zero : new UInt64Property(value);
    }
}

/// <summary>
/// 32-bit floating point property value.
/// </summary>
public sealed class FloatProperty : PropertyValue<float>
{
    public static readonly FloatProperty Zero = new(0f);

    public FloatProperty(float value) => Value = value;

    public static FloatProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadFloat(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0f ? Zero : new FloatProperty(value);
    }
}

/// <summary>
/// 64-bit floating point property value.
/// </summary>
public sealed class DoubleProperty : PropertyValue<double>
{
    public static readonly DoubleProperty Zero = new(0d);

    public DoubleProperty(double value) => Value = value;

    public static DoubleProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero) return Zero;
        if (!ar.TryReadDouble(out var value))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return Zero;
        }
        return value == 0d ? Zero : new DoubleProperty(value);
    }
}
