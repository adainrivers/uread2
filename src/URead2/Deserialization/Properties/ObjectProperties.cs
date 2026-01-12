using URead2.Deserialization.Abstractions;
using URead2.IO;

namespace URead2.Deserialization.Properties;

/// <summary>
/// Object reference property value.
/// Contains resolved type, name, path, and index information.
/// </summary>
public sealed class ObjectProperty : PropertyValue<ObjectReference>
{
    public ObjectProperty(ObjectReference reference) => Value = reference;

    public ObjectProperty(ArchiveReader ar, PropertyReadContext context, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
        {
            Value = ObjectReference.Null;
            return;
        }

        var packageIndex = ar.ReadInt32();
        Value = context.ResolveReference(packageIndex);
    }

    /// <summary>
    /// True if this references an import (external object).
    /// </summary>
    public bool IsImport => Value?.IsImport ?? false;

    /// <summary>
    /// True if this references an export (local object).
    /// </summary>
    public bool IsExport => Value?.IsExport ?? false;

    /// <summary>
    /// True if this is a null reference.
    /// </summary>
    public bool IsNull => Value?.IsNull ?? true;

    /// <summary>
    /// The object's class type.
    /// </summary>
    public string? Type => Value?.Type;

    /// <summary>
    /// The object's name.
    /// </summary>
    public string? Name => Value?.Name;

    /// <summary>
    /// The package path.
    /// </summary>
    public string? Path => Value?.Path;

    /// <summary>
    /// The raw package index.
    /// </summary>
    public int Index => Value?.Index ?? 0;

    public override string? ToString() => Value?.ToString();
}

/// <summary>
/// Soft object path property value.
/// </summary>
public sealed class SoftObjectProperty : PropertyValue<SoftObjectPath>
{
    public SoftObjectProperty(SoftObjectPath value) => Value = value;

    /// <summary>
    /// Reads SoftObjectProperty in tagged/versioned format (uses FString).
    /// </summary>
    public SoftObjectProperty(ArchiveReader ar, ReadContext context)
    {
        if (context == ReadContext.Zero)
        {
            Value = new SoftObjectPath(null, null);
            return;
        }

        var assetPath = ar.ReadFString();
        var subPath = ar.ReadFString();
        Value = new SoftObjectPath(assetPath, subPath);
    }

    /// <summary>
    /// Reads SoftObjectProperty in unversioned format using FTopLevelAssetPath (FName pairs) + SubPath.
    /// </summary>
    public SoftObjectProperty(ArchiveReader ar, string[] nameTable, ReadContext context)
    {
        if (context == ReadContext.Zero)
        {
            Value = new SoftObjectPath(null, null);
            return;
        }

        // FSoftObjectPath in UE5:
        // - FTopLevelAssetPath: PackageName (FMappedName 8 bytes) + AssetName (FMappedName 8 bytes)
        // - SubPathString: FString

        // Read FMappedName for PackageName
        var packageNameRaw = ar.ReadUInt32();
        var packageNameExtra = ar.ReadUInt32();
        var packageNameIndex = (int)(packageNameRaw & 0x3FFFFFFF);

        // Read FMappedName for AssetName
        var assetNameRaw = ar.ReadUInt32();
        var assetNameExtra = ar.ReadUInt32();
        var assetNameIndex = (int)(assetNameRaw & 0x3FFFFFFF);

        // Read SubPath FString
        var subPath = ar.ReadFString();

        string? packageName = null;
        string? assetName = null;

        if (packageNameIndex >= 0 && packageNameIndex < nameTable.Length)
        {
            packageName = nameTable[packageNameIndex];
            if (packageNameExtra > 0)
                packageName = $"{packageName}_{packageNameExtra - 1}";
        }

        if (assetNameIndex >= 0 && assetNameIndex < nameTable.Length)
        {
            assetName = nameTable[assetNameIndex];
            if (assetNameExtra > 0)
                assetName = $"{assetName}_{assetNameExtra - 1}";
        }

        // Combine into path format
        string? fullPath = null;
        if (!string.IsNullOrEmpty(packageName))
        {
            fullPath = string.IsNullOrEmpty(assetName) ? packageName : $"{packageName}.{assetName}";
        }

        Value = new SoftObjectPath(fullPath, string.IsNullOrEmpty(subPath) ? null : subPath);
    }
}

/// <summary>
/// Soft object path (asset path + sub-path).
/// </summary>
public readonly record struct SoftObjectPath(string? AssetPath, string? SubPath)
{
    public override string ToString() => SubPath != null ? $"{AssetPath}:{SubPath}" : AssetPath ?? string.Empty;
}

/// <summary>
/// Interface property value (object reference).
/// </summary>
public sealed class InterfaceProperty : PropertyValue<ObjectReference>
{
    public InterfaceProperty(ObjectReference reference) => Value = reference;

    public InterfaceProperty(ArchiveReader ar, PropertyReadContext context, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
        {
            Value = ObjectReference.Null;
            return;
        }

        var packageIndex = ar.ReadInt32();
        Value = context.ResolveReference(packageIndex);
    }

    public override string? ToString() => Value?.ToString();
}

/// <summary>
/// Delegate property value.
/// </summary>
public sealed class DelegateProperty : PropertyValue<DelegateValue>
{
    public DelegateProperty(DelegateValue value) => Value = value;

    public DelegateProperty(ArchiveReader ar, PropertyReadContext context, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
        {
            Value = default;
            return;
        }

        // Skip reading delegate data - just advance the stream
        ar.ReadInt32(); // objectIndex
        ar.ReadFString(); // functionName
        Value = default;
    }
}

/// <summary>
/// Delegate value (object + function name).
/// </summary>
public readonly record struct DelegateValue(ObjectReference Object, string? FunctionName)
{
    public override string ToString() => Object.IsNull ? FunctionName ?? "None" : $"{Object}.{FunctionName}";
}

/// <summary>
/// Multicast delegate property value.
/// </summary>
public sealed class MulticastDelegateProperty : PropertyValue<DelegateValue[]>
{
    public MulticastDelegateProperty(DelegateValue[] value) => Value = value;

    public MulticastDelegateProperty(ArchiveReader ar, PropertyReadContext context, ReadContext readContext)
    {
        if (readContext == ReadContext.Zero)
        {
            Value = [];
            return;
        }

        // Skip reading delegate data - just advance the stream
        var count = ar.ReadInt32();
        if (count < 0 || count > 10000)
            throw new InvalidDataException($"MulticastDelegateProperty count out of range: {count}");

        for (int i = 0; i < count; i++)
        {
            ar.ReadInt32(); // objectIndex
            ar.ReadFString(); // functionName - must read to advance stream
        }

        Value = [];
    }
}
