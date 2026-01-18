using System.IO.Compression;
using System.Text;
using URead2.Compression;
using URead2.IO;

namespace URead2.TypeResolution;

/// <summary>
/// Loads type definitions from .usmap files into a TypeRegistry.
/// </summary>
public sealed class UsmapLoader
{
    private const ushort UsmapMagic = 0x30C4;

    private readonly Decompressor? _decompressor;

    public UsmapLoader(Decompressor? decompressor = null)
    {
        _decompressor = decompressor;
    }

    /// <summary>
    /// Loads types from a .usmap file into the registry.
    /// </summary>
    public void Load(string path, TypeRegistry registry)
    {
        using var stream = File.OpenRead(path);
        Load(stream, registry);
    }

    /// <summary>
    /// Loads types from a .usmap stream into the registry.
    /// </summary>
    public void Load(Stream stream, TypeRegistry registry)
    {
        using var archive = new ArchiveReader(stream, leaveOpen: true);

        // Header
        if (!archive.TryReadUInt16(out var magic))
            throw new InvalidDataException("Failed to read .usmap magic");

        if (magic != UsmapMagic)
            throw new InvalidDataException($"Invalid .usmap magic: 0x{magic:X4}");

        if (!archive.TryReadByte(out var versionByte))
            throw new InvalidDataException("Failed to read .usmap version");

        var version = (EUsmapVersion)versionByte;
        if (version > EUsmapVersion.Latest)
            throw new InvalidDataException($"Unsupported .usmap version: {version}");

        // Package versioning (optional)
        if (version >= EUsmapVersion.PackageVersioning)
        {
            if (!archive.TryReadInt32(out var hasVersioningRaw))
                throw new InvalidDataException("Failed to read versioning flag");

            bool hasVersioning = hasVersioningRaw != 0;
            if (hasVersioning)
            {
                if (!archive.TrySkip(4)) // FileVersionUE4
                    throw new InvalidDataException("Failed to skip FileVersionUE4");
                if (!archive.TrySkip(4)) // FileVersionUE5
                    throw new InvalidDataException("Failed to skip FileVersionUE5");
                if (!archive.TryReadInt32(out int customVersionCount))
                    throw new InvalidDataException("Failed to read custom version count");
                if (!archive.TrySkip(customVersionCount * 20)) // GUID (16) + Version (4)
                    throw new InvalidDataException("Failed to skip custom versions");
                if (!archive.TrySkip(4)) // NetCL
                    throw new InvalidDataException("Failed to skip NetCL");
            }
        }

        // Compression
        if (!archive.TryReadByte(out var compressionByte))
            throw new InvalidDataException("Failed to read compression method");

        var compression = (EUsmapCompression)compressionByte;

        if (!archive.TryReadUInt32(out var compressedSize) ||
            !archive.TryReadUInt32(out var decompressedSize))
            throw new InvalidDataException("Failed to read compression sizes");

        // Read and decompress data
        if (!archive.TryReadBytes((int)compressedSize, out var compressedData))
            throw new InvalidDataException("Failed to read compressed data");

        byte[] data;

        if (compression == EUsmapCompression.None)
        {
            if (compressedSize != decompressedSize)
                throw new InvalidDataException("Uncompressed size mismatch");
            data = compressedData;
        }
        else
        {
            data = new byte[decompressedSize];
            DecompressData(compression, compressedData, data);
        }

        // Parse decompressed data
        using var dataStream = new MemoryStream(data);
        using var dataArchive = new ArchiveReader(dataStream, leaveOpen: true);

        ParseAndLoad(dataArchive, version, registry);
    }

    private void DecompressData(EUsmapCompression compression, byte[] source, byte[] destination)
    {
        switch (compression)
        {
            case EUsmapCompression.Oodle:
                if (_decompressor == null)
                    throw new InvalidOperationException("Oodle decompressor required");
                _decompressor.Decompress(source, destination, CompressionMethod.Oodle);
                break;

            case EUsmapCompression.ZStandard:
                using (var zstd = new ZstdSharp.Decompressor())
                {
                    zstd.Unwrap(source, destination);
                }
                break;

            case EUsmapCompression.Brotli:
                using (var decoder = new BrotliDecoder())
                {
                    decoder.Decompress(source, destination, out _, out _);
                }
                break;

            default:
                throw new NotSupportedException($"Unsupported compression: {compression}");
        }
    }

    private static void ParseAndLoad(ArchiveReader archive, EUsmapVersion version, TypeRegistry registry)
    {
        // Name map
        if (!archive.TryReadInt32(out var nameCount))
            throw new InvalidDataException("Failed to read name count");

        var nameMap = new string[nameCount];

        for (int i = 0; i < nameCount; i++)
        {
            int length;
            if (version >= EUsmapVersion.LongFName)
            {
                if (!archive.TryReadUInt16(out var len16))
                    throw new InvalidDataException($"Failed to read name length at index {i}");
                length = len16;
            }
            else
            {
                if (!archive.TryReadByte(out var len8))
                    throw new InvalidDataException($"Failed to read name length at index {i}");
                length = len8;
            }

            if (!archive.TryReadBytes(length, out var bytes))
                throw new InvalidDataException($"Failed to read name bytes at index {i}");

            nameMap[i] = Encoding.UTF8.GetString(bytes);
        }

        // Enums
        if (!archive.TryReadInt32(out var enumCount))
            throw new InvalidDataException("Failed to read enum count");

        for (int i = 0; i < enumCount; i++)
        {
            var enumDef = ReadEnum(archive, nameMap, version);
            if (enumDef != null)
                registry.Register(enumDef);
        }

        // Types (schemas)
        if (!archive.TryReadInt32(out var schemaCount))
            throw new InvalidDataException("Failed to read schema count");

        for (int i = 0; i < schemaCount; i++)
        {
            var typeDef = ReadType(archive, nameMap, version);
            if (typeDef != null)
                registry.Register(typeDef);
        }
    }

