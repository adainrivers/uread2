using URead2.Assets.Models;
using URead2.IO;

namespace URead2.Deserialization.Fields;

/// <summary>
/// Property flags matching EPropertyFlags from UE.
/// </summary>
[Flags]
public enum EPropertyFlags : ulong
{
    None = 0,
    Edit = 0x0000000000000001,
    ConstParm = 0x0000000000000002,
    BlueprintVisible = 0x0000000000000004,
    ExportObject = 0x0000000000000008,
    BlueprintReadOnly = 0x0000000000000010,
    Net = 0x0000000000000020,
    EditFixedSize = 0x0000000000000040,
    Parm = 0x0000000000000080,
    OutParm = 0x0000000000000100,
    ZeroConstructor = 0x0000000000000200,
    ReturnParm = 0x0000000000000400,
    DisableEditOnTemplate = 0x0000000000000800,
    Transient = 0x0000000000002000,
    Config = 0x0000000000004000,
    DisableEditOnInstance = 0x0000000000010000,
    EditConst = 0x0000000000020000,
    GlobalConfig = 0x0000000000040000,
    InstancedReference = 0x0000000000080000,
    DuplicateTransient = 0x0000000000200000,
    SaveGame = 0x0000000001000000,
    NoClear = 0x0000000002000000,
    ReferenceParm = 0x0000000008000000,
    BlueprintAssignable = 0x0000000010000000,
    Deprecated = 0x0000000020000000,
    IsPlainOldData = 0x0000000040000000,
    RepSkip = 0x0000000080000000,
    RepNotify = 0x0000000100000000,
    Interp = 0x0000000200000000,
    NonTransactional = 0x0000000400000000,
    EditorOnly = 0x0000000800000000,
    NoDestructor = 0x0000001000000000,
    AutoWeak = 0x0000004000000000,
    ContainsInstancedReference = 0x0000008000000000,
    AssetRegistrySearchable = 0x0000010000000000,
    SimpleDisplay = 0x0000020000000000,
    AdvancedDisplay = 0x0000040000000000,
    Protected = 0x0000080000000000,
    BlueprintCallable = 0x0000100000000000,
    BlueprintAuthorityOnly = 0x0000200000000000,
    TextExportTransient = 0x0000400000000000,
    NonPIEDuplicateTransient = 0x0000800000000000,
    ExposeOnSpawn = 0x0001000000000000,
    PersistentInstance = 0x0002000000000000,
    UObjectWrapper = 0x0004000000000000,
    HasGetValueTypeHash = 0x0008000000000000,
    NativeAccessSpecifierPublic = 0x0010000000000000,
    NativeAccessSpecifierProtected = 0x0020000000000000,
    NativeAccessSpecifierPrivate = 0x0040000000000000,
    SkipSerialization = 0x0080000000000000,
}

/// <summary>
/// Context for resolving package indices during FField deserialization.
/// </summary>
public class FFieldContext
{
    public required string[] NameTable { get; init; }
    public required AssetImport[] Imports { get; init; }
    public required AssetExport[] Exports { get; init; }

    /// <summary>
    /// Resolves a package index to an object name.
    /// Positive = export, negative = import, zero = null.
    /// </summary>
    public string? ResolvePackageIndex(int index)
    {
        if (index > 0)
        {
            // Export (1-based)
            int exportIdx = index - 1;
            if (exportIdx >= 0 && exportIdx < Exports.Length)
                return Exports[exportIdx].Name;
        }
        else if (index < 0)
        {
            // Import (negative 1-based)
            int importIdx = -index - 1;
            if (importIdx >= 0 && importIdx < Imports.Length)
                return Imports[importIdx].Name;
        }
        return null;
    }
}

/// <summary>
/// Base class for property definitions read from UStruct exports.
/// </summary>
public class FProperty : FField
{
    /// <summary>
    /// The original type name from serialization (e.g., "FloatProperty", "IntProperty").
    /// Used to determine the exact numeric property type.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    public int ArrayDim { get; set; }
    public int ElementSize { get; set; }
    public EPropertyFlags PropertyFlags { get; set; }
    public ushort RepIndex { get; set; }
    public string RepNotifyFunc { get; set; } = string.Empty;
    public byte BlueprintReplicationCondition { get; set; }

