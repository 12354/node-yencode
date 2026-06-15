using System;

namespace Yencode
{
    /// <summary>
    /// SIMD-accelerated yEnc encoder. Mirrors the scalar algorithm exactly but uses a vectorized
    /// fast path for the common case: runs of non-"critical" bytes in the middle of a line, where
    /// no escaping or line-break can occur. Such runs are emitted with a single packed add (+42) and
    /// vector store; everything else (line edges, escapes, the first char of each line) falls back to
    /// the same per-character logic the scalar encoder uses. Output is byte-for-byte identical to
    /// <see cref="Yenc.EncodeScalar"/>. Works on x86 (SSE2/AVX2) and ARM (AdvSimd/NEON).
    /// </summary>
    internal static class EncoderSimd
    {
#if NET8_0_OR_GREATER
        internal static IsaLevel Isa => SimdHelpers.Isa;

        internal static bool TryEncode(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd, out int written)
        {
            int vw = SimdHelpers.VectorWidth;
            if (!SimdHelpers.Supported || lineSize < vw + 2 || src.Length < vw)
            {
                written = 0;
                return false;
            }
            written = Encode(lineSize, ref col, src, dest, doEnd, vw);
            return true;
        }

        private static unsafe int Encode(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd, int vw)
        {
            int n = src.Length, i = 0, o = 0;
            fixed (byte* sp = src)
            fixed (byte* dp = dest)
            {
                while (i < n)
                {
                    if (col >= 1 && col + vw <= lineSize - 1 && n - i >= vw
                        && SimdHelpers.EncoderCleanAdd42(sp + i, dp + o, vw))
                    {
                        i += vw; o += vw; col += vw;
                        continue;
                    }

                    // per-character fallback (identical semantics to the scalar reference encoder)
                    int c = (sp[i] + 42) & 0xFF;
                    bool esc;
                    if (c == 0 || c == 13 || c == 10 || c == 61) esc = true;
                    else if (c == '\t' || c == ' ') esc = !(col > 0 && col < lineSize - 1);
                    else if (c == '.') esc = col == 0;
                    else esc = false;

                    if (esc) { dp[o++] = (byte)'='; c = (c + 64) & 0xFF; col++; }
                    dp[o++] = (byte)c; col++;
                    if (col >= lineSize) { dp[o++] = (byte)'\r'; dp[o++] = (byte)'\n'; col = 0; }
                    i++;
                }

                if (doEnd)
                {
                    if (o >= 2 && dp[o - 1] == '\n' && dp[o - 2] == '\r') o -= 2;
                    if (o >= 1 && (dp[o - 1] == '\t' || dp[o - 1] == ' '))
                    {
                        int c = dp[o - 1];
                        o--;
                        dp[o++] = (byte)'=';
                        dp[o++] = (byte)((c + 64) & 0xFF);
                        col++;
                    }
                }
            }
            return o;
        }
#else
        internal static IsaLevel Isa => IsaLevel.Generic;

        internal static bool TryEncode(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd, out int written)
        {
            written = 0;
            return false;
        }
#endif
    }
}
