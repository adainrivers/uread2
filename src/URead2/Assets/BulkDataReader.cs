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
        if (!reader.TryReadUInt32(out var flagsRaw))
            return [];

        var flags = (BulkDataFlags)flagsRaw;

        long elementCount;
        long sizeOnDisk;
        long offsetInFile;

        if (flags.HasFlag(BulkDataFlags.Size64Bit))
        {
            if (!reader.TryReadInt64(out elementCount) || !reader.TryReadInt64(out sizeOnDisk))
                return [];
        }
        else
        {
            if (!reader.TryReadInt32(out var ec) || !reader.TryReadInt32(out var sd))
                return [];
            elementCount = ec;
            sizeOnDisk = sd;
        }

        if (!reader.TryReadInt64(out offsetInFile))
            return [];

        if (elementCount == 0 || sizeOnDisk == 0)
            return [];

        // Inline data
        if (flags.HasFlag(BulkDataFlags.ForceInlinePayload) ||
            !flags.HasFlag(BulkDataFlags.PayloadInSeperateFile) && !flags.HasFlag(BulkDataFlags.PayloadAtEndOfFile))
        {
            if (!reader.TryReadBytes((int)sizeOnDisk, out var data))
                return [];
            return data;
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
            if (!reader.TryReadBytes((int)sizeOnDisk, out var data))
            {
                reader.Seek(currentPos);
                return [];
            }
            reader.Seek(currentPos);
            return data;
        }

        throw new InvalidDataException($"Unknown bulk data flags: {flags}");
    }

    /// <inheritdoc />
    public virtual ExportData ReadBulkDataPooled(ArchiveReader reader, Stream? bulkStream)
    {
        if (!reader.TryReadUInt32(out var flagsRaw))
            return new ExportData(null, 0);

        var flags = (BulkDataFlags)flagsRaw;

        long elementCount;
        long sizeOnDisk;
        long offsetInFile;

        if (flags.HasFlag(BulkDataFlags.Size64Bit))
        {
            if (!reader.TryReadInt64(out elementCount) || !reader.TryReadInt64(out sizeOnDisk))
                return new ExportData(null, 0);
        }
        else
        {
            if (!reader.TryReadInt32(out var ec) || !reader.TryReadInt32(out var sd))
                return new ExportData(null, 0);
            elementCount = ec;
            sizeOnDisk = sd;
        }

        if (!reader.TryReadInt64(out offsetInFile))
            return new ExportData(null, 0);

        if (elementCount == 0 || sizeOnDisk == 0)
            return new ExportData(null, 0);

        var buffer = ArrayPool<byte>.Shared.Rent((int)sizeOnDisk);
        try
        {
            if (flags.HasFlag(BulkDataFlags.ForceInlinePayload) ||
                !flags.HasFlag(BulkDataFlags.PayloadInSeperateFile) && !flags.HasFlag(BulkDataFlags.PayloadAtEndOfFile))
            {
                if (!reader.TryReadBytes(buffer.AsSpan(0, (int)sizeOnDisk)))
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    return new ExportData(null, 0);
                }
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
                if (!reader.TryReadBytes(buffer.AsSpan(0, (int)sizeOnDisk)))
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                    reader.Seek(currentPos);
                    return new ExportData(null, 0);
                }
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