    public virtual void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context.NameTable);
        ArrayDim = ar.ReadInt32();
        ElementSize = ar.ReadInt32();
        PropertyFlags = (EPropertyFlags)ar.ReadUInt64();
        RepIndex = ar.ReadUInt16();
        RepNotifyFunc = ReadFName(ar, context.NameTable);
        BlueprintReplicationCondition = ar.ReadByte();
    }

    /// <summary>
    /// Constructs the appropriate FProperty subclass based on type name.
    /// </summary>
    public static FProperty? Construct(string typeName) => typeName switch
    {
        "ArrayProperty" => new FArrayProperty(),
        "BoolProperty" => new FBoolProperty(),
        "ByteProperty" => new FByteProperty(),
        "ClassProperty" => new FClassProperty(),
        "DelegateProperty" => new FDelegateProperty(),
        "EnumProperty" => new FEnumProperty(),
        "FieldPathProperty" => new FFieldPathProperty(),
        "DoubleProperty" => new FNumericProperty(),
        "FloatProperty" => new FNumericProperty(),
        "Int16Property" => new FNumericProperty(),
        "Int64Property" => new FNumericProperty(),
        "Int8Property" => new FNumericProperty(),
        "IntProperty" => new FNumericProperty(),
        "InterfaceProperty" => new FInterfaceProperty(),
        "MapProperty" => new FMapProperty(),
        "MulticastDelegateProperty" => new FMulticastDelegateProperty(),
        "MulticastInlineDelegateProperty" => new FMulticastInlineDelegateProperty(),
        "MulticastSparseDelegateProperty" => new FMulticastDelegateProperty(),
        "NameProperty" => new FProperty(),
        "ObjectProperty" or "ObjectPtrProperty" => new FObjectProperty(),
        "SetProperty" => new FSetProperty(),
        "SoftClassProperty" => new FSoftClassProperty(),
        "SoftObjectProperty" => new FSoftObjectProperty(),
        "StrProperty" => new FProperty(),
        "StructProperty" => new FStructProperty(),
        "TextProperty" => new FProperty(),
        "UInt16Property" => new FNumericProperty(),
        "UInt32Property" => new FNumericProperty(),
        "UInt64Property" => new FNumericProperty(),
        "WeakObjectProperty" => new FWeakObjectProperty(),
        "LazyObjectProperty" => new FLazyObjectProperty(),
        "OptionalProperty" => new FOptionalProperty(),
        _ => null // Unknown property type
    };

    /// <summary>
    /// Reads a single FField from the archive.
    /// </summary>
    public static FProperty? ReadSingleField(ArchiveReader ar, FFieldContext context)
    {
        var typeName = ReadFName(ar, context.NameTable);
        if (typeName == "None" || string.IsNullOrEmpty(typeName))
            return null;

        var prop = Construct(typeName);
        if (prop != null)
        {
            prop.TypeName = typeName; // Store the original type name
            prop.Deserialize(ar, context);
        }
        return prop;
    }

    /// <summary>
    /// Reads an array of FProperty from the archive.
    /// </summary>
    public static FProperty[] ReadPropertyArray(ArchiveReader ar, FFieldContext context)
    {
        var count = ar.ReadInt32();
        if (count <= 0 || count > 65536)
            return [];

        var properties = new List<FProperty>(count);
        for (int i = 0; i < count; i++)
        {
            var prop = ReadSingleField(ar, context);
            if (prop != null)
                properties.Add(prop);
        }
        return properties.ToArray();
    }
}

// Numeric property - no extra fields
public class FNumericProperty : FProperty { }

// Array property
public class FArrayProperty : FProperty
{
    public FProperty? Inner { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        Inner = ReadSingleField(ar, context);
    }
}

