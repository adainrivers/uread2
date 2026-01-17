namespace URead2.IO;

public unsafe class SpanStream : UnmanagedMemoryStream
{
    private readonly ReadOnlySpan<byte> _span;

    public SpanStream(ReadOnlySpan<byte> span)
    {
        _span = span;
        fixed (byte* p = span)
        {
            Initialize(p, span.Length);
        }
    }
}