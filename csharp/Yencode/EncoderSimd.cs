using System;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Yencode
{
    /// <summary>
    /// SIMD-accelerated yEnc encoder. Mirrors the scalar algorithm exactly but uses a vectorized
    /// fast path for the common case: runs of non-"critical" bytes in the middle of a line, where
    /// no escaping or line-break can occur. Such runs are emitted with a single packed add (+42) and
    /// 16/32-byte store; everything else (line edges, escapes, the first char of each line) falls back
    /// to the same per-character logic the scalar encoder uses. Output is byte-for-byte identical to
    /// <see cref="Yenc.EncodeScalar"/>.
    /// </summary>
    internal static class EncoderSimd
    {
#if NET8_0_OR_GREATER
        internal static IsaLevel Isa =>
            Avx2.IsSupported ? IsaLevel.Avx2 :
            Sse2.IsSupported ? IsaLevel.Sse2 :
            IsaLevel.Generic;

        private static bool Supported => Sse2.IsSupported;

        internal static bool TryEncode(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd, out int written)
        {
            int vw = Avx2.IsSupported ? 32 : 16;
            // need room for at least one vector in the middle of a line, and a worthwhile amount of data
            if (!Supported || lineSize < vw + 2 || src.Length < vw)
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
                var add42_128 = Vector128.Create((byte)42);
                var add42_256 = Vector256.Create((byte)42);
                var c0 = Vector128.Create((byte)0);
                var c13 = Vector128.Create((byte)13);
                var c10 = Vector128.Create((byte)10);
                var c61 = Vector128.Create((byte)61);
                var c0b = Vector256.Create((byte)0);
                var c13b = Vector256.Create((byte)13);
                var c10b = Vector256.Create((byte)10);
                var c61b = Vector256.Create((byte)61);

                while (i < n)
                {
                    if (col >= 1 && col + vw <= lineSize - 1 && n - i >= vw)
                    {
                        if (vw == 32)
                        {
                            var v = Avx.LoadVector256(sp + i);
                            var a = Avx2.Add(v, add42_256);
                            var m = Avx2.Or(Avx2.Or(Avx2.CompareEqual(a, c0b), Avx2.CompareEqual(a, c13b)),
                                            Avx2.Or(Avx2.CompareEqual(a, c10b), Avx2.CompareEqual(a, c61b)));
                            if (Avx2.MoveMask(m) == 0)
                            {
                                Avx.Store(dp + o, a);
                                i += 32; o += 32; col += 32;
                                continue;
                            }
                        }
                        else
                        {
                            var v = Sse2.LoadVector128(sp + i);
                            var a = Sse2.Add(v, add42_128);
                            var m = Sse2.Or(Sse2.Or(Sse2.CompareEqual(a, c0), Sse2.CompareEqual(a, c13)),
                                            Sse2.Or(Sse2.CompareEqual(a, c10), Sse2.CompareEqual(a, c61)));
                            if (Sse2.MoveMask(m) == 0)
                            {
                                Sse2.Store(dp + o, a);
                                i += 16; o += 16; col += 16;
                                continue;
                            }
                        }
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
