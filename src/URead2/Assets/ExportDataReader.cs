using URead2.Assets.Abstractions;
using URead2.Assets.Models;

namespace URead2.Assets;

/// <summary>
/// Default implementation for reading export binary data.
/// </summary>
public class ExportDataReader : IExportDataReader
{
    /// <inheritdoc />
    public virtual void ReadExportData(AssetExport export, Stream assetStream, Span<byte> destination)
    {
        if (destination.Length < export.SerialSize)
            throw new ArgumentException($"Buffer too small: {destination.Length} < {export.SerialSize}", nameof(destination));

        assetStream.Seek(export.SerialOffset, SeekOrigin.Begin);
        assetStream.ReadExactly(destination[..(int)export.SerialSize]);
    }
}
