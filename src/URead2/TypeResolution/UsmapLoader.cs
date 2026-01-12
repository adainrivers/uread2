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
        var magic = archive.ReadUInt16();
        if (magic != UsmapMagic)
            throw new InvalidDataException($"Invalid .usmap magic: 0x{magic:X4}");

        var version = (EUsmapVersion)archive.ReadByte();
        if (version > EUsmapVersion.Latest)
            throw new InvalidDataException($"Unsupported .usmap version: {version}");

        // Package versioning (optional)
        if (version >= EUsmapVersion.PackageVersioning)
        {
            bool hasVersioning = archive.ReadInt32() != 0;
            if (hasVersioning)
            {
                archive.Skip(4); // FileVersionUE4
                archive.Skip(4); // FileVersionUE5
                int customVersionCount = archive.ReadInt32();
                archive.Skip(customVersionCount * 20); // GUID (16) + Version (4)
                archive.Skip(4); // NetCL
            }
        }

        // Compression
        var compression = (EUsmapCompression)archive.ReadByte();
        var compressedSize = archive.ReadUInt32();
        var decompressedSize = archive.ReadUInt32();

        // Read and decompress data
        var compressedData = archive.ReadBytes((int)compressedSize);
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
        var nameCount = archive.ReadInt32();
        var nameMap = new string[nameCount];

        for (int i = 0; i < nameCount; i++)
        {
            int length = version >= EUsmapVersion.LongFName
                ? archive.ReadUInt16()
                : archive.ReadByte();

            var bytes = archive.ReadBytes(length);
            nameMap[i] = Encoding.UTF8.GetString(bytes);
        }

        // Enums
        var enumCount = archive.ReadInt32();
        for (int i = 0; i < enumCount; i++)
        {
            var enumDef = ReadEnum(archive, nameMap, version);
            registry.Register(enumDef);
        }

        // Types (schemas)
        var schemaCount = archive.ReadInt32();
        for (int i = 0; i < schemaCount; i++)
        {
            var typeDef = ReadType(archive, nameMap, version);
            registry.Register(typeDef);
        }
    }

    private static EnumDefinition ReadEnum(ArchiveReader archive, string[] nameMap, EUsmapVersion version)
    {
        var enumName = ReadName(archive, nameMap);
        var values = new Dictionary<long, string>();

        int valueCount = version >= EUsmapVersion.LargeEnums
            ? archive.ReadUInt16()
            : archive.ReadByte();

        if (version >= EUsmapVersion.ExplicitEnumValues)
        {
            for (int j = 0; j < valueCount; j++)
            {
                var value = archive.ReadInt64();
                var name = ReadName(archive, nameMap);
                values[value] = name;
            }
        }
        else
        {
            for (int j = 0; j < valueCount; j++)
            {
                var name = ReadName(archive, nameMap);
                values[j] = name;
            }
        }

        return new EnumDefinition(enumName, TypeSource.Runtime, values);
    }

    private static TypeDefinition ReadType(ArchiveReader archive, string[] nameMap, EUsmapVersion version)
    {
        var name = ReadName(archive, nameMap);
        var superType = ReadNameOrNull(archive, nameMap);
        var propertyCount = archive.ReadUInt16();
        var serializablePropertyCount = archive.ReadUInt16();

        var properties = new Dictionary<int, PropertyDefinition>(serializablePropertyCount);

        for (int i = 0; i < serializablePropertyCount; i++)
        {
            var schemaIndex = archive.ReadUInt16();
            var arraySize = archive.ReadByte();
            var propName = ReadName(archive, nameMap);
            var propType = ReadPropertyType(archive, nameMap);

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

    private static PropertyType ReadPropertyType(ArchiveReader archive, string[] nameMap)
    {
        var kind = (PropertyKind)archive.ReadByte();

        switch (kind)
        {
            case PropertyKind.EnumProperty:
                var innerType = ReadPropertyType(archive, nameMap);
                var enumName = ReadName(archive, nameMap);
                return new PropertyType(PropertyKind.EnumProperty)
                {
                    InnerType = innerType,
                    EnumName = enumName
                };

            case PropertyKind.StructProperty:
                return PropertyType.Struct(ReadName(archive, nameMap));

            case PropertyKind.ArrayProperty:
            case PropertyKind.SetProperty:
            case PropertyKind.OptionalProperty:
                return new PropertyType(kind)
                {
                    InnerType = ReadPropertyType(archive, nameMap)
                };

            case PropertyKind.MapProperty:
                return PropertyType.Map(
                    ReadPropertyType(archive, nameMap),
                    ReadPropertyType(archive, nameMap));

            default:
                return PropertyType.Simple(kind);
        }
    }

    private static string ReadName(ArchiveReader archive, string[] nameMap)
    {
        var index = archive.ReadInt32();
        if (index < 0 || index >= nameMap.Length)
            throw new InvalidDataException($"Invalid name index: {index}");
        return nameMap[index];
    }

    private static string? ReadNameOrNull(ArchiveReader archive, string[] nameMap)
    {
        var index = archive.ReadInt32();
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
