using System.IO.Compression;
using System.Text;
using URead2.Compression;
using URead2.IO;

namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// Reads and parses .usmap type mapping files.
/// </summary>
public sealed class UsmapReader
{
    private const ushort UsmapMagic = 0x30C4;

    private readonly Decompressor? _decompressor;

    /// <summary>
    /// Creates a UsmapReader with optional decompressor for Oodle-compressed .usmap files.
    /// </summary>
    public UsmapReader(Decompressor? decompressor = null)
    {
        _decompressor = decompressor;
    }

    /// <summary>
    /// Reads a .usmap file from disk.
    /// </summary>
    public TypeMappings Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>
    /// Reads a .usmap file from a stream.
    /// </summary>
    public TypeMappings Read(Stream stream)
    {
        using var archive = new ArchiveReader(stream, leaveOpen: true);
        return Read(archive);
    }

    /// <summary>
    /// Reads a .usmap file from an archive reader.
    /// </summary>
    public TypeMappings Read(ArchiveReader archive)
    {
        // Header
        var magic = archive.ReadUInt16();
        if (magic != UsmapMagic)
            throw new InvalidDataException($"Invalid .usmap magic: 0x{magic:X4}, expected 0x{UsmapMagic:X4}");

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

        return ParseData(dataArchive, version);
    }

    private void DecompressData(EUsmapCompression compression, byte[] source, byte[] destination)
    {
        switch (compression)
        {
            case EUsmapCompression.Oodle:
                if (_decompressor == null)
                    throw new InvalidOperationException("Oodle decompressor required but not provided");
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

    private static TypeMappings ParseData(ArchiveReader archive, EUsmapVersion version)
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
        var enums = new Dictionary<string, UsmapEnum>(enumCount, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < enumCount; i++)
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

            enums.TryAdd(enumName, new UsmapEnum(enumName, values));
        }

        // Schemas
        var schemaCount = archive.ReadInt32();
        var schemas = new Dictionary<string, UsmapSchema>(schemaCount, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < schemaCount; i++)
        {
            var schema = ReadSchema(archive, nameMap);
            schemas.TryAdd(schema.Name, schema);
        }

        return new TypeMappings(schemas, enums);
    }

    private static UsmapSchema ReadSchema(ArchiveReader archive, string[] nameMap)
    {
        var name = ReadName(archive, nameMap);
        var superType = ReadNameOrNull(archive, nameMap);
        var propertyCount = archive.ReadUInt16();
        var serializablePropertyCount = archive.ReadUInt16();

        var properties = new Dictionary<int, UsmapProperty>(serializablePropertyCount);

        for (int i = 0; i < serializablePropertyCount; i++)
        {
            var schemaIndex = archive.ReadUInt16();
            var arraySize = archive.ReadByte();
            var propName = ReadName(archive, nameMap);
            var propType = ReadPropertyType(archive, nameMap);

            var property = new UsmapProperty(propName, schemaIndex, arraySize, propType);

            // Expand static arrays
            for (int j = 0; j < arraySize; j++)
            {
                var expandedProp = new UsmapProperty(propName, (ushort)(schemaIndex + j), arraySize, propType)
                {
                    ArrayIndex = (ushort)j
                };
                properties[schemaIndex + j] = expandedProp;
            }
        }

        return new UsmapSchema(name, superType, propertyCount, properties);
    }

    private static UsmapPropertyType ReadPropertyType(ArchiveReader archive, string[] nameMap)
    {
        var type = (EPropertyType)archive.ReadByte();
        var propType = new UsmapPropertyType(type);

        switch (type)
        {
            case EPropertyType.EnumProperty:
                return new UsmapPropertyType(type)
                {
                    InnerType = ReadPropertyType(archive, nameMap),
                    EnumName = ReadName(archive, nameMap)
                };

            case EPropertyType.StructProperty:
                return new UsmapPropertyType(type)
                {
                    StructType = ReadName(archive, nameMap)
                };

            case EPropertyType.ArrayProperty:
            case EPropertyType.SetProperty:
            case EPropertyType.OptionalProperty:
                return new UsmapPropertyType(type)
                {
                    InnerType = ReadPropertyType(archive, nameMap)
                };

            case EPropertyType.MapProperty:
                return new UsmapPropertyType(type)
                {
                    InnerType = ReadPropertyType(archive, nameMap),
                    ValueType = ReadPropertyType(archive, nameMap)
                };

            default:
                return propType;
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
