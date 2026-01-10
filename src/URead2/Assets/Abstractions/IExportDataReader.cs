using URead2.Assets.Models;

namespace URead2.Assets.Abstractions;

/// <summary>
/// Reads binary data for individual exports from an asset stream.
/// </summary>
public interface IExportDataReader
{
    /// <summary>
    /// Reads export binary data into a caller-provided buffer.
    /// </summary>
    /// <param name="export">The export metadata containing offset and size.</param>
    /// <param name="assetStream">The asset stream to read from.</param>
    /// <param name="destination">Buffer to write data into. Must be at least export.SerialSize bytes.</param>
    void ReadExportData(AssetExport export, Stream assetStream, Span<byte> destination);
}
