namespace URead2.Assets.Models;

/// <summary>
/// Flags for FByteBulkData serialization.
/// </summary>
[Flags]
public enum BulkDataFlags : uint
{
    None = 0,
    PayloadAtEndOfFile = 0x0001,
    SerializeCompressedZLIB = 0x0002,
    ForceSingleElementSerialization = 0x0008,
    SingleUse = 0x0010,
    Unused = 0x0020,
    PayloadInSeperateFile = 0x0100,
    SerializeCompressedBitWindow = 0x0200,
    ForceInlinePayload = 0x0400,
    OptionalPayload = 0x0800,
    MemoryMappedPayload = 0x1000,
    Size64Bit = 0x2000,
    DuplicateNonOptionalPayload = 0x4000,
    BadDataVersion = 0x8000,
}
