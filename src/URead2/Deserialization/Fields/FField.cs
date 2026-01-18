using URead2.IO;

namespace URead2.Deserialization.Fields;

/// <summary>
/// Base class for FField - runtime property/function metadata.
/// </summary>
public class FField
{
    public string Name { get; set; } = string.Empty;
    public uint Flags { get; set; }

    public virtual void Deserialize(ArchiveReader ar, string[] nameTable)
    {
        Name = ReadFName(ar, nameTable);
        Flags = ar.ReadUInt32();
    }

    protected static string ReadFName(ArchiveReader ar, string[] nameTable)
    {
        int index = ar.ReadInt32();
        int number = ar.ReadInt32();

        if (index < 0 || index >= nameTable.Length)
            return "None";

        var name = nameTable[index];
        return number > 0 ? $"{name}_{number - 1}" : name;
    }
}
