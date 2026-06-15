using System;
using System.Collections.Generic;
using Xunit;

namespace Yencode.Tests
{
    public class EncoderTests
    {
        private static readonly int[] LineSizes = BuildLineSizes();

        private static int[] BuildLineSizes()
        {
            var list = new List<int>();
            void Range(int a, int b) { for (int i = a; i < b; i++) list.Add(i); }
            Range(1, 18); Range(23, 26); Range(30, 35); Range(46, 51); Range(62, 67); Range(126, 131);
            list.Add(136); list.Add(145); list.Add(159); Range(254, 259);
            return list.ToArray();
        }

        private static void ForEachLineSize(Action<int, int> fn)
        {
            foreach (int ls in LineSizes)
                foreach (int offs in LineSizes)
                    if (offs < ls) fn(ls, offs);
        }

        private static string Hex(byte[] b) => Reference.Hex(b);

        private static void DoTest(byte[] data, int lineSize, int col, byte[] expected = null)
        {
            expected ??= Reference.Encode(data, lineSize, col);
            string exp = Hex(expected);

            byte[] actual = Yenc.Encode(data, lineSize, col);
            Assert.Equal(exp, Hex(actual));

            var buf = new byte[Yenc.MaxSize(data.Length, lineSize)];
            int len = Yenc.EncodeTo(data, buf, lineSize, col);
            Assert.Equal(exp, Hex(Slice(buf, 0, len)));
        }

        private static byte[] Slice(byte[] a, int start, int len)
        {
            var r = new byte[len];
            Array.Copy(a, start, r, 0, len);
            return r;
        }

        private static byte[] Fill(int n, byte v)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = v;
            return b;
        }

        [Fact]
        public void Empty()
        {
            Assert.Equal("", Hex(Yenc.Encode(Array.Empty<byte>())));
        }

        [Fact]
        public void FixedPatterns_AllLineSizes()
        {
            ForEachLineSize((ls, offs) =>
            {
                DoTest(Fill(256, 0), ls, offs);     // long no escaping
                DoTest(Fill(256, 227), ls, offs);   // long all escaping
                DoTest(Fill(256, 4), ls, offs);     // long all dots
                DoTest(Fill(256, 223), ls, offs);   // long all tabs
            });
        }

        [Fact]
        public void CaseTests()
        {
            DoTest(new byte[] { 0, 1, 2, 3, 224, 4 }, 128, 0);
            DoTest(new byte[] { 223, 223, 223 }, 128, 0);
            DoTest(new byte[] { 4, 3, 224, 2, 1, 0 }, 128, 0);
            DoTest(new byte[] { 0, 1, 2, 3, 4 }, 2, 0);
            DoTest(new byte[] { 0, 1, 224, 3, 4 }, 2, 0);
            DoTest(new byte[] { 0, 1, 2, 3, 4 }, 2, 1);
            DoTest(new byte[] { 0, 1, 224, 3, 4 }, 2, 1);
            // explicit expected: tab & lf around line break
            DoTest(new byte[] { 223, 224 }, 128, 127, HexBytes("3d490d0a3d4a"));
        }

        [Fact]
        public void CaseTests_WithPadding()
        {
            int[] padLens = BuildPadLens();
            byte[][] payloads =
            {
                new byte[] { 0, 1, 2, 3, 224, 4 },
                new byte[] { 223, 223, 223 },
                new byte[] { 4, 3, 224, 2, 1, 0 },
            };
            var padding = Fill(128, 97);
            foreach (var payload in payloads)
            {
                foreach (int pre in padLens)
                    foreach (int post in padLens)
                    {
                        var buf = Concat(Slice(padding, 0, pre), payload, Slice(padding, 0, post));
                        DoTest(buf, 128, 0);
                    }
            }
        }

        private static int[] BuildPadLens()
        {
            var list = new List<int>();
            for (int i = 0; i < 35; i++) list.Add(i);
            for (int i = 46; i < 51; i++) list.Add(i);
            for (int i = 62; i < 67; i++) list.Add(i);
            return list.ToArray();
        }

        [Fact]
        public void RandomData_AllLineSizes()
        {
            var rng = new Random(12345);
            for (int round = 0; round < 4; round++)
            {
                var data = new byte[4096];
                rng.NextBytes(data);
                ForEachLineSize((ls, offs) => DoTest(data, ls, offs));
            }
        }

        [Fact]
        public void TargetedRandomData_AllLineSizes()
        {
            byte[] charset = { (byte)'a', 0xd6, 0xdf, 0xe0, 0xe3, 0xf6, 0x04, 0x13 };
            var rng = new Random(67890);
            for (int round = 0; round < 4; round++)
            {
                var data = new byte[2048];
                for (int i = 0; i < data.Length; i++) data[i] = charset[rng.Next(charset.Length)];
                ForEachLineSize((ls, offs) => DoTest(data, ls, offs));
            }
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var r = new byte[n];
            int o = 0;
            foreach (var p in parts) { Array.Copy(p, 0, r, o, p.Length); o += p.Length; }
            return r;
        }

        private static byte[] HexBytes(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