// Bool property
public class FBoolProperty : FProperty
{
    public byte FieldSize { get; set; }
    public byte ByteOffset { get; set; }
    public byte ByteMask { get; set; }
    public byte FieldMask { get; set; }
    public byte BoolSize { get; set; }
    public bool IsNativeBool { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        FieldSize = ar.ReadByte();
        ByteOffset = ar.ReadByte();
        ByteMask = ar.ReadByte();
        FieldMask = ar.ReadByte();
        BoolSize = ar.ReadByte();
        IsNativeBool = ar.ReadByte() != 0;
    }
}

// Byte property (can have enum)
public class FByteProperty : FProperty
{
    public string? EnumName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var enumIndex = ar.ReadInt32();
        EnumName = context.ResolvePackageIndex(enumIndex);
    }
}

// Class property
public class FClassProperty : FObjectProperty
{
    public string? MetaClassName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var metaClassIndex = ar.ReadInt32();
        MetaClassName = context.ResolvePackageIndex(metaClassIndex);
    }
}

// Delegate property
public class FDelegateProperty : FProperty
{
    public string? SignatureFunctionName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var signatureIndex = ar.ReadInt32();
        SignatureFunctionName = context.ResolvePackageIndex(signatureIndex);
    }
}

// Enum property
public class FEnumProperty : FProperty
{
    public string? EnumName { get; set; }
    public FProperty? UnderlyingProp { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var enumIndex = ar.ReadInt32();
        EnumName = context.ResolvePackageIndex(enumIndex);
        UnderlyingProp = ReadSingleField(ar, context);
    }
}

// FieldPath property
public class FFieldPathProperty : FProperty
{
    public string PropertyClass { get; set; } = string.Empty;

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        PropertyClass = ReadFName(ar, context.NameTable);
    }
}

// Interface property
public class FInterfaceProperty : FProperty
{
    public string? InterfaceClassName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var interfaceIndex = ar.ReadInt32();
        InterfaceClassName = context.ResolvePackageIndex(interfaceIndex);
    }
}

// Map property
public class FMapProperty : FProperty
{
    public FProperty? KeyProp { get; set; }
    public FProperty? ValueProp { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        KeyProp = ReadSingleField(ar, context);
        ValueProp = ReadSingleField(ar, context);
    }
}

// Multicast delegate property
public class FMulticastDelegateProperty : FProperty
{
    public string? SignatureFunctionName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var signatureIndex = ar.ReadInt32();
        SignatureFunctionName = context.ResolvePackageIndex(signatureIndex);
    }
}

// Multicast inline delegate property
public class FMulticastInlineDelegateProperty : FMulticastDelegateProperty { }

// Object property
public class FObjectProperty : FProperty
{
    public string? PropertyClassName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var classIndex = ar.ReadInt32();
        PropertyClassName = context.ResolvePackageIndex(classIndex);
    }
}

// Set property
public class FSetProperty : FProperty
{
    public FProperty? ElementProp { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        ElementProp = ReadSingleField(ar, context);
    }
}

// Soft class property
public class FSoftClassProperty : FSoftObjectProperty
{
    public string? MetaClassName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var metaClassIndex = ar.ReadInt32();
        MetaClassName = context.ResolvePackageIndex(metaClassIndex);
    }
}

// Soft object property
public class FSoftObjectProperty : FObjectProperty { }

// Struct property
public class FStructProperty : FProperty
{
    public string? StructName { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        var structIndex = ar.ReadInt32();
        StructName = context.ResolvePackageIndex(structIndex);
    }
}

// Weak object property
public class FWeakObjectProperty : FObjectProperty { }

// Lazy object property
public class FLazyObjectProperty : FObjectProperty { }

// Optional property
public class FOptionalProperty : FProperty
{
    public FProperty? ValueProperty { get; set; }

    public override void Deserialize(ArchiveReader ar, FFieldContext context)
    {
        base.Deserialize(ar, context);
        ValueProperty = ReadSingleField(ar, context);
    }
}
