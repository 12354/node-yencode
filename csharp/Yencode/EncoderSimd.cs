using System;

namespace Yencode
{
    // SIMD-accelerated yEnc encoder kernels live here (added incrementally).
    // Until enabled, TryEncode returns false so the scalar path is used.
    internal static class EncoderSimd
    {
        internal static IsaLevel Isa => IsaLevel.Generic;

        internal static bool TryEncode(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd, out int written)
        {
            written = 0;
            return false;
        }
    }
}
