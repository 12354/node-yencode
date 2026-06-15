using System;

namespace Yencode
{
    // Carry-less-multiply (PCLMULQDQ) accelerated CRC-32.
    // Until the intrinsic implementation is enabled, this advertises no support so the
    // scalar slice-by-8 path is used.
    internal static class Crc32Clmul
    {
        internal static bool IsSupported => false;

        internal static uint Compute(ReadOnlySpan<byte> data, uint init) => Crc32.ComputeScalar(data, init);
    }
}
