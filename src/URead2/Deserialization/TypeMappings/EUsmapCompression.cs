namespace URead2.Deserialization.TypeMappings;

/// <summary>
/// .usmap compression methods.
/// </summary>
public enum EUsmapCompression : byte
{
    None = 0,
    Oodle = 1,
    Brotli = 2,
    ZStandard = 3
}
