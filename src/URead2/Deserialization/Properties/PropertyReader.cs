using URead2.Deserialization.Abstractions;
using URead2.IO;
using URead2.TypeResolution;

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
        var typeDef = context.TypeRegistry.GetType(className);
        var bag = new PropertyBag
        {
            TypeName = className,
            TypeDef = typeDef
        };

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
        while (!context.HasFatalError)
        {
            var name = ReadFName(ar, context);
            if (name == "None" || context.HasFatalError)
                break;

            var typeName = ReadFName(ar, context);
            if (context.HasFatalError) break;

            if (!ar.TryReadInt32(out var size) || !ar.TryReadInt32(out var arrayIndex))
            {
                context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                break;
            }

            var tagData = ReadTagData(ar, context, typeName);
            if (context.HasFatalError) break;

            var startPos = ar.Position;
            var value = ReadPropertyValue(ar, context, typeName, tagData, size, ReadContext.Normal);

            var consumed = ar.Position - startPos;
            if (consumed != size)
            {
                context.Warn(DiagnosticCode.SizeMismatch, startPos, $"property={name}, expected={size}, consumed={consumed}");
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
        var typeDef = context.TypeRegistry.GetType(className);
        if (typeDef == null)
        {
            context.Warn(DiagnosticCode.MissingTypeDef, ar.Position, className);
            return;
        }

        var header = ReadUnversionedHeader(ar, context);
        if (header == null || !header.HasValues)
            return;

        var allProperties = context.TypeRegistry.GetFlattenedProperties(className);
        if (allProperties == null)
        {
            context.Warn(DiagnosticCode.MissingFlattenedProperties, ar.Position, className);
            return;
        }

        int schemaIndex = 0;
        int zeroMaskIndex = 0;

        foreach (var fragment in header.Fragments)
        {
            if (context.HasFatalError) break;

            schemaIndex += fragment.SkipNum;

            for (int i = 0; i < fragment.ValueNum; i++)
            {
                if (context.HasFatalError) break;

                bool isZero = false;
                if (fragment.HasAnyZeroes)
                {
                    int byteIndex = zeroMaskIndex / 8;
                    int bitIndex = zeroMaskIndex % 8;
                    isZero = byteIndex < header.ZeroMask.Length && (header.ZeroMask[byteIndex] & (1 << bitIndex)) != 0;
                    zeroMaskIndex++;
                }

                var prop = schemaIndex < allProperties.Length ? allProperties[schemaIndex] : null;
                if (prop != null)
                {
                    var readContext = isZero ? ReadContext.Zero : ReadContext.Normal;
                    var value = ReadPropertyByType(ar, context, prop.Type, readContext);
                    bag.Add(prop.Name, value);
                }
                else if (schemaIndex >= allProperties.Length)
                {
                    context.Warn(DiagnosticCode.SchemaIndexOutOfRange, ar.Position, $"index={schemaIndex}, max={allProperties.Length}");
                }

                schemaIndex++;
            }
        }
    }

    /// <summary>
    /// Reads tag-specific data (inner type, struct type, enum name, etc.)
    /// </summary>
    protected virtual PropertyTagData ReadTagData(ArchiveReader ar, PropertyReadContext context, string typeName)
    {
        return typeName switch
        {
            "StructProperty" => ReadStructTagData(ar, context),
            "BoolProperty" => ReadBoolTagData(ar, context),
            "ByteProperty" or "EnumProperty" => new PropertyTagData { EnumName = ReadFName(ar, context) },
            "ArrayProperty" or "SetProperty" => new PropertyTagData { InnerType = ReadFName(ar, context) },
            "MapProperty" => new PropertyTagData
            {
                InnerType = ReadFName(ar, context),
                ValueType = ReadFName(ar, context)
            },
            _ => PropertyTagData.Empty
        };
    }

    private static PropertyTagData ReadStructTagData(ArchiveReader ar, PropertyReadContext context)
    {
        var structType = ReadFName(ar, context);
        // Skip struct GUID (16 bytes)
        if (!ar.TrySkip(16))
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
        return new PropertyTagData { StructType = structType };
    }

    private static PropertyTagData ReadBoolTagData(ArchiveReader ar, PropertyReadContext context)
    {
        if (!ar.TryReadByte(out var b))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new PropertyTagData { BoolValue = false };
        }
        return new PropertyTagData { BoolValue = b != 0 };
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
            "Int8Property" => Int8Property.Create(ar, context, readContext),
            "ByteProperty" when !string.IsNullOrEmpty(tagData.EnumName) && tagData.EnumName != "None"
                => ReadEnumValue(ar, context, tagData.EnumName, readContext),
            "ByteProperty" => ByteProperty.Create(ar, context, readContext),
            "Int16Property" => Int16Property.Create(ar, context, readContext),
            "UInt16Property" => UInt16Property.Create(ar, context, readContext),
            "IntProperty" => IntProperty.Create(ar, context, readContext),
            "UInt32Property" => UInt32Property.Create(ar, context, readContext),
            "Int64Property" => Int64Property.Create(ar, context, readContext),
            "UInt64Property" => UInt64Property.Create(ar, context, readContext),
            "FloatProperty" => FloatProperty.Create(ar, context, readContext),
            "DoubleProperty" => DoubleProperty.Create(ar, context, readContext),
            "StrProperty" => StrProperty.Create(ar, context, readContext),
            "NameProperty" => NameProperty.Create(ar, context, readContext),
            "TextProperty" => TextProperty.Create(ar, context, readContext),
            "ObjectProperty" => ObjectProperty.Create(ar, context, readContext),
            "SoftObjectProperty" => SoftObjectProperty.CreateTagged(ar, context, readContext),
            "InterfaceProperty" => InterfaceProperty.Create(ar, context, readContext),
            "DelegateProperty" => DelegateProperty.Create(ar, context, readContext),
            "MulticastDelegateProperty" => MulticastDelegateProperty.Create(ar, context, readContext),
            "EnumProperty" => ReadEnumValue(ar, context, tagData.EnumName, readContext),
            "ArrayProperty" => ReadArrayProperty(ar, context, tagData.InnerType, size, readContext),
            "SetProperty" => ReadSetProperty(ar, context, tagData.InnerType, size, readContext),
            "MapProperty" => ReadMapProperty(ar, context, tagData.InnerType, tagData.ValueType, size, readContext),
            "StructProperty" => ReadStructProperty(ar, context, tagData.StructType, size, readContext),
            _ => ReadUnknownProperty(ar, context, typeName, size)
        };
    }

    /// <summary>
    /// Reads a property value by property type (unversioned format).
    /// </summary>
    public virtual PropertyValue ReadPropertyByType(
        ArchiveReader ar,
        PropertyReadContext context,
        PropertyType propType,
        ReadContext readContext)
    {
        return propType.Kind switch
        {
            PropertyKind.BoolProperty => BoolProperty.Create(ar, context, readContext),
            PropertyKind.Int8Property => Int8Property.Create(ar, context, readContext),
            PropertyKind.ByteProperty => ByteProperty.Create(ar, context, readContext),
            PropertyKind.Int16Property => Int16Property.Create(ar, context, readContext),
            PropertyKind.UInt16Property => UInt16Property.Create(ar, context, readContext),
            PropertyKind.IntProperty => IntProperty.Create(ar, context, readContext),
            PropertyKind.UInt32Property => UInt32Property.Create(ar, context, readContext),
            PropertyKind.Int64Property => Int64Property.Create(ar, context, readContext),
            PropertyKind.UInt64Property => UInt64Property.Create(ar, context, readContext),
            PropertyKind.FloatProperty => FloatProperty.Create(ar, context, readContext),
            PropertyKind.DoubleProperty => DoubleProperty.Create(ar, context, readContext),
            PropertyKind.StrProperty => StrProperty.Create(ar, context, readContext),
            PropertyKind.NameProperty => NameProperty.Create(ar, context, readContext),
            PropertyKind.TextProperty => TextProperty.Create(ar, context, readContext),
            PropertyKind.ObjectProperty => ObjectProperty.Create(ar, context, readContext),
            PropertyKind.SoftObjectProperty => SoftObjectProperty.Create(ar, context, readContext),
            PropertyKind.InterfaceProperty => InterfaceProperty.Create(ar, context, readContext),
            PropertyKind.DelegateProperty => DelegateProperty.Create(ar, context, readContext),
            PropertyKind.MulticastDelegateProperty => MulticastDelegateProperty.Create(ar, context, readContext),
            PropertyKind.EnumProperty => ReadEnumByType(ar, context, propType, readContext),
            PropertyKind.ArrayProperty => ReadArrayByType(ar, context, propType, readContext),
            PropertyKind.SetProperty => ReadSetByType(ar, context, propType, readContext),
            PropertyKind.MapProperty => ReadMapByType(ar, context, propType, readContext),
            PropertyKind.StructProperty => ReadStructByType(ar, context, propType, readContext),
            _ => ReadUnknownPropertyKind(ar, context, propType.Kind)
        };
    }

    protected virtual PropertyValue ReadUnknownPropertyKind(ArchiveReader ar, PropertyReadContext context, PropertyKind kind)
    {
        context.Warn(DiagnosticCode.UnknownPropertyKind, ar.Position, kind.ToString());
        return ByteProperty.Zero;
    }

    #region Enum Reading

    protected virtual EnumProperty ReadEnumValue(ArchiveReader ar, PropertyReadContext context, string? enumName, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new EnumProperty(null, enumName);

        var value = ReadFName(ar, context);
        return new EnumProperty(value, enumName);
    }

    protected virtual EnumProperty ReadEnumByType(ArchiveReader ar, PropertyReadContext context, PropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new EnumProperty(null, propType.EnumName);

        if (propType.InnerType != null)
        {
            var innerValue = ReadPropertyByType(ar, context, propType.InnerType, readContext);
            var numericValue = innerValue.GenericValue;

            var enumDef = context.TypeRegistry.GetEnum(propType.EnumName ?? "");
            if (enumDef != null && numericValue is IConvertible conv)
            {
                var longValue = conv.ToInt64(null);
                var valueName = EnumResolver.GetName(enumDef, longValue);
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

        if (!ar.TryReadInt32(out var count))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new ArrayProperty([], innerType);
        }

        if (count < 0 || count > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"array count={count}");
            return new ArrayProperty([], innerType);
        }

        var elements = new PropertyValue[count];
        for (int i = 0; i < count && !context.HasFatalError; i++)
        {
            elements[i] = ReadPropertyValue(ar, context, innerType ?? "ByteProperty", PropertyTagData.Empty, 0, ReadContext.Array);
        }

        return new ArrayProperty(elements, innerType);
    }

    protected virtual ArrayProperty ReadArrayByType(ArchiveReader ar, PropertyReadContext context, PropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new ArrayProperty([], propType.InnerType?.Kind.ToString());

        if (!ar.TryReadInt32(out var count))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new ArrayProperty([], propType.InnerType?.Kind.ToString());
        }

        if (count < 0 || count > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"array count={count}");
            return new ArrayProperty([], propType.InnerType?.Kind.ToString());
        }

        var elements = new PropertyValue[count];
        if (propType.InnerType != null)
        {
            for (int i = 0; i < count && !context.HasFatalError; i++)
            {
                elements[i] = ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Array);
            }
        }

        return new ArrayProperty(elements, propType.InnerType?.Kind.ToString());
    }

    #endregion

    #region Set Reading

    protected virtual SetProperty ReadSetProperty(ArchiveReader ar, PropertyReadContext context, string? elementType, int size, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new SetProperty([], elementType);

        if (!ar.TryReadInt32(out var numToRemove))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SetProperty([], elementType);
        }

        if (numToRemove < 0 || numToRemove > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"set numToRemove={numToRemove}");
            return new SetProperty([], elementType);
        }

        for (int i = 0; i < numToRemove && !context.HasFatalError; i++)
        {
            ReadPropertyValue(ar, context, elementType ?? "ByteProperty", PropertyTagData.Empty, 0, ReadContext.Map);
        }

        if (!ar.TryReadInt32(out var count))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SetProperty([], elementType);
        }

        if (count < 0 || count > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"set count={count}");
            return new SetProperty([], elementType);
        }

        var elements = new PropertyValue[count];
        for (int i = 0; i < count && !context.HasFatalError; i++)
        {
            elements[i] = ReadPropertyValue(ar, context, elementType ?? "ByteProperty", PropertyTagData.Empty, 0, ReadContext.Map);
        }

        return new SetProperty(elements, elementType);
    }

    protected virtual SetProperty ReadSetByType(ArchiveReader ar, PropertyReadContext context, PropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new SetProperty([], propType.InnerType?.Kind.ToString());

        if (!ar.TryReadInt32(out var numToRemove))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SetProperty([], propType.InnerType?.Kind.ToString());
        }

        if (numToRemove < 0 || numToRemove > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"set numToRemove={numToRemove}");
            return new SetProperty([], propType.InnerType?.Kind.ToString());
        }

        if (propType.InnerType != null)
        {
            for (int i = 0; i < numToRemove && !context.HasFatalError; i++)
            {
                ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            }
        }

        if (!ar.TryReadInt32(out var count))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SetProperty([], propType.InnerType?.Kind.ToString());
        }

        if (count < 0 || count > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"set count={count}");
            return new SetProperty([], propType.InnerType?.Kind.ToString());
        }

        var elements = new PropertyValue[count];
        if (propType.InnerType != null)
        {
            for (int i = 0; i < count && !context.HasFatalError; i++)
            {
                elements[i] = ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            }
        }

        return new SetProperty(elements, propType.InnerType?.Kind.ToString());
    }

    #endregion

    #region Map Reading

    protected virtual MapProperty ReadMapProperty(ArchiveReader ar, PropertyReadContext context, string? keyType, string? valueType, int size, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new MapProperty([], keyType, valueType);

        if (!ar.TryReadInt32(out var numToRemove))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new MapProperty([], keyType, valueType);
        }

        if (numToRemove < 0 || numToRemove > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"map numToRemove={numToRemove}");
            return new MapProperty([], keyType, valueType);
        }

        for (int i = 0; i < numToRemove && !context.HasFatalError; i++)
        {
            ReadPropertyValue(ar, context, keyType ?? "ByteProperty", PropertyTagData.Empty, 0, ReadContext.Map);
        }

        if (!ar.TryReadInt32(out var count))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new MapProperty([], keyType, valueType);
        }

        if (count < 0 || count > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"map count={count}");
            return new MapProperty([], keyType, valueType);
        }

        var entries = new MapEntry[count];
        for (int i = 0; i < count && !context.HasFatalError; i++)
        {
            var key = ReadPropertyValue(ar, context, keyType ?? "ByteProperty", PropertyTagData.Empty, 0, ReadContext.Map);
            var value = ReadPropertyValue(ar, context, valueType ?? "ByteProperty", PropertyTagData.Empty, 0, ReadContext.Map);
            entries[i] = new MapEntry(key, value);
        }

        return new MapProperty(entries, keyType, valueType);
    }

    protected virtual MapProperty ReadMapByType(ArchiveReader ar, PropertyReadContext context, PropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new MapProperty([], propType.InnerType?.Kind.ToString(), propType.ValueType?.Kind.ToString());

        if (!ar.TryReadInt32(out var numToRemove))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new MapProperty([], propType.InnerType?.Kind.ToString(), propType.ValueType?.Kind.ToString());
        }

        if (numToRemove < 0 || numToRemove > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"map numToRemove={numToRemove}");
            return new MapProperty([], propType.InnerType?.Kind.ToString(), propType.ValueType?.Kind.ToString());
        }

        if (propType.InnerType != null)
        {
            for (int i = 0; i < numToRemove && !context.HasFatalError; i++)
            {
                ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            }
        }

        if (!ar.TryReadInt32(out var count))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new MapProperty([], propType.InnerType?.Kind.ToString(), propType.ValueType?.Kind.ToString());
        }

        if (count < 0 || count > 1000000)
        {
            context.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"map count={count}");
            return new MapProperty([], propType.InnerType?.Kind.ToString(), propType.ValueType?.Kind.ToString());
        }

        if (propType.InnerType == null || propType.ValueType == null)
        {
            context.Warn(DiagnosticCode.MissingMapKeyOrValueType, ar.Position,
                $"key={propType.InnerType?.Kind.ToString() ?? "null"}, value={propType.ValueType?.Kind.ToString() ?? "null"}");
            return new MapProperty([], propType.InnerType?.Kind.ToString(), propType.ValueType?.Kind.ToString());
        }

        var entries = new MapEntry[count];
        for (int i = 0; i < count && !context.HasFatalError; i++)
        {
            var key = ReadPropertyByType(ar, context, propType.InnerType, ReadContext.Map);
            var value = ReadPropertyByType(ar, context, propType.ValueType, ReadContext.Map);
            entries[i] = new MapEntry(key, value);
        }

        return new MapProperty(entries, propType.InnerType.Kind.ToString(), propType.ValueType.Kind.ToString());
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

    protected virtual StructProperty ReadStructByType(ArchiveReader ar, PropertyReadContext context, PropertyType propType, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
            return new StructProperty(new PropertyBag(), propType.StructName);

        var structType = propType.StructName;

        var allProperties = context.TypeRegistry.GetFlattenedProperties(structType ?? "");
        if (allProperties == null)
        {
            context.Fatal(ReadErrorCode.MissingStructSchema, ar.Position, structType);
            return new StructProperty(new PropertyBag(), structType);
        }

        if (CompactStructTypes.Contains(structType ?? ""))
        {
            var bag = new PropertyBag(allProperties.Length);
            for (int i = 0; i < allProperties.Length && !context.HasFatalError; i++)
            {
                var prop = allProperties[i];
                if (prop != null)
                {
                    var value = ReadPropertyByType(ar, context, prop.Type, ReadContext.Normal);
                    bag.Add(prop.Name, value);
                }
            }
            return new StructProperty(bag, structType);
        }
        else
        {
            var header = ReadUnversionedHeader(ar, context);
            if (header == null || !header.HasValues)
                return new StructProperty(new PropertyBag(), structType);

            var bag = new PropertyBag();
            int schemaIndex = 0;
            int zeroMaskIndex = 0;

            foreach (var fragment in header.Fragments)
            {
                if (context.HasFatalError) break;

                schemaIndex += fragment.SkipNum;

                for (int i = 0; i < fragment.ValueNum && !context.HasFatalError; i++)
                {
                    bool isZero = false;
                    if (fragment.HasAnyZeroes)
                    {
                        int byteIndex = zeroMaskIndex / 8;
                        int bitIndex = zeroMaskIndex % 8;
                        isZero = byteIndex < header.ZeroMask.Length && (header.ZeroMask[byteIndex] & (1 << bitIndex)) != 0;
                        zeroMaskIndex++;
                    }

                    var prop = schemaIndex < allProperties.Length ? allProperties[schemaIndex] : null;
                    if (prop != null)
                    {
                        var propReadContext = isZero ? ReadContext.Zero : ReadContext.Normal;
                        var value = ReadPropertyByType(ar, context, prop.Type, propReadContext);
                        bag.Add(prop.Name, value);
                    }

                    schemaIndex++;
                }
            }
            return new StructProperty(bag, structType);
        }
    }

    #endregion

    #region Helpers

    private static readonly HashSet<string> CompactStructTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Vector", "Vector2D", "Vector4", "Vector2f", "Vector3f", "Vector4f",
        "IntVector", "IntVector2", "IntVector4",
        "IntPoint", "Int32Point", "Int64Point", "UintPoint", "Uint32Point", "Uint64Point",
        "Rotator", "Rotator3d", "Rotator3f",
        "Quat", "Quat4d", "Quat4f",
        "Matrix", "Matrix44d", "Matrix44f",
        "Transform", "Transform3d", "Transform3f",
        "Plane", "Plane4d", "Plane4f",
        "Box", "Box2D", "Box2f", "Box3d", "Box3f",
        "BoxSphereBounds", "OrientedBox",
        "Ray", "Ray3d", "Ray3f",
        "Sphere", "Sphere3d", "Sphere3f",
        "Color", "LinearColor",
        "Guid", "DateTime", "Timespan", "FrameNumber",
        "SoftObjectPath", "SoftClassPath", "TopLevelAssetPath",
        "PrimaryAssetType", "PrimaryAssetId",
        "GameplayTag", "GameplayTagContainer",
        "NavAgentSelector", "PointerToUberGraphFrame",
        "PerPlatformInt", "PerPlatformFloat", "PerPlatformBool",
        "PerQualityLevelInt", "PerQualityLevelFloat",
        "FontCharacter",
    };

    protected virtual PropertyValue ReadUnknownProperty(ArchiveReader ar, PropertyReadContext context, string typeName, int size)
    {
        context.Warn(DiagnosticCode.UnknownTaggedPropertyType, ar.Position, typeName);
        ar.TrySkip(size);
        return new ByteProperty(0);
    }

    protected static string ReadFName(ArchiveReader ar, PropertyReadContext context)
    {
        if (!ar.TryReadInt32(out int index) || !ar.TryReadInt32(out int number))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return "None";
        }

        var nameTable = context.NameTable;
        if (index < 0 || index >= nameTable.Length)
        {
            context.Warn(DiagnosticCode.InvalidFNameIndex, ar.Position - 8, $"index={index}, max={nameTable.Length}");
            return "None";
        }

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }

    #endregion

    #region Unversioned Header

    protected static UnversionedHeader? ReadUnversionedHeader(ArchiveReader ar, PropertyReadContext context)
    {
        var fragments = new List<UnversionedFragment>();
        int zeroMaskNum = 0;
        int unmaskedNum = 0;
        const int maxFragments = 50;

        while (true)
        {
            if (!ar.TryReadUInt16(out var packed))
            {
                context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return null;
            }

            var fragment = new UnversionedFragment(packed);
            fragments.Add(fragment);

            if (fragment.HasAnyZeroes)
                zeroMaskNum += fragment.ValueNum;
            else
                unmaskedNum += fragment.ValueNum;

            if (fragment.IsLast)
                break;

            if (fragments.Count >= maxFragments)
            {
                context.Fatal(ReadErrorCode.TooManyFragments, ar.Position, $"fragments={fragments.Count}+, likely delta-serialized");
                return null;
            }
        }

        byte[] zeroMask = [];
        if (zeroMaskNum > 0)
        {
            zeroMask = ReadZeroMask(ar, context, zeroMaskNum);
            if (context.HasFatalError)
                return null;
        }

        bool hasNonZeroValues = unmaskedNum > 0 || HasAnyZeroBit(zeroMask, zeroMaskNum);
        return new UnversionedHeader(fragments, zeroMask, hasNonZeroValues);
    }

    private static bool HasAnyZeroBit(byte[] mask, int numBits)
    {
        for (int i = 0; i < numBits; i++)
        {
            int byteIndex = i / 8;
            int bitIndex = i % 8;
            if ((mask[byteIndex] & (1 << bitIndex)) == 0)
                return true;
        }
        return false;
    }

    protected static byte[] ReadZeroMask(ArchiveReader ar, PropertyReadContext context, int numBits)
    {
        int byteCount = numBits <= 8 ? 1 : numBits <= 16 ? 2 : (numBits + 31) / 32 * 4;

        if (!ar.TryReadBytes(byteCount, out var bytes))
        {
            context.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return [];
        }

        return bytes;
    }

    protected record UnversionedHeader(List<UnversionedFragment> Fragments, byte[] ZeroMask, bool HasNonZeroValues)
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

internal static class PropertyTagDataExtensions
{
    public static PropertyTagData Also(this PropertyTagData data, Action<PropertyTagData> action)
    {
        action(data);
        return data;
    }
}