    private static EnumDefinition? ReadEnum(ArchiveReader archive, string[] nameMap, EUsmapVersion version)
    {
        var enumName = ReadName(archive, nameMap);
        if (enumName == null)
            return null;

        var values = new Dictionary<long, string>();

        int valueCount;
        if (version >= EUsmapVersion.LargeEnums)
        {
            if (!archive.TryReadUInt16(out var vc16))
                return null;
            valueCount = vc16;
        }
        else
        {
            if (!archive.TryReadByte(out var vc8))
                return null;
            valueCount = vc8;
        }

        if (version >= EUsmapVersion.ExplicitEnumValues)
        {
            for (int j = 0; j < valueCount; j++)
            {
                if (!archive.TryReadInt64(out var value))
                    break;
                var name = ReadName(archive, nameMap);
                if (name != null)
                    values[value] = name;
            }
        }
        else
        {
            for (int j = 0; j < valueCount; j++)
            {
                var name = ReadName(archive, nameMap);
                if (name != null)
                    values[j] = name;
            }
        }

        return new EnumDefinition(enumName, TypeSource.Runtime, values);
    }

    private static TypeDefinition? ReadType(ArchiveReader archive, string[] nameMap, EUsmapVersion version)
    {
        var name = ReadName(archive, nameMap);
        if (name == null)
            return null;

        var superType = ReadNameOrNull(archive, nameMap);

        if (!archive.TryReadUInt16(out var propertyCount) ||
            !archive.TryReadUInt16(out var serializablePropertyCount))
            return null;

        var properties = new Dictionary<int, PropertyDefinition>(serializablePropertyCount);

        for (int i = 0; i < serializablePropertyCount; i++)
        {
            if (!archive.TryReadUInt16(out var schemaIndex) ||
                !archive.TryReadByte(out var arraySize))
                break;

            var propName = ReadName(archive, nameMap);
            if (propName == null)
                break;

            var propType = ReadPropertyType(archive, nameMap);
            if (propType == null)
                break;

            // Expand static arrays
            for (int j = 0; j < arraySize; j++)
            {
                var prop = new PropertyDefinition(propName, schemaIndex + j, propType)
                {
                    ArrayIndex = j,
                    ArraySize = arraySize
                };
                properties[schemaIndex + j] = prop;
            }
        }

        return new TypeDefinition(name, TypeSource.Runtime, properties)
        {
            SuperName = superType,
            PropertyCount = propertyCount,
            Kind = TypeKind.Class // Default, structs are also represented as schemas
        };
    }

    private static PropertyType? ReadPropertyType(ArchiveReader archive, string[] nameMap)
    {
        if (!archive.TryReadByte(out var kindByte))
            return null;

        var kind = (PropertyKind)kindByte;

        switch (kind)
        {
            case PropertyKind.EnumProperty:
                var innerType = ReadPropertyType(archive, nameMap);
                var enumName = ReadName(archive, nameMap);
                if (innerType == null || enumName == null)
                    return null;
                return new PropertyType(PropertyKind.EnumProperty)
                {
                    InnerType = innerType,
                    EnumName = enumName
                };

            case PropertyKind.StructProperty:
                var structName = ReadName(archive, nameMap);
                return structName != null ? PropertyType.Struct(structName) : null;

            case PropertyKind.ArrayProperty:
            case PropertyKind.SetProperty:
            case PropertyKind.OptionalProperty:
                var inner = ReadPropertyType(archive, nameMap);
                return inner != null ? new PropertyType(kind) { InnerType = inner } : null;

            case PropertyKind.MapProperty:
                var keyType = ReadPropertyType(archive, nameMap);
                var valueType = ReadPropertyType(archive, nameMap);
                return keyType != null && valueType != null
                    ? PropertyType.Map(keyType, valueType)
                    : null;

            default:
                return PropertyType.Simple(kind);
        }
    }

    private static string? ReadName(ArchiveReader archive, string[] nameMap)
    {
        if (!archive.TryReadInt32(out var index))
            return null;

        if (index < 0 || index >= nameMap.Length)
            return null;

        return nameMap[index];
    }

    private static string? ReadNameOrNull(ArchiveReader archive, string[] nameMap)
    {
        if (!archive.TryReadInt32(out var index))
            return null;

        if (index < 0 || index >= nameMap.Length)
            return null;

        var name = nameMap[index];
        return string.IsNullOrEmpty(name) ? null : name;
    }
}

/// <summary>
/// Usmap file version.
/// </summary>
public enum EUsmapVersion : byte
{
    Initial = 0,
    PackageVersioning = 1,
    LongFName = 2,
    LargeEnums = 3,
    ExplicitEnumValues = 4,

    Latest = ExplicitEnumValues
}

/// <summary>
/// Usmap compression method.
/// </summary>
public enum EUsmapCompression : byte
{
    None = 0,
    Oodle = 1,
    Brotli = 2,
    ZStandard = 3
}
