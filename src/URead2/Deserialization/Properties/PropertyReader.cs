using URead2.Deserialization.Abstractions;
using URead2.Deserialization.TypeMappings;
using URead2.IO;

namespace URead2.Deserialization.Properties;

/// <summary>
/// Default implementation for reading properties from serialized UObject data.
/// </summary>
public class PropertyReader : IPropertyReader
{
    /// <summary>
    /// Reads all properties from an export's serialized data.
    /// </summary>
    public PropertyBag ReadProperties(
        ArchiveReader ar,
        PropertyReadContext context,
        string className,
        bool isUnversioned = false)
    {
        var bag = new PropertyBag();

        if (isUnversioned)
        {
            ReadUnversionedProperties(ar, context, className, bag);
        }
        else
        {
            ReadTaggedProperties(ar, context, bag);
        }

        return bag;
    }

    /// <summary>
    /// Reads tagged properties (versioned format with embedded property tags).
    /// </summary>
    protected virtual void ReadTaggedProperties(ArchiveReader ar, PropertyReadContext context, PropertyBag bag)
    {
        while (true)
        {
            var name = ReadFName(ar, context.NameTable);
            if (name == "None")
                break;

            var typeName = ReadFName(ar, context.NameTable);
            var size = ar.ReadInt32();
            var arrayIndex = ar.ReadInt32();
            var tagData = ReadTagData(ar, context.NameTable, typeName);

            var startPos = ar.Position;
            var value = ReadPropertyValue(ar, context, typeName, tagData, size, ReadContext.Normal);

            var consumed = ar.Position - startPos;
            if (consumed != size)
            {
                ar.Position = startPos + size;
            }

            var propName = arrayIndex > 0 ? $"{name}[{arrayIndex}]" : name;
            bag.Add(propName, value);
        }
    }

    /// <summary>
    /// Reads unversioned properties using schema from type resolver.
    /// </summary>
    protected virtual void ReadUnversionedProperties(
        ArchiveReader ar,
        PropertyReadContext context,
        string className,
        PropertyBag bag)
    {
        var schema = context.TypeResolver.GetSchema(className);
        if (schema == null)
            return;

        var header = ReadUnversionedHeader(ar);
        if (!header.HasValues)
            return;

        int schemaIndex = 0;
        int zeroMaskIndex = 0;

        foreach (var fragment in header.Fragments)
        {
            schemaIndex += fragment.SkipNum;

            for (int i = 0; i < fragment.ValueNum; i++)
            {
                bool isZero = false;
                if (fragment.HasAnyZeroes)
                {
                    isZero = zeroMaskIndex < header.ZeroMask.Length && header.ZeroMask[zeroMaskIndex];
                    zeroMaskIndex++;
                }

                var prop = GetPropertyAtIndex(context.TypeResolver, schema, schemaIndex);
                if (prop != null)
                {
                    var readContext = isZero ? ReadContext.Zero : ReadContext.Normal;
                    var value = ReadPropertyByType(ar, context, prop.PropertyType, readContext);
                    bag.Add(prop.Name, value);
                }

                schemaIndex++;
            }
        }
    }

    /// <summary>
    /// Reads tag-specific data (inner type, struct type, enum name, etc.)
    /// </summary>
    protected virtual PropertyTagData ReadTagData(ArchiveReader ar, string[] nameTable, string typeName)
    {
        return typeName switch
        {
            "StructProperty" => new PropertyTagData
            {
                StructType = ReadFName(ar, nameTable),
                // Skip struct GUID (16 bytes)
            }.Also(_ => ar.Skip(16)),

            "BoolProperty" => new PropertyTagData { BoolValue = ar.ReadByte() != 0 },

            "ByteProperty" or "EnumProperty" => new PropertyTagData { EnumName = ReadFName(ar, nameTable) },

            "ArrayProperty" or "SetProperty" => new PropertyTagData { InnerType = ReadFName(ar, nameTable) },

            "MapProperty" => new PropertyTagData
            {
                InnerType = ReadFName(ar, nameTable),
                ValueType = ReadFName(ar, nameTable)
            },

            _ => new PropertyTagData()
        };
    }

