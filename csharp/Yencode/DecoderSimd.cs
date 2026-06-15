using System;

namespace Yencode
{
    /// <summary>
    /// SIMD-accelerated yEnc decoder. The buffer is processed in vector-sized blocks: a block that
    /// contains none of the special bytes ('=', '\r', '\n') and starts from a clean decoder state is
    /// decoded with a single packed subtract (-42) and vector store. Any other block is handed to the
    /// scalar incremental decoder with state threaded across blocks, so all the tricky escape /
    /// dot-unstuffing / end-detection edge cases (including ones spanning block boundaries) are handled
    /// by the proven scalar code. Output is identical to the scalar decoders.
    /// Works on x86 (SSE2/AVX2) and ARM (AdvSimd/NEON).
    /// </summary>
    internal static class DecoderSimd
    {
#if NET8_0_OR_GREATER
        internal static IsaLevel Isa => SimdHelpers.Isa;

        private const int MinLen = 32;

        internal static bool TryDecodeNoEnd(bool isRaw, ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState, out int written)
        {
            if (!SimdHelpers.Supported || src.Length < MinLen) { written = 0; return false; }
            written = DecodeNoEnd(isRaw, src, dest, ref state);
            return true;
        }

        internal static bool TryDecodeEnd(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState, out int read, out int written, out DecodeEnd end)
        {
            if (!SimdHelpers.Supported || src.Length < MinLen) { read = 0; written = 0; end = DecodeEnd.None; return false; }
            end = RunEnd(src, dest, ref state, out read, out written);
            return true;
        }

        private static unsafe int DecodeNoEnd(bool isRaw, ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state)
        {
            int vw = SimdHelpers.VectorWidth;
            int n = src.Length, i = 0, o = 0;
            fixed (byte* sp = src)
            fixed (byte* dp = dest)
            {
                while (i < n)
                {
                    int remain = n - i;
                    if (state == DecoderState.None && remain >= vw && SimdHelpers.DecoderClean(sp + i, vw))
                    {
                        SimdHelpers.DecoderSub42(sp + i, dp + o, vw);
                        i += vw; o += vw;
                        continue;
                    }
                    int blk = Math.Min(vw, remain);
                    int w = isRaw
                        ? Yenc.DecodeNoEndRaw(src.Slice(i, blk), dest.Slice(o), ref state, true)
                        : Yenc.DecodeNoEndPlain(src.Slice(i, blk), dest.Slice(o), ref state, true);
                    o += w; i += blk;
                }
            }
            return o;
        }

        private static unsafe DecodeEnd RunEnd(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, out int read, out int written)
        {
            int vw = SimdHelpers.VectorWidth;
            int n = src.Length, i = 0, o = 0;
            fixed (byte* sp = src)
            fixed (byte* dp = dest)
            {
                while (i < n)
                {
                    int remain = n - i;
                    if (state == DecoderState.None && remain >= vw && SimdHelpers.DecoderClean(sp + i, vw))
                    {
                        SimdHelpers.DecoderSub42(sp + i, dp + o, vw);
                        i += vw; o += vw;
                        continue;
                    }
                    int blk = Math.Min(vw, remain);
                    var e = Yenc.DecodeEndRawScalar(src.Slice(i, blk), dest.Slice(o), ref state, true, out int rd, out int wr);
                    o += wr;
                    if (e != DecodeEnd.None)
                    {
                        read = i + rd; written = o;
                        return e;
                    }
                    i += blk; // end==None consumes the whole block
                }
            }
            read = n; written = o;
            return DecodeEnd.None;
        }
#else
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
#endif
    }
}
