using System;

namespace Yencode
{
    /// <summary>
    /// Fully managed CRC-32 (IEEE / zlib, reflected polynomial 0xEDB88320) implementation,
    /// ported from node-yencode's native CRC routines. Includes the GF(2) helper operations
    /// (multiply, shift, combine, zeroes) used for yEnc post handling.
    ///
    /// A carry-less-multiply (PCLMULQDQ) accelerated path is used automatically when the CPU
    /// supports it; otherwise a slice-by-8 table implementation is used. Both produce identical
    /// results.
    /// </summary>
    public static class Crc32
    {
        internal const uint Poly = 0xEDB88320u; // reflected IEEE polynomial

        // slice-by-8 tables: Table[s*256 + b]
        private static readonly uint[] Table = BuildTable();

        // The standard byte-by-byte table (Table[0..255]); used by the PCLMULQDQ path's final reduction.
        internal static uint[] ByteTable => Table;

        private static uint[] BuildTable()
        {
            var t = new uint[8 * 256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                    crc = (crc >> 1) ^ (Poly & (uint)(-(int)(crc & 1)));
                t[i] = crc;
            }
            for (uint i = 0; i < 256; i++)
            {
                uint crc = t[i];
                for (int s = 1; s < 8; s++)
                {
                    crc = (crc >> 8) ^ t[crc & 0xFF];
                    t[s * 256 + i] = crc;
                }
            }
            return t;
        }

        /// <summary>The instruction set used for CRC computation on this machine.</summary>
        public static IsaLevel IsaLevel =>
            Crc32Clmul.IsSupported ? IsaLevel.Pclmul :
            Crc32Arm.IsSupported ? IsaLevel.ArmCrc :
            IsaLevel.Generic;

        /// <summary>
        /// Computes the CRC-32 of <paramref name="data"/>. Pass the result of a previous call
        /// as <paramref name="init"/> to continue an incremental computation.
        /// </summary>
        public static uint Compute(ReadOnlySpan<byte> data, uint init = 0)
        {
            if (Crc32Clmul.IsSupported && data.Length >= 64)
                return Crc32Clmul.Compute(data, init);
            if (Crc32Arm.IsSupported && data.Length >= 16)
                return Crc32Arm.Compute(data, init);
            return ComputeScalar(data, init);
        }

        /// <summary>Convenience overload accepting a byte array.</summary>
        public static uint Compute(byte[] data, uint init = 0) =>
            Compute(new ReadOnlySpan<byte>(data ?? Array.Empty<byte>()), init);

        internal static uint ComputeScalar(ReadOnlySpan<byte> data, uint init)
        {
            uint crc = ~init;
            var t = Table;
            int n = data.Length;
            int pos = 0;
            while (n >= 8)
            {
                uint one = crc ^ (uint)(data[pos] | data[pos + 1] << 8 | data[pos + 2] << 16 | data[pos + 3] << 24);
                uint two = (uint)(data[pos + 4] | data[pos + 5] << 8 | data[pos + 6] << 16 | data[pos + 7] << 24);
                crc = t[7 * 256 + (one & 0xFF)]
                    ^ t[6 * 256 + ((one >> 8) & 0xFF)]
                    ^ t[5 * 256 + ((one >> 16) & 0xFF)]
                    ^ t[4 * 256 + ((one >> 24) & 0xFF)]
                    ^ t[3 * 256 + (two & 0xFF)]
                    ^ t[2 * 256 + ((two >> 8) & 0xFF)]
                    ^ t[1 * 256 + ((two >> 16) & 0xFF)]
                    ^ t[0 * 256 + ((two >> 24) & 0xFF)];
                pos += 8;
                n -= 8;
            }
            while (n-- > 0)
                crc = (crc >> 8) ^ t[(crc & 0xFF) ^ data[pos++]];
            return ~crc;
        }

        // ---- GF(2) helpers (pre-computed powers of 2 in the CRC field) ----