    /// <summary>
    /// Reads a property value based on type name and tag data (tagged/versioned format).
    /// </summary>
    public virtual PropertyValue ReadPropertyValue(
        ArchiveReader ar,
        PropertyReadContext context,
        string typeName,
        PropertyTagData tagData,
        int size,
        ReadContext readContext)
    {
        return typeName switch
        {
            "BoolProperty" => new BoolProperty(tagData.BoolValue),
            "Int8Property" => new Int8Property(ar, readContext),
            "ByteProperty" when !string.IsNullOrEmpty(tagData.EnumName) && tagData.EnumName != "None"
                => ReadEnumValue(ar, context, tagData.EnumName, readContext),
            "ByteProperty" => new ByteProperty(ar, readContext),
            "Int16Property" => new Int16Property(ar, readContext),
            "UInt16Property" => new UInt16Property(ar, readContext),
            "IntProperty" => new IntProperty(ar, readContext),
            "UInt32Property" => new UInt32Property(ar, readContext),
            "Int64Property" => new Int64Property(ar, readContext),
            "UInt64Property" => new UInt64Property(ar, readContext),
            "FloatProperty" => new FloatProperty(ar, readContext),
            "DoubleProperty" => new DoubleProperty(ar, readContext),
            "StrProperty" => new StrProperty(ar, readContext),
            "NameProperty" => new NameProperty(ar, context.NameTable, readContext),
            "TextProperty" => new TextProperty(ar, readContext),
            "ObjectProperty" => new ObjectProperty(ar, context, readContext),
            "SoftObjectProperty" => new SoftObjectProperty(ar, readContext),
            "InterfaceProperty" => new InterfaceProperty(ar, context, readContext),
            "DelegateProperty" => new DelegateProperty(ar, context, readContext),
            "MulticastDelegateProperty" => new MulticastDelegateProperty(ar, context, readContext),
            "EnumProperty" => ReadEnumValue(ar, context, tagData.EnumName, readContext),
            "ArrayProperty" => ReadArrayProperty(ar, context, tagData.InnerType, size, readContext),
            "SetProperty" => ReadSetProperty(ar, context, tagData.InnerType, size, readContext),
            "MapProperty" => ReadMapProperty(ar, context, tagData.InnerType, tagData.ValueType, size, readContext),
            "StructProperty" => ReadStructProperty(ar, context, tagData.StructType, size, readContext),
            _ => ReadUnknownProperty(ar, size)
        };
    }

    /// <summary>
    /// Reads a property value by property type (unversioned format).
    /// </summary>
    public virtual PropertyValue ReadPropertyByType(
        ArchiveReader ar,
        PropertyReadContext context,
        UsmapPropertyType propType,
        ReadContext readContext)
    {
        return propType.Type switch
        {
            EPropertyType.BoolProperty => new BoolProperty(ar, readContext),
            EPropertyType.Int8Property => new Int8Property(ar, readContext),
            EPropertyType.ByteProperty => new ByteProperty(ar, readContext),
            EPropertyType.Int16Property => new Int16Property(ar, readContext),
            EPropertyType.UInt16Property => new UInt16Property(ar, readContext),
            EPropertyType.IntProperty => new IntProperty(ar, readContext),
            EPropertyType.UInt32Property => new UInt32Property(ar, readContext),
            EPropertyType.Int64Property => new Int64Property(ar, readContext),
            EPropertyType.UInt64Property => new UInt64Property(ar, readContext),
            EPropertyType.FloatProperty => new FloatProperty(ar, readContext),
            EPropertyType.DoubleProperty => new DoubleProperty(ar, readContext),
            EPropertyType.StrProperty => new StrProperty(ar, readContext),
            EPropertyType.NameProperty => new NameProperty(ar, context.NameTable, readContext),
            EPropertyType.TextProperty => new TextProperty(ar, readContext),
            EPropertyType.ObjectProperty => new ObjectProperty(ar, context, readContext),
            EPropertyType.SoftObjectProperty => new SoftObjectProperty(ar, context.NameTable, readContext),
            EPropertyType.InterfaceProperty => new InterfaceProperty(ar, context, readContext),
            EPropertyType.DelegateProperty => new DelegateProperty(ar, context, readContext),
            EPropertyType.MulticastDelegateProperty => new MulticastDelegateProperty(ar, context, readContext),
            EPropertyType.EnumProperty => ReadEnumByType(ar, context, propType, readContext),
            EPropertyType.ArrayProperty => ReadArrayByType(ar, context, propType, readContext),
            EPropertyType.SetProperty => ReadSetByType(ar, context, propType, readContext),
            EPropertyType.MapProperty => ReadMapByType(ar, context, propType, readContext),
            EPropertyType.StructProperty => ReadStructByType(ar, context, propType, readContext),
            _ => new ByteProperty(0)
        };
    }

