using URead2.IO;

namespace URead2.Deserialization.Fields;

/// <summary>
/// Base class for FField - runtime property/function metadata.
/// </summary>
public class FField
{
    public string Name { get; set; } = string.Empty;
    public uint Flags { get; set; }

    public virtual bool Deserialize(ArchiveReader ar, string[] nameTable)
    {
        Name = ReadFName(ar, nameTable, out var success);
        if (!success)
            return false;

        if (!ar.TryReadUInt32(out var flags))
            return false;

        Flags = flags;
        return true;
    }

    protected static string ReadFName(ArchiveReader ar, string[] nameTable, out bool success)
    {
        success = false;
        if (!ar.TryReadInt32(out int index) || !ar.TryReadInt32(out int number))
            return "None";

        success = true;
        if (index < 0 || index >= nameTable.Length)
            return "None";

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }
}
