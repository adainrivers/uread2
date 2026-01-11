using URead2.IO;

namespace URead2.Deserialization.Properties;

/// <summary>
/// Boolean property value.
/// </summary>
public sealed class BoolProperty : PropertyValue<bool>
{
    public BoolProperty(bool value) => Value = value;

    public BoolProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? false : ar.ReadBool();
    }
}

/// <summary>
/// 8-bit signed integer property value.
/// </summary>
public sealed class Int8Property : PropertyValue<sbyte>
{
    public Int8Property(sbyte value) => Value = value;

    public Int8Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (sbyte)0 : (sbyte)ar.ReadByte();
    }
}

/// <summary>
/// 8-bit unsigned integer (byte) property value.
/// </summary>
public sealed class ByteProperty : PropertyValue<byte>
{
    public ByteProperty(byte value) => Value = value;

    public ByteProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (byte)0 : ar.ReadByte();
    }
}

/// <summary>
/// 16-bit signed integer property value.
/// </summary>
public sealed class Int16Property : PropertyValue<short>
{
    public Int16Property(short value) => Value = value;

    public Int16Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (short)0 : ar.ReadInt16();
    }
}

/// <summary>
/// 16-bit unsigned integer property value.
/// </summary>
public sealed class UInt16Property : PropertyValue<ushort>
{
    public UInt16Property(ushort value) => Value = value;

    public UInt16Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? (ushort)0 : ar.ReadUInt16();
    }
}

/// <summary>
/// 32-bit signed integer property value.
/// </summary>
public sealed class IntProperty : PropertyValue<int>
{
    public IntProperty(int value) => Value = value;

    public IntProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0 : ar.ReadInt32();
    }
}

/// <summary>
/// 32-bit unsigned integer property value.
/// </summary>
public sealed class UInt32Property : PropertyValue<uint>
{
    public UInt32Property(uint value) => Value = value;

    public UInt32Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0u : ar.ReadUInt32();
    }
}

/// <summary>
/// 64-bit signed integer property value.
/// </summary>
public sealed class Int64Property : PropertyValue<long>
{
    public Int64Property(long value) => Value = value;

    public Int64Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0L : ar.ReadInt64();
    }
}

/// <summary>
/// 64-bit unsigned integer property value.
/// </summary>
public sealed class UInt64Property : PropertyValue<ulong>
{
    public UInt64Property(ulong value) => Value = value;

    public UInt64Property(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0UL : ar.ReadUInt64();
    }
}

/// <summary>
/// 32-bit floating point property value.
/// </summary>
public sealed class FloatProperty : PropertyValue<float>
{
    public FloatProperty(float value) => Value = value;

    public FloatProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0f : ar.ReadFloat();
    }
}

/// <summary>
/// 64-bit floating point property value.
/// </summary>
public sealed class DoubleProperty : PropertyValue<double>
{
    public DoubleProperty(double value) => Value = value;

    public DoubleProperty(ArchiveReader ar, ReadContext context)
    {
        Value = context == ReadContext.Zero ? 0d : ar.ReadDouble();
    }
}
