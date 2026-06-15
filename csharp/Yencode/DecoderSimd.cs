using System;

namespace Yencode
{
    // SIMD-accelerated yEnc decoder kernels live here (added incrementally).
    // Until enabled, the Try* methods return false so the scalar path is used.
    internal static class DecoderSimd
    {
        internal static IsaLevel Isa => IsaLevel.Generic;

        internal static bool TryDecodeNoEnd(bool isRaw, ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState, out int written)
        {
            written = 0;
            return false;
        }

        internal static bool TryDecodeEnd(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState, out int read, out int written, out DecodeEnd end)
        {
            read = 0; written = 0; end = DecodeEnd.None;
            return false;
        }
    }
}