    #region Enum Reading

    protected virtual EnumProperty ReadEnumValue(ArchiveReader ar, PropertyReadContext context, string? enumName, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new EnumProperty(null, enumName);

        var value = ReadFName(ar, context.NameTable);
        return new EnumProperty(value, enumName);
    }

    protected virtual EnumProperty ReadEnumByType(ArchiveReader ar, PropertyReadContext context, UsmapPropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new EnumProperty(null, propType.EnumName);

        if (propType.InnerType != null)
        {
            var innerValue = ReadPropertyByType(ar, context, propType.InnerType, readContext);
            var numericValue = innerValue.GenericValue;

            var enumDef = context.TypeResolver.GetEnum(propType.EnumName ?? "");
            if (enumDef != null && numericValue is IConvertible conv)
            {
                var longValue = conv.ToInt64(null);
                var valueName = enumDef.GetValueName(longValue);
                return new EnumProperty(valueName, propType.EnumName);
            }
        }

        return new EnumProperty(null, propType.EnumName);
    }

    #endregion

    #region Array Reading

    protected virtual ArrayProperty ReadArrayProperty(ArchiveReader ar, PropertyReadContext context, string? innerType, int size, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new ArrayProperty([], innerType);

        var count = ar.ReadInt32();
        if (count < 0 || count > 1000000)
            return new ArrayProperty([], innerType);

        var elements = new PropertyValue[count];
        for (int i = 0; i < count; i++)
        {
            elements[i] = ReadPropertyValue(ar, context, innerType ?? "ByteProperty", new PropertyTagData(), 0, ReadContext.Array);
        }

        return new ArrayProperty(elements, innerType);
    }

    protected virtual ArrayProperty ReadArrayByType(ArchiveReader ar, PropertyReadContext context, UsmapPropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new ArrayProperty([], propType.InnerType?.Type.ToString());

        var count = ar.ReadInt32();
        if (count < 0 || count > 1000000)
            return new ArrayProperty([], propType.InnerType?.Type.ToString());

        var elements = new PropertyValue[count];
        if (propType.InnerType != null)
        {
            for (int i = 0; i < count; i++)
            {
                elements[i] = ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Array);
            }
        }

