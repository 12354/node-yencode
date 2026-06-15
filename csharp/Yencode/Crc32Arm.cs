using System;
using System.Runtime.CompilerServices;

#if NET8_0_OR_GREATER
using ArmCrc = System.Runtime.Intrinsics.Arm.Crc32;
#endif

namespace Yencode
{
    /// <summary>
    /// CRC-32 (IEEE / reflected 0xEDB88320) using the ARMv8 CRC32 instructions. These compute exactly
    /// the same reflected CRC as the scalar table path; eight bytes are consumed per instruction.
    /// </summary>
    internal static class Crc32Arm
    {
#if NET8_0_OR_GREATER
        internal static bool IsSupported => ArmCrc.Arm64.IsSupported;

        internal static unsafe uint Compute(ReadOnlySpan<byte> data, uint init)
        {
            uint crc = ~init;
            int n = data.Length, pos = 0;
            fixed (byte* p = data)
            {
                while (n >= 8)
                {
                    crc = ArmCrc.Arm64.ComputeCrc32(crc, Unsafe.ReadUnaligned<ulong>(p + pos));
                    pos += 8; n -= 8;
                }
                if (n >= 4)
                {
                    crc = ArmCrc.ComputeCrc32(crc, Unsafe.ReadUnaligned<uint>(p + pos));
                    pos += 4; n -= 4;
                }
                while (n-- > 0)
                    crc = ArmCrc.ComputeCrc32(crc, p[pos++]);
            }
            return ~crc;
        }
#else
        internal static bool IsSupported => false;
        internal static uint Compute(ReadOnlySpan<byte> data, uint init) => Crc32.ComputeScalar(data, init);
#endif
    }
}
