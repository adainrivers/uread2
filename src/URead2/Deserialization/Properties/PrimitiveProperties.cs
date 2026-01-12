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

    public BoolProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? false : ar.ReadBool();
    }

    public static BoolProperty Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        return ar.ReadBool() ? True : Zero;
    }
}

/// <summary>
/// 8-bit signed integer property value.
/// </summary>
public sealed class Int8Property : PropertyValue<sbyte>
{
    public static readonly Int8Property Zero = new(0);

    public Int8Property(sbyte value) => Value = value;

    public Int8Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (sbyte)0 : (sbyte)ar.ReadByte();
    }

    public static Int8Property Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = (sbyte)ar.ReadByte();
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

    public ByteProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (byte)0 : ar.ReadByte();
    }

    public static ByteProperty Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadByte();
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

    public Int16Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (short)0 : ar.ReadInt16();
    }

    public static Int16Property Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadInt16();
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

    public UInt16Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (ushort)0 : ar.ReadUInt16();
    }

    public static UInt16Property Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadUInt16();
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

    public IntProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0 : ar.ReadInt32();
    }

    public static IntProperty Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadInt32();
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

    public UInt32Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0u : ar.ReadUInt32();
    }

    public static UInt32Property Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadUInt32();
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

    public Int64Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0L : ar.ReadInt64();
    }

    public static Int64Property Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadInt64();
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

    public UInt64Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0UL : ar.ReadUInt64();
    }

    public static UInt64Property Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadUInt64();
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

    public FloatProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0f : ar.ReadFloat();
    }

    public static FloatProperty Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadFloat();
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

    public DoubleProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0d : ar.ReadDouble();
    }

    public static DoubleProperty Create(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero) return Zero;
        var value = ar.ReadDouble();
        return value == 0d ? Zero : new DoubleProperty(value);
    }
}
