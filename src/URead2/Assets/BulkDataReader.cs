using System.Buffers;
using URead2.Assets.Abstractions;
using URead2.Assets.Models;
using URead2.IO;

namespace URead2.Assets;

/// <summary>
/// Default implementation for reading FByteBulkData structures.
/// </summary>
public class BulkDataReader : IBulkDataReader
{
    /// <inheritdoc />
    public virtual byte[] ReadBulkData(ArchiveReader reader, Stream? bulkStream)
    {
        var flags = (BulkDataFlags)reader.ReadUInt32();

        long elementCount;
        long sizeOnDisk;
        long offsetInFile;

        if (flags.HasFlag(BulkDataFlags.Size64Bit))
        {
            elementCount = reader.ReadInt64();
            sizeOnDisk = reader.ReadInt64();
        }
        else
        {
            elementCount = reader.ReadInt32();
            sizeOnDisk = reader.ReadInt32();
        }

        offsetInFile = reader.ReadInt64();

        if (elementCount == 0 || sizeOnDisk == 0)
            return [];

        // Inline data
        if (flags.HasFlag(BulkDataFlags.ForceInlinePayload) ||
            !flags.HasFlag(BulkDataFlags.PayloadInSeperateFile) && !flags.HasFlag(BulkDataFlags.PayloadAtEndOfFile))
        {
            return reader.ReadBytes((int)sizeOnDisk);
        }

        // External data in .ubulk
        if (flags.HasFlag(BulkDataFlags.PayloadInSeperateFile))
        {
            if (bulkStream == null)
                throw new InvalidOperationException("Bulk data is in .ubulk but no bulk stream provided");

            bulkStream.Seek(offsetInFile, SeekOrigin.Begin);
            var data = new byte[sizeOnDisk];
            bulkStream.ReadExactly(data);
            return data;
        }

        // Data at end of asset file
        if (flags.HasFlag(BulkDataFlags.PayloadAtEndOfFile))
        {
            long currentPos = reader.Position;
            reader.Seek(offsetInFile);
            var data = reader.ReadBytes((int)sizeOnDisk);
            reader.Seek(currentPos);
            return data;
        }

        throw new InvalidDataException($"Unknown bulk data flags: {flags}");
    }

    /// <inheritdoc />
    public virtual ExportData ReadBulkDataPooled(ArchiveReader reader, Stream? bulkStream)
    {
        var flags = (BulkDataFlags)reader.ReadUInt32();

        long elementCount;
        long sizeOnDisk;
        long offsetInFile;

        if (flags.HasFlag(BulkDataFlags.Size64Bit))
        {
            elementCount = reader.ReadInt64();
            sizeOnDisk = reader.ReadInt64();
        }
        else
        {
            elementCount = reader.ReadInt32();
            sizeOnDisk = reader.ReadInt32();
        }

        offsetInFile = reader.ReadInt64();

        if (elementCount == 0 || sizeOnDisk == 0)
            return new ExportData(null, 0);

        var buffer = ArrayPool<byte>.Shared.Rent((int)sizeOnDisk);
        try
        {
            if (flags.HasFlag(BulkDataFlags.ForceInlinePayload) ||
                !flags.HasFlag(BulkDataFlags.PayloadInSeperateFile) && !flags.HasFlag(BulkDataFlags.PayloadAtEndOfFile))
            {
                reader.ReadBytes(buffer.AsSpan(0, (int)sizeOnDisk));
            }
            else if (flags.HasFlag(BulkDataFlags.PayloadInSeperateFile))
            {
                if (bulkStream == null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    throw new InvalidOperationException("Bulk data is in .ubulk but no bulk stream provided");
                }

                bulkStream.Seek(offsetInFile, SeekOrigin.Begin);
                bulkStream.ReadExactly(buffer.AsSpan(0, (int)sizeOnDisk));
            }
            else if (flags.HasFlag(BulkDataFlags.PayloadAtEndOfFile))
            {
                long currentPos = reader.Position;
                reader.Seek(offsetInFile);
                reader.ReadBytes(buffer.AsSpan(0, (int)sizeOnDisk));
                reader.Seek(currentPos);
            }
            else
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw new InvalidDataException($"Unknown bulk data flags: {flags}");
            }

            return new ExportData(buffer, (int)sizeOnDisk);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
