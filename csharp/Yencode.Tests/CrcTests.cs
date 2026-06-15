using System;
using Xunit;

namespace Yencode.Tests
{
    public class CrcTests
    {
        private static byte[] Ascii(string s) { var b = new byte[s.Length]; for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i]; return b; }
        private static string Hex(uint c) => c.ToString("x8");

        [Fact]
        public void BasicVectors()
        {
            Assert.Equal(RefHex(Ascii("")), Hex(Yenc_Crc(Ascii(""))));
            Assert.Equal(RefHex(Ascii("z")), Hex(Yenc_Crc(Ascii("z"))));
            Assert.Equal(RefHex(Ascii("aabbcc")), Hex(Yenc_Crc(Ascii("aabbcc"))));
        }

        private static uint Yenc_Crc(byte[] b, uint init = 0) => Crc32.Compute(b, init);
        private static string RefHex(byte[] b) => Reference.Crc32(b).ToString("x8");

        [Fact]
        public void IncrementalAndCombine()
        {
            // Join (incremental continue)
            Assert.Equal(RefHex(Ascii("aabbcc")), Hex(Yenc_Crc(Ascii("cc"), Yenc_Crc(Ascii("aabb")))));
            Assert.Equal(RefHex(Ascii("123456789012")), Hex(Yenc_Crc(Ascii("789012"), Yenc_Crc(Ascii("123456")))));

            // Combine
            Assert.Equal(RefHex(Ascii("aabbcc")), Hex(Crc32.Combine(Yenc_Crc(Ascii("aabb")), Reference.Crc32(Ascii("cc")), 2)));
            Assert.Equal(RefHex(Ascii("123456789012")), Hex(Crc32.Combine(Yenc_Crc(Ascii("123456")), Reference.Crc32(Ascii("789012")), 6)));

            // empties
            Assert.Equal(RefHex(Ascii("z")), Hex(Yenc_Crc(Ascii(""), Yenc_Crc(Ascii("z")))));
            Assert.Equal(RefHex(Ascii("z")), Hex(Yenc_Crc(Ascii("z"), Yenc_Crc(Ascii("")))));
            Assert.Equal(RefHex(Ascii("")), Hex(Yenc_Crc(Ascii(""), Yenc_Crc(Ascii("")))));
            Assert.Equal(RefHex(Ascii("z")), Hex(Crc32.Combine(Yenc_Crc(Ascii("")), Yenc_Crc(Ascii("z")), 1)));
            Assert.Equal(RefHex(Ascii("z")), Hex(Crc32.Combine(Yenc_Crc(Ascii("z")), Yenc_Crc(Ascii("")), 0)));
            Assert.Equal(RefHex(Ascii("")), Hex(Crc32.Combine(Yenc_Crc(Ascii("")), Yenc_Crc(Ascii("")), 0)));
        }

        [Fact]
        public void Zeroes()
        {
            Assert.Equal("00000000", Hex(Crc32.Zeroes(0)));
            Assert.Equal("d202ef8d", Hex(Crc32.Zeroes(1)));
            Assert.Equal("2144df1c", Hex(Crc32.Zeroes(4)));

            Assert.Equal("00000000", Hex(Crc32.Zeroes(0, Yenc_Crc(Ascii("")))));
            Assert.Equal("d202ef8d", Hex(Crc32.Zeroes(1, Yenc_Crc(Ascii("")))));
            Assert.Equal(RefHex(Ascii("z")), Hex(Crc32.Zeroes(0, Yenc_Crc(Ascii("z")))));
            Assert.Equal(RefHex(Ascii("z\0\0\0\0")), Hex(Crc32.Zeroes(4, Yenc_Crc(Ascii("z")))));

            Assert.Equal("00000000", Hex(Crc32.Zeroes(4, Crc32.Zeroes(-4))));
            Assert.Equal("00000000", Hex(Crc32.Zeroes(-4, Crc32.Zeroes(4))));
            Assert.Equal(RefHex(Ascii("z\0\0\0\0")), Hex(Crc32.Zeroes(6, Crc32.Zeroes(-2, Yenc_Crc(Ascii("z"))))));
        }

        [Fact]
        public void Multiply()
        {
            Assert.Equal("00000000", Hex(Crc32.Multiply(0x01020304, Yenc_Crc(Ascii("")))));
            Assert.Equal("80000000", Hex(Crc32.Multiply(0x80000000, 0x80000000)));
            Assert.Equal("81e243a3", Hex(Crc32.Multiply(0x01020304, 0x05060708)));
        }

        [Fact]
        public void Shift()
        {
            Assert.Equal("80000000", Hex(Crc32.Shift(0)));
            Assert.Equal("40000000", Hex(Crc32.Shift(1)));
            Assert.Equal("20000000", Hex(Crc32.Shift(2)));
            Assert.Equal("db710641", Hex(Crc32.Shift(-1)));
            Assert.Equal("80000000", Hex(Crc32.Shift(-1, Crc32.Shift(1))));
            Assert.Equal("20000000", Hex(Crc32.Shift(4, Crc32.Shift(-2))));
        }

        [Fact]
        public void RandomShortBuffers()
        {
            var rng = new Random(7);
            for (int i = 1; i < 128; i++)
            {
                var b = new byte[i];
                rng.NextBytes(b);
                Assert.Equal(Reference.Crc32(b).ToString("x8"), Hex(Yenc_Crc(b)));
            }
        }

        [Fact]
        public void RandomLargeBuffers_AndContinue()
        {
            var rng = new Random(8);
            for (int i = 0; i < 8; i++)
            {
                var b = new byte[100000];
                rng.NextBytes(b);
                Assert.Equal(Reference.Crc32(b).ToString("x8"), Hex(Yenc_Crc(b)));

                int split = rng.Next(b.Length);
                var head = new byte[split];
                var tail = new byte[b.Length - split];
                Array.Copy(b, 0, head, 0, split);
                Array.Copy(b, split, tail, 0, tail.Length);
                Assert.Equal(Reference.Crc32(b).ToString("x8"), Hex(Yenc_Crc(tail, Yenc_Crc(head))));
            }
        }

        [Fact]
        public void ByteConversions()
        {
            uint c = 0x515ad3cc;
            var bytes = Crc32.ToBytes(c);
            Assert.Equal(new byte[] { 0x51, 0x5a, 0xd3, 0xcc }, bytes);
            Assert.Equal(c, Crc32.FromBytes(bytes));
            Assert.Equal("515ad3cc", Crc32.ToHex(c));
        }
    }
}
