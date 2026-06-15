using System;
using System.Text;
using Xunit;

namespace Yencode.Tests
{
    public class DecoderTests
    {
        private static string Hex(byte[] b) => Reference.Hex(b);
        private static byte[] Ascii(string s) { var b = new byte[s.Length]; for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i]; return b; }
        private static byte[] Slice(byte[] a, int len) { var r = new byte[len]; Array.Copy(a, r, len); return r; }
        private static byte[] Fill(int n, byte v) { var b = new byte[n]; for (int i = 0; i < n; i++) b[i] = v; return b; }
        private static byte[] Concat(byte[] a, byte[] b, byte[] c)
        {
            var r = new byte[a.Length + b.Length + c.Length];
            Array.Copy(a, 0, r, 0, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            Array.Copy(c, 0, r, a.Length + b.Length, c.Length);
            return r;
        }

        private enum Mode { Nntp, Plain, NntpEnd }

        private static byte[] RefDecode(Mode m, byte[] td) => m switch
        {
            Mode.Nntp => Reference.DecodeRaw(td, false),
            Mode.Plain => Reference.Decode(td, false),
            _ => Reference.DecodeRaw(td, true),
        };

        private static byte[] ActualDecode(Mode m, byte[] td)
        {
            switch (m)
            {
                case Mode.Nntp: return Yenc.Decode(td, true);
                case Mode.Plain: return Yenc.Decode(td, false);
                default: return Yenc.DecodeChunk(td).Output;
            }
        }

        private static byte[] ActualDecodeInSitu(Mode m, byte[] td)
        {
            var b = (byte[])td.Clone();
            int len;
            switch (m)
            {
                case Mode.Nntp: len = Yenc.DecodeTo(b, b, true); break;
                case Mode.Plain: len = Yenc.DecodeTo(b, b, false); break;
                default:
                    if (b.Length == 0) { len = 0; break; }
                    var state = DecoderState.Crlf;
                    Yenc.DecodeIncremental(b, b, ref state, out _, out len);
                    break;
            }
            return Slice(b, len);
        }

        private static void DoTestRef(byte[] data)
        {
            int prepad = 48, postpad = 48;
            if (data.Length > 1024) { prepad = 1; postpad = 1; }
            for (int i = 0; i < prepad; i++)
            {
                var pre = Fill(i, 1);
                for (int j = 0; j < postpad; j++)
                {
                    var post = Fill(j, 1);
                    var td = Concat(pre, data, post);
                    foreach (Mode m in new[] { Mode.Nntp, Mode.Plain, Mode.NntpEnd })
                    {
                        string exp = Hex(RefDecode(m, td));
                        Assert.Equal(exp, Hex(ActualDecode(m, td)));
                        Assert.Equal(exp, Hex(ActualDecodeInSitu(m, td)));
                    }
                }
            }
        }

        private static void DoTestExpect(byte[] data, byte[] expected)
        {
            string exp = Hex(expected);
            foreach (Mode m in new[] { Mode.Nntp, Mode.Plain, Mode.NntpEnd })
            {
                Assert.Equal(exp, Hex(ActualDecode(m, data)));
                Assert.Equal(exp, Hex(ActualDecodeInSitu(m, data)));
            }
        }

        [Fact]
        public void ExplicitCases()
        {
            DoTestExpect(Array.Empty<byte>(), Array.Empty<byte>());
            DoTestExpect(new byte[] { 10 }, Array.Empty<byte>());
            DoTestExpect(new byte[] { 61 }, Array.Empty<byte>());
            DoTestExpect(new byte[] { 61, 13 }, new byte[] { 163 });
            DoTestExpect(new byte[] { 61, 61 }, new byte[] { 211 });
            DoTestExpect(new byte[] { 61, 61, 13 }, new byte[] { 211 });
            DoTestExpect(new byte[] { 10, 61 }, Array.Empty<byte>());
        }

        [Fact]
        public void ReferenceCases_Small()
        {
            DoTestRef(new byte[] { 0, 1, 2, 10, 3, 61, 64 }); // simple
            DoTestRef(new byte[] { 13, 10, 46 });             // stripped dot
            DoTestRef(new byte[] { 46 });                     // just dot
            DoTestRef(new byte[] { 13, 10, 46, 13, 10, 46 }); // consecutive stripped dot
            DoTestRef(new byte[] { 61, 13, 10, 46 });         // bad escape stripped dot
        }

        [Fact]
        public void EndSequenceCases()
        {
            DoTestRef(Ascii("\r\n.\r\n"));
            DoTestRef(Ascii(".\r\n"));
            DoTestRef(Ascii("\r\n=y"));
            DoTestRef(Ascii("=y"));
            DoTestRef(Ascii("\r\n=y\r\n.\r\n"));
            DoTestRef(Ascii("\r\n.\r\n=y"));
            DoTestRef(Ascii("\r\n=abc"));
            DoTestRef(Ascii("\r\n.=y"));
            DoTestRef(Ascii("\r\n.=."));
            DoTestRef(Ascii("\r\n.\ra\n"));
            DoTestRef(Ascii("\r\n..\r\n"));
            DoTestRef(Ascii("\r\n.a=y"));
            DoTestRef(Ascii("=\r\n.\r\n"));
            DoTestRef(Ascii("=\r\n=y"));
        }

        [Fact]
        public void LongPatterns()
        {
            var b = Fill(256, 0);
            DoTestRef(b);
            b = Fill(256, (byte)'=');
            DoTestRef(b);
            for (int i = 1; i < b.Length; i += 2) b[i] = 64;
            DoTestRef(b);
            b = Fill(256, (byte)'=');
            for (int i = 0; i < b.Length; i += 2) b[i] = 64;
            DoTestRef(b);
            DoTestRef(Fill(256, 10));
            DoTestRef(Fill(256, 223));
        }

        [Fact]
        public void ExtraNullIssue()
        {
            DoTestRef(HexBytes("2e900a4fb6054c9126171cdc196dc41237bb1b76da9191aa5e85c1d2a2a5c638fe39054a210e8c799473cd510541fd118f3904b242a9938558c879238aae1d3bdab32e287cedb820b494f54ffae6dd0b13f73a4a9499df486a7845c612182bcef72a6e50a8e98351c35765d26c605115dc8c5c56a5e3f20ae6da8dcd78536e6d1601eb1fc3ddc774"));
        }

        [Fact]
        public void RandomData()
        {
            var rng = new Random(2024);
            for (int round = 0; round < 6; round++)
            {
                var rand = new byte[64 * 1024];
                rng.NextBytes(rand);
                DoTestRef(rand);
            }
        }

        [Fact]
        public void TargetedRandomData()
        {
            byte[] charset = Ascii("=\r\n.ay");
            var rng = new Random(99);
            for (int round = 0; round < 64; round++)
            {
                var rand = new byte[2048];
                for (int i = 0; i < rand.Length; i++) rand[i] = charset[rng.Next(charset.Length)];
                DoTestRef(rand);
            }
        }

        private static byte[] HexBytes(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }
    }
}