        return new ArrayProperty(elements, propType.InnerType?.Type.ToString());
    }

    #endregion

    #region Set Reading

    protected virtual SetProperty ReadSetProperty(ArchiveReader ar, PropertyReadContext context, string? elementType, int size, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new SetProperty([], elementType);

        var numToRemove = ar.ReadInt32();
        if (numToRemove < 0 || numToRemove > 1000000)
            return new SetProperty([], elementType);

        for (int i = 0; i < numToRemove; i++)
        {
            ReadPropertyValue(ar, context, elementType ?? "ByteProperty", new PropertyTagData(), 0, ReadContext.Map);
        }

        var count = ar.ReadInt32();
        if (count < 0 || count > 1000000)
            return new SetProperty([], elementType);

        var elements = new PropertyValue[count];
        for (int i = 0; i < count; i++)
        {
            elements[i] = ReadPropertyValue(ar, context, elementType ?? "ByteProperty", new PropertyTagData(), 0, ReadContext.Map);
        }

        return new SetProperty(elements, elementType);
    }

    protected virtual SetProperty ReadSetByType(ArchiveReader ar, PropertyReadContext context, UsmapPropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new SetProperty([], propType.InnerType?.Type.ToString());

        var numToRemove = ar.ReadInt32();
        if (numToRemove < 0 || numToRemove > 1000000)
            return new SetProperty([], propType.InnerType?.Type.ToString());

        if (propType.InnerType != null)
        {
            for (int i = 0; i < numToRemove; i++)
            {
                ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            }
        }

        var count = ar.ReadInt32();
        if (count < 0 || count > 1000000)
            return new SetProperty([], propType.InnerType?.Type.ToString());

        var elements = new PropertyValue[count];
        if (propType.InnerType != null)
        {
            for (int i = 0; i < count; i++)
            {
                elements[i] = ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            }
        }

        return new SetProperty(elements, propType.InnerType?.Type.ToString());
    }

    #endregion

    #region Map Reading

    protected virtual MapProperty ReadMapProperty(ArchiveReader ar, PropertyReadContext context, string? keyType, string? valueType, int size, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new MapProperty([], keyType, valueType);

        var numToRemove = ar.ReadInt32();
        if (numToRemove < 0 || numToRemove > 1000000)
            return new MapProperty([], keyType, valueType);

        for (int i = 0; i < numToRemove; i++)
        {
            ReadPropertyValue(ar, context, keyType ?? "ByteProperty", new PropertyTagData(), 0, ReadContext.Map);
        }

        var count = ar.ReadInt32();
        if (count < 0 || count > 1000000)
            return new MapProperty([], keyType, valueType);

        var entries = new MapEntry[count];
        for (int i = 0; i < count; i++)
        {
            var key = ReadPropertyValue(ar, context, keyType ?? "ByteProperty", new PropertyTagData(), 0, ReadContext.Map);
            var value = ReadPropertyValue(ar, context, valueType ?? "ByteProperty", new PropertyTagData(), 0, ReadContext.Map);
            entries[i] = new MapEntry(key, value);
        }

        return new MapProperty(entries, keyType, valueType);
    }

    protected virtual MapProperty ReadMapByType(ArchiveReader ar, PropertyReadContext context, UsmapPropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new MapProperty([], propType.InnerType?.Type.ToString(), propType.ValueType?.Type.ToString());

        var numToRemove = ar.ReadInt32();
        if (numToRemove < 0 || numToRemove > 1000000)
            return new MapProperty([], propType.InnerType?.Type.ToString(), propType.ValueType?.Type.ToString());

        if (propType.InnerType != null)
        {
            for (int i = 0; i < numToRemove; i++)
            {
                ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            }
        }

        var count = ar.ReadInt32();
        if (count < 0 || count > 1000000)
            return new MapProperty([], propType.InnerType?.Type.ToString(), propType.ValueType?.Type.ToString());

        var entries = new MapEntry[count];
        for (int i = 0; i < count; i++)
        {
            var key = propType.InnerType != null
                ? ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map)
                : new ByteProperty(0);
            var value = propType.ValueType != null
                ? ReadPropertyByType(ar, context, propType.ValueType, ReadContext.Map)
                : new ByteProperty(0);
            entries[i] = new MapEntry(key, value);
        }

        return new MapProperty(entries, propType.InnerType?.Type.ToString(), propType.ValueType?.Type.ToString());
    }

    #endregion

    #region Struct Reading

    protected virtual StructProperty ReadStructProperty(ArchiveReader ar, PropertyReadContext context, string? structType, int size, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new StructProperty(new PropertyBag(), structType);

        var bag = new PropertyBag();
        var startPos = ar.Position;
        ReadTaggedProperties(ar, context, bag);

        var consumed = ar.Position - startPos;
        if (consumed < size)
        {
            ar.Position = startPos + size;
        }

        return new StructProperty(bag, structType);
    }

    protected virtual StructProperty ReadStructByType(ArchiveReader ar, PropertyReadContext context, UsmapPropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new StructProperty(new PropertyBag(), propType.StructType);

        var structType = propType.StructType;
        var schema = context.TypeResolver.GetSchema(structType ?? "");

        if (schema != null)
        {
            var bag = new PropertyBag();
            for (int i = 0; i < schema.PropertyCount; i++)
            {
                var prop = GetPropertyAtIndex(context.TypeResolver, schema, i);
                if (prop != null)
                {
                    var value = ReadPropertyByType(ar, context, prop.PropertyType, ReadContext.Normal);
                    bag.Add(prop.Name, value);
                }
            }
            return new StructProperty(bag, structType);
        }

        return new StructProperty(new PropertyBag(), structType);
    }

    #endregion

    #region Helpers

    protected virtual PropertyValue ReadUnknownProperty(ArchiveReader ar, int size)
    {
        ar.Skip(size);
        return new ByteProperty(0);
    }

    protected static string ReadFName(ArchiveReader ar, string[] nameTable)
    {
        int index = ar.ReadInt32();
        int number = ar.ReadInt32();

        if (index < 0 || index >= nameTable.Length)
            return "None";

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }

    /// <summary>
    /// Gets property definition at schema index, walking inheritance chain.
    /// </summary>
    protected static UsmapProperty? GetPropertyAtIndex(ITypeResolver resolver, UsmapSchema schema, int index)
    {
        if (schema.Properties.TryGetValue(index, out var prop))
            return prop;

        if (schema.SuperType != null)
        {
            var parent = resolver.GetSchema(schema.SuperType);
            if (parent != null)
                return GetPropertyAtIndex(resolver, parent, index);
        }

        return null;
    }

    #endregion

    #region Unversioned Header

    protected static UnversionedHeader ReadUnversionedHeader(ArchiveReader ar)
    {
        var fragments = new List<UnversionedFragment>();
        int zeroMaskNum = 0;
        int unmaskedNum = 0;

        while (true)
        {
            var packed = ar.ReadUInt16();
            var fragment = new UnversionedFragment(packed);
            fragments.Add(fragment);

            if (fragment.HasAnyZeroes)
                zeroMaskNum += fragment.ValueNum;
            else
                unmaskedNum += fragment.ValueNum;

            if (fragment.IsLast)
                break;
        }

        bool[] zeroMask = [];
        if (zeroMaskNum > 0)
        {
            zeroMask = ReadZeroMask(ar, zeroMaskNum);
        }

        bool hasNonZeroValues = unmaskedNum > 0 || Array.Exists(zeroMask, z => !z);
        return new UnversionedHeader(fragments, zeroMask, hasNonZeroValues);
    }

    protected static bool[] ReadZeroMask(ArchiveReader ar, int numBits)
    {
        byte[] bytes;
        if (numBits <= 8)
            bytes = ar.ReadBytes(1);
        else if (numBits <= 16)
            bytes = ar.ReadBytes(2);
        else
            bytes = ar.ReadBytes((numBits + 31) / 32 * 4);

        var mask = new bool[numBits];
        for (int i = 0; i < numBits; i++)
        {
            int byteIndex = i / 8;
            int bitIndex = i % 8;
            if (byteIndex < bytes.Length)
                mask[i] = (bytes[byteIndex] & (1 << bitIndex)) != 0;
        }
        return mask;
    }

    protected record UnversionedHeader(List<UnversionedFragment> Fragments, bool[] ZeroMask, bool HasNonZeroValues)
    {
        public bool HasValues => HasNonZeroValues || ZeroMask.Length > 0;
    }

    protected readonly struct UnversionedFragment
    {
        private const uint SkipNumMask = 0x007F;
        private const uint HasZeroMask = 0x0080;
        private const int ValueNumShift = 9;
        private const uint IsLastMask = 0x0100;

        public readonly byte SkipNum;
        public readonly bool HasAnyZeroes;
        public readonly byte ValueNum;
        public readonly bool IsLast;

        public UnversionedFragment(ushort packed)
        {
            SkipNum = (byte)(packed & SkipNumMask);
            HasAnyZeroes = (packed & HasZeroMask) != 0;
            ValueNum = (byte)(packed >> ValueNumShift);
            IsLast = (packed & IsLastMask) != 0;
        }
    }

    #endregion
}

/// <summary>
/// Extension for fluent configuration.
/// </summary>
internal static class PropertyTagDataExtensions
{
    public static PropertyTagData Also(this PropertyTagData data, Action<PropertyTagData> action)
    {
        action(data);
        return data;
    }
}
