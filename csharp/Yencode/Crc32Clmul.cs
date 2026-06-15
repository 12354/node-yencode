using System;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Yencode
{
    /// <summary>
    /// Carry-less-multiply (PCLMULQDQ) accelerated CRC-32, using the classic "folding" technique.
    /// Full 16-byte blocks are folded into a single 128-bit register with one fold constant, the
    /// register is reduced to 32 bits with the standard byte table, and the &lt;16-byte tail is finished
    /// scalar. The initial CRC is XORed into the first 32 bits of data (a standard linearity trick),
    /// so no Barrett-reduction constants are needed.
    /// </summary>
    internal static class Crc32Clmul
    {
#if NET8_0_OR_GREATER
        internal static bool IsSupported => Pclmulqdq.IsSupported && Sse2.IsSupported;

        // Fold-by-128-bits constants for the reflected CRC-32 polynomial:
        //   low lane  k3 = x^128 region, high lane k4 = x^192 region.
        private const ulong K3 = 0x1751997d0UL;
        private const ulong K4 = 0x0ccaa009eUL;

        internal static unsafe uint Compute(ReadOnlySpan<byte> data, uint init)
        {
            int n = data.Length;
            if (n < 16) return Crc32.ComputeScalar(data, init);

            var bt = Crc32.ByteTable;
            int bulk = (n / 16) * 16;
            uint crc;

            fixed (byte* p = data)
            {
                var k = Vector128.Create(K3, K4);
                Vector128<ulong> x = Sse2.LoadVector128((ulong*)p);
                // mix the initial CRC into the first 32 bits of the data
                x = Sse2.Xor(x, Vector128.CreateScalar((ulong)(~init)));

                for (int pos = 16; pos < bulk; pos += 16)
                {
                    Vector128<ulong> y = Sse2.LoadVector128((ulong*)(p + pos));
                    Vector128<ulong> lo = Pclmulqdq.CarrylessMultiply(x, k, 0x00); // x.low  * k3
                    Vector128<ulong> hi = Pclmulqdq.CarrylessMultiply(x, k, 0x11); // x.high * k4
                    x = Sse2.Xor(Sse2.Xor(lo, hi), y);
                }

                // reduce the 16-byte folded register via the byte table
                byte* xb = stackalloc byte[16];
                Sse2.Store(xb, x.AsByte());
                crc = 0;
                for (int i = 0; i < 16; i++)
                    crc = (crc >> 8) ^ bt[(crc & 0xFF) ^ xb[i]];

                // finish the tail
                for (int i = bulk; i < n; i++)
                    crc = (crc >> 8) ^ bt[(crc & 0xFF) ^ p[i]];
            }
            return ~crc;
        }
#else
        internal static bool IsSupported => false;
        internal static uint Compute(ReadOnlySpan<byte> data, uint init) => Crc32.ComputeScalar(data, init);
#endif
    }
}
