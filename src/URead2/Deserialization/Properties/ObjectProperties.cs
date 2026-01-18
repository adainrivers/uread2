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

    public static ObjectProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new ObjectProperty(ObjectReference.Null);

        if (!ar.TryReadInt32(out var packageIndex))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new ObjectProperty(ObjectReference.Null);
        }

        return new ObjectProperty(ctx.ResolveReference(packageIndex));
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
    public static SoftObjectProperty CreateTagged(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new SoftObjectProperty(new SoftObjectPath(null, null));

        if (!ar.TryReadFString(out var assetPath) || !ar.TryReadFString(out var subPath))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SoftObjectProperty(new SoftObjectPath(null, null));
        }

        return new SoftObjectProperty(new SoftObjectPath(assetPath, subPath));
    }

    /// <summary>
    /// Reads SoftObjectProperty in unversioned format using FTopLevelAssetPath (FName pairs) + SubPath.
    /// </summary>
    public static SoftObjectProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new SoftObjectProperty(new SoftObjectPath(null, null));

        // FSoftObjectPath in UE5:
        // - FTopLevelAssetPath: PackageName (FMappedName 8 bytes) + AssetName (FMappedName 8 bytes)
        // - SubPathString: FString

        // Read FMappedName for PackageName
        if (!ar.TryReadUInt32(out var packageNameRaw) || !ar.TryReadUInt32(out var packageNameExtra))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SoftObjectProperty(new SoftObjectPath(null, null));
        }
        var packageNameIndex = (int)(packageNameRaw & 0x3FFFFFFF);

        // Read FMappedName for AssetName
        if (!ar.TryReadUInt32(out var assetNameRaw) || !ar.TryReadUInt32(out var assetNameExtra))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SoftObjectProperty(new SoftObjectPath(null, null));
        }
        var assetNameIndex = (int)(assetNameRaw & 0x3FFFFFFF);

        // Read SubPath FString
        if (!ar.TryReadFString(out var subPath))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new SoftObjectProperty(new SoftObjectPath(null, null));
        }

        var nameTable = ctx.NameTable;
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

        return new SoftObjectProperty(new SoftObjectPath(fullPath, string.IsNullOrEmpty(subPath) ? null : subPath));
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

    public static InterfaceProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new InterfaceProperty(ObjectReference.Null);

        if (!ar.TryReadInt32(out var packageIndex))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new InterfaceProperty(ObjectReference.Null);
        }

        return new InterfaceProperty(ctx.ResolveReference(packageIndex));
    }

    public override string? ToString() => Value?.ToString();
}

/// <summary>
/// Delegate property value.
/// </summary>
public sealed class DelegateProperty : PropertyValue<DelegateValue>
{
    public DelegateProperty(DelegateValue value) => Value = value;

    public static DelegateProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new DelegateProperty(default);

        // Skip reading delegate data - just advance the stream
        if (!ar.TryReadInt32(out _) || !ar.TryReadFString(out _))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new DelegateProperty(default);
        }

        return new DelegateProperty(default);
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

    public static MulticastDelegateProperty Create(ArchiveReader ar, PropertyReadContext ctx, ReadContext readCtx)
    {
        if (readCtx == ReadContext.Zero)
            return new MulticastDelegateProperty([]);

        if (!ar.TryReadInt32(out var count))
        {
            ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
            return new MulticastDelegateProperty([]);
        }

        if (count < 0 || count > 10000)
        {
            ctx.Warn(DiagnosticCode.InvalidCollectionCount, ar.Position - 4, $"MulticastDelegate count={count}");
            return new MulticastDelegateProperty([]);
        }

        for (int i = 0; i < count; i++)
        {
            if (!ar.TryReadInt32(out _) || !ar.TryReadFString(out _))
            {
                ctx.Fatal(ReadErrorCode.StreamOverrun, ar.Position);
                return new MulticastDelegateProperty([]);
            }
        }

        return new MulticastDelegateProperty([]);
    }
}
