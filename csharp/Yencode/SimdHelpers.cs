using System;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
#endif

namespace Yencode
{
#if NET8_0_OR_GREATER
    /// <summary>
    /// Small architecture-specific vector helpers shared by the SIMD encoder and decoder.
    /// Supports x86 (SSE2 16-byte / AVX2 32-byte) and ARM (AdvSimd/NEON 16-byte).
    /// </summary>
    internal static class SimdHelpers
    {
        internal static bool Supported => Sse2.IsSupported || AdvSimd.Arm64.IsSupported;

        // Test-only instrumentation: when Probe is set, the vector helpers count how many times their
        // vector instruction sequences actually execute. Off (and effectively free) in normal use.
        internal static bool Probe;
        internal static long EncoderVectorBlocks;
        internal static long DecoderVectorBlocks;

        // 16 on SSE2/NEON, 32 on AVX2
        internal static int VectorWidth => Avx2.IsSupported ? 32 : 16;

        internal static IsaLevel Isa =>
            Avx2.IsSupported ? IsaLevel.Avx2 :
            Sse2.IsSupported ? IsaLevel.Sse2 :
            AdvSimd.Arm64.IsSupported ? IsaLevel.Neon :
            IsaLevel.Generic;

        // ---- decoder helpers ----

        /// <summary>True if the block contains none of the decoder's special bytes ('=', '\r', '\n').</summary>
        internal static unsafe bool DecoderClean(byte* s, int vw)
        {
            if (Probe) DecoderVectorBlocks++;
            if (vw == 32)
            {
                var v = Avx.LoadVector256(s);
                var m = Avx2.Or(Avx2.Or(Avx2.CompareEqual(v, Vector256.Create((byte)0x3d)),
                                        Avx2.CompareEqual(v, Vector256.Create((byte)0x0d))),
                                Avx2.CompareEqual(v, Vector256.Create((byte)0x0a)));
                return Avx2.MoveMask(m) == 0;
            }
            if (Sse2.IsSupported)
            {
                var v = Sse2.LoadVector128(s);
                var m = Sse2.Or(Sse2.Or(Sse2.CompareEqual(v, Vector128.Create((byte)0x3d)),
                                        Sse2.CompareEqual(v, Vector128.Create((byte)0x0d))),
                                Sse2.CompareEqual(v, Vector128.Create((byte)0x0a)));
                return Sse2.MoveMask(m) == 0;
            }
            else
            {
                var v = AdvSimd.LoadVector128(s);
                var m = AdvSimd.Or(AdvSimd.Or(AdvSimd.CompareEqual(v, Vector128.Create((byte)0x3d)),
                                              AdvSimd.CompareEqual(v, Vector128.Create((byte)0x0d))),
                                   AdvSimd.CompareEqual(v, Vector128.Create((byte)0x0a)));
                return AdvSimd.Arm64.MaxAcross(m).ToScalar() == 0;
            }
        }

        /// <summary>Stores (block - 42) for vw bytes.</summary>
        internal static unsafe void DecoderSub42(byte* s, byte* d, int vw)
        {
            if (vw == 32)
                Avx.Store(d, Avx2.Subtract(Avx.LoadVector256(s), Vector256.Create((byte)42)));
            else if (Sse2.IsSupported)
                Sse2.Store(d, Sse2.Subtract(Sse2.LoadVector128(s), Vector128.Create((byte)42)));
            else
                AdvSimd.Store(d, AdvSimd.Subtract(AdvSimd.LoadVector128(s), Vector128.Create((byte)42)));
        }

        // ---- encoder helper ----

        /// <summary>
        /// If the block, after adding 42, contains no "critical" byte (0, '\r', '\n', '='), stores the
        /// added result to <paramref name="d"/> and returns true; otherwise returns false (no store).
        /// </summary>
        internal static unsafe bool EncoderCleanAdd42(byte* s, byte* d, int vw)
        {
            if (Probe) EncoderVectorBlocks++;
            if (vw == 32)
            {
                var a = Avx2.Add(Avx.LoadVector256(s), Vector256.Create((byte)42));
                var m = Avx2.Or(Avx2.Or(Avx2.CompareEqual(a, Vector256.Create((byte)0)),
                                        Avx2.CompareEqual(a, Vector256.Create((byte)13))),
                                Avx2.Or(Avx2.CompareEqual(a, Vector256.Create((byte)10)),
                                        Avx2.CompareEqual(a, Vector256.Create((byte)61))));
                if (Avx2.MoveMask(m) != 0) return false;
                Avx.Store(d, a);
                return true;
            }
            if (Sse2.IsSupported)
            {
                var a = Sse2.Add(Sse2.LoadVector128(s), Vector128.Create((byte)42));
                var m = Sse2.Or(Sse2.Or(Sse2.CompareEqual(a, Vector128.Create((byte)0)),
                                        Sse2.CompareEqual(a, Vector128.Create((byte)13))),
                                Sse2.Or(Sse2.CompareEqual(a, Vector128.Create((byte)10)),
                                        Sse2.CompareEqual(a, Vector128.Create((byte)61))));
                if (Sse2.MoveMask(m) != 0) return false;
                Sse2.Store(d, a);
                return true;
            }
            else
            {
                var a = AdvSimd.Add(AdvSimd.LoadVector128(s), Vector128.Create((byte)42));
                var m = AdvSimd.Or(AdvSimd.Or(AdvSimd.CompareEqual(a, Vector128.Create((byte)0)),
                                              AdvSimd.CompareEqual(a, Vector128.Create((byte)13))),
                                   AdvSimd.Or(AdvSimd.CompareEqual(a, Vector128.Create((byte)10)),
                                              AdvSimd.CompareEqual(a, Vector128.Create((byte)61))));
                if (AdvSimd.Arm64.MaxAcross(m).ToScalar() != 0) return false;
                AdvSimd.Store(d, a);
                return true;
            }
        }
    }
#endif
}