        private static readonly uint[] CrcPower =
        {
            0x40000000, 0x20000000, 0x08000000, 0x00800000, 0x00008000, 0xedb88320, 0xb1e6b092, 0xa06a2517,
            0xed627dae, 0x88d14467, 0xd7bbfe6a, 0xec447f11, 0x8e7ea170, 0x6427800e, 0x4d47bae0, 0x09fe548f,
            0x83852d0f, 0x30362f1a, 0x7b5a9cc3, 0x31fec169, 0x9fec022a, 0x6c8dedc4, 0x15d6874d, 0x5fde7a4e,
            0xbad90e37, 0x2e4e5eef, 0x4eaba214, 0xa8a472c0, 0x429a969e, 0x148d302a, 0xc40ba6d0, 0xc4e22c3c
        };

        // computes n mod (2^32 - 1) (almost), via bit-hacks
        private static uint PowMod(ulong n)
        {
            n = (n >> 32) + (n & 0xffffffff);
            n += n >> 32;
            return (uint)n;
        }

        // computes PowMod(n*8) avoiding overflow == rotl(PowMod(n), 3)
        private static uint BytePow(ulong n)
        {
            uint res = PowMod(n);
            return (res << 3) | (res >> 29);
        }

        /// <summary>Returns the product of <paramref name="a"/> and <paramref name="b"/> in the CRC32 field.</summary>
        public static uint Multiply(uint a, uint b)
        {
            uint res = 0;
            for (int i = 0; i < 31; i++)
            {
                res ^= (0u - (b >> 31)) & a;
                a = (a >> 1) ^ (Poly & (0u - (a & 1)));
                b <<= 1;
            }
            res ^= (0u - (b >> 31)) & a;
            return res;
        }

        private static uint ShiftInternal(uint crc, uint n)
        {
            uint result = crc;
            uint power = 0;
            while (n != 0)
            {
                if ((n & 1) != 0)
                    result = Multiply(result, CrcPower[power]);
                n >>= 1;
                power++;
            }
            return result;
        }

        private static uint ZerosInternal(uint crc, ulong len) => ~ShiftInternal(~crc, BytePow(len));
        private static uint UnzeroInternal(uint crc, ulong len) => ~ShiftInternal(~crc, ~BytePow(len));

        /// <summary>
        /// Combines two CRC32s: returns <c>crc32(a + b)</c> given <c>crc32(a)</c>=<paramref name="crc1"/>,
        /// <c>crc32(b)</c>=<paramref name="crc2"/> and the length of <c>b</c>=<paramref name="len2"/>.
        /// </summary>
        public static uint Combine(uint crc1, uint crc2, long len2) =>
            ShiftInternal(crc1, BytePow((ulong)len2)) ^ crc2;

        /// <summary>
        /// Computes the CRC32 of a run of <paramref name="length"/> null bytes appended to
        /// <paramref name="init"/>. If <paramref name="length"/> is negative, removes that many
        /// trailing null bytes instead.
        /// </summary>
        public static uint Zeroes(long length, uint init = 0)
        {
            if (length < 0)
                return UnzeroInternal(init, (ulong)(-length));
            return ZerosInternal(init, (ulong)length);
        }

        /// <summary>
        /// Returns 2^<paramref name="n"/> in the CRC32 field if <paramref name="crc"/> is left at its
        /// default; otherwise shifts <paramref name="crc"/> forward by <paramref name="n"/> bits.
        /// <paramref name="n"/> may be negative.
        /// </summary>
        public static uint Shift(long n, uint crc = 0x80000000)
        {
            if (n < 0)
                return ShiftInternal(crc, ~PowMod((ulong)(-n)));
            return ShiftInternal(crc, PowMod((ulong)n));
        }

        // ---- byte conversions (big-endian, matching the original module's Buffer representation) ----

        /// <summary>Converts a CRC value to its 4-byte big-endian representation.</summary>
        public static byte[] ToBytes(uint crc) => new[]
        {
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc
        };

        /// <summary>Reads a 4-byte big-endian CRC value.</summary>
        public static uint FromBytes(ReadOnlySpan<byte> b) =>
            ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];

        /// <summary>Formats a CRC as the lower-case 8-digit hex string used in yEnc trailers.</summary>
        public static string ToHex(uint crc) => crc.ToString("x8");
    }
}
