using URead2.Assets.Models;
using URead2.IO;

namespace URead2.Assets.Abstractions;

/// <summary>
/// Reads FByteBulkData structures from export data, resolving data from inline or .ubulk.
/// </summary>
public interface IBulkDataReader
{
    /// <summary>
    /// Reads FByteBulkData from the reader's current position.
    /// Resolves data from inline, end of file, or external .ubulk based on flags.
    /// </summary>
    /// <param name="reader">Archive reader positioned at FByteBulkData structure.</param>
    /// <param name="bulkStream">Optional .ubulk stream for external bulk data.</param>
    /// <returns>The resolved bulk data bytes, or empty array if no data.</returns>
    byte[] ReadBulkData(ArchiveReader reader, Stream? bulkStream);

    /// <summary>
    /// Reads FByteBulkData from the reader's current position into a pooled buffer.
    /// Caller must dispose the result to return buffer to pool.
    /// </summary>
    /// <param name="reader">Archive reader positioned at FByteBulkData structure.</param>
    /// <param name="bulkStream">Optional .ubulk stream for external bulk data.</param>
    /// <returns>Pooled bulk data. Dispose when done.</returns>
    ExportData ReadBulkDataPooled(ArchiveReader reader, Stream? bulkStream);
}
