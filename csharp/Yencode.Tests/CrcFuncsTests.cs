using System;
using Xunit;

namespace Yencode.Tests
{
    /// <summary>
    /// Port of the original repo's standalone C test (test/testcrcfuncs.c), which exercises the
    /// low-level GF(2) CRC helpers: crc32_bytepow and crc32_mul2pow (== a raw crc32_shift).
    /// </summary>
    public class CrcFuncsTests
    {
        // crc32_mul2pow(n, x) in the original == shift x forward by n bits == Crc32.ShiftInternal(x, n)
        private static uint Mul2Pow(uint n, uint x) => Crc32.ShiftInternal(x, n);

        [Fact]
        public void BytePow_BoundaryVectors()
        {
            Assert.Equal(0u, Crc32.BytePow(0));
            Assert.Equal(8u, Crc32.BytePow(1));
            Assert.Equal(16u, Crc32.BytePow(2));
            Assert.Equal(0xfffffff8u, Crc32.BytePow((1u << 29) - 1));
            Assert.Equal(1u, Crc32.BytePow(1u << 29));
            Assert.Equal(9u, Crc32.BytePow((1u << 29) + 1));

            Assert.Equal(0x80000000u, Crc32.BytePow(1UL << 60));
            Assert.Equal(1u, Crc32.BytePow(1UL << 61));
            Assert.Equal(2u, Crc32.BytePow(1UL << 62));
            Assert.Equal(4u, Crc32.BytePow(1UL << 63));

            uint actual = Crc32.BytePow(~0UL);
            Assert.True(actual == 0 || actual == 0xffffffff);
            Assert.Equal(0xfffffff7u, Crc32.BytePow(~0UL - 1));
            Assert.Equal(0xfffffffbu, Crc32.BytePow((1UL << 63) - 1));
            Assert.Equal(12u, Crc32.BytePow((1UL << 63) + 1));
        }

        [Fact]
        public void BytePow_AgainstReference()
        {
            // Reference: (i*8) mod (2^32-1), with the same 0/0xffffffff ambiguity the C test allows.
            // Reduce i first so i*8 can't overflow for full 64-bit inputs.
            void Check(ulong i)
            {
                ulong m = i % 0xffffffffUL;
                uint refv = (uint)((m * 8UL) % 0xffffffffUL);
                uint actual = Crc32.BytePow(i);
                if (refv == 0)
                    Assert.True(actual == 0 || actual == 0xffffffff, "i=" + i);
                else
                    Assert.Equal(refv, actual);
            }

            // dense around the boundaries the C test cares about
            for (ulong i = (1UL << 29) - 1024; i < (1UL << 29) + 1024; i++) Check(i);
            for (ulong i = (1UL << 32) - 1024; i < (1UL << 32) + 512; i++) Check(i);
            for (ulong i = 0; i < 4096; i++) Check(i);

            // broad strided sweep across the whole 32-bit-ish domain (the C test does this exhaustively;
            // a stride keeps it fast while still covering the bit-hack's carry cases)
            for (ulong i = 1UL << 29; i < (1UL << 32) + 256; i += 1009) Check(i);

            // a handful of random samples across the full 64-bit input range
            var rng = new Random(20260615);
            for (int k = 0; k < 200000; k++)
            {
                ulong i = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                Check(i);
            }
        }

        [Fact]
        public void Mul2Pow_Vectors()
        {
            Assert.Equal(0u, Mul2Pow(0, 0));
            Assert.Equal(1u, Mul2Pow(0, 1));
            Assert.Equal(0u, Mul2Pow(1, 0));
            Assert.Equal(0x40000000u, Mul2Pow(1, 0x80000000));
            Assert.Equal(0x20000000u, Mul2Pow(1, 0x40000000));
            Assert.Equal(0x08000000u, Mul2Pow(4, 0x80000000));
            Assert.Equal(0x04000000u, Mul2Pow(5, 0x80000000));
            Assert.Equal(123u, Mul2Pow(0xffffffff, 123));
        }
    }
}
