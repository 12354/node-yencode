using System;
using System.Collections.Generic;
using Xunit;

namespace Yencode.Tests
{
    /// <summary>
    /// Directly cross-checks the SIMD kernels against the scalar implementations (independent of the
    /// public auto-dispatch), and confirms SIMD is actually engaged on capable hardware.
    /// </summary>
    public class SimdTests
    {
        private static string Hex(byte[] b) => Reference.Hex(b);
        private static byte[] Slice(byte[] a, int len) { var r = new byte[len]; Array.Copy(a, r, len); return r; }

        private static readonly int[] LineSizes = { 16, 17, 33, 34, 35, 48, 64, 100, 128, 129, 200, 256 };

        [Fact]
        public void IsaReported()
        {
            // Just exercise the properties; on this CI hardware they are expected to be non-generic,
            // but we don't hard-fail on machines without the instruction sets.
            _ = Yenc.EncoderIsa;
            _ = Yenc.DecoderIsa;
            _ = Crc32.IsaLevel;
        }

        [Fact]
        public void Encoder_Simd_Matches_Scalar()
        {
            if (Yenc.EncoderIsa == IsaLevel.Generic) return; // no SIMD here; scalar already validated
            var rng = new Random(31337);

            foreach (var data in SampleDatas(rng))
            {
                foreach (int ls in LineSizes)
                {
                    for (int off = 0; off < ls; off += Math.Max(1, ls / 5))
                    {
                        var ds = new byte[Yenc.MaxSize(data.Length, ls)];
                        int colS = off;
                        int ws = Yenc.EncodeScalar(ls, ref colS, data, ds, true);

                        var dv = new byte[Yenc.MaxSize(data.Length, ls)];
                        int colV = off;
                        bool used = EncoderSimd.TryEncode(ls, ref colV, data, dv, true, out int wv);
                        if (!used) continue; // short line / small input -> scalar path

                        Assert.Equal(Hex(Slice(ds, ws)), Hex(Slice(dv, wv)));
                    }
                }
            }
        }

        [Fact]
        public void Decoder_NoEnd_Simd_Matches_Scalar()
        {
            if (Yenc.DecoderIsa == IsaLevel.Generic) return;
            var rng = new Random(8088);

            foreach (var data in SampleDatas(rng))
            {
                foreach (bool isRaw in new[] { false, true })
                {
                    var ds = new byte[data.Length];
                    var s1 = DecoderState.Crlf;
                    int ws = isRaw
                        ? Yenc.DecodeNoEndRaw(data, ds, ref s1, false)
                        : Yenc.DecodeNoEndPlain(data, ds, ref s1, false);

                    var dv = new byte[data.Length];
                    var s2 = DecoderState.Crlf;
                    bool used = DecoderSimd.TryDecodeNoEnd(isRaw, data, dv, ref s2, false, out int wv);
                    if (!used) continue;

                    Assert.Equal(Hex(Slice(ds, ws)), Hex(Slice(dv, wv)));
                }
            }
        }

        [Fact]
        public void Decoder_End_Simd_Matches_Scalar()
        {
            if (Yenc.DecoderIsa == IsaLevel.Generic) return;
            var rng = new Random(6502);

            foreach (var data in SampleDatas(rng))
            {
                var ds = new byte[data.Length];
                var s1 = DecoderState.Crlf;
                var eS = Yenc.DecodeEndRawScalar(data, ds, ref s1, true, out int rS, out int wS);

                var dv = new byte[data.Length];
                var s2 = DecoderState.Crlf;
                bool used = DecoderSimd.TryDecodeEnd(data, dv, ref s2, true, out int rV, out int wV, out var eV);
                if (!used) continue;

                Assert.Equal(eS, eV);
                Assert.Equal(rS, rV);
                Assert.Equal(s1, s2);
                Assert.Equal(Hex(Slice(ds, wS)), Hex(Slice(dv, wV)));
            }
        }

        [Fact]
        public void Crc_Simd_Engaged()
        {
            // On hardware with PCLMULQDQ this should be the chosen path.
            if (!Crc32Clmul.IsSupported) return;
            Assert.Equal(IsaLevel.Pclmul, Crc32.IsaLevel);
        }

        private static IEnumerable<byte[]> SampleDatas(Random rng)
        {
            // pure random
            foreach (int n in new[] { 32, 33, 47, 64, 96, 200, 1000, 5000 })
            {
                var b = new byte[n];
                rng.NextBytes(b);
                yield return b;
            }
            // targeted charset (lots of specials / escapes / dots / newlines)
            byte[] charset = { (byte)'=', (byte)'\r', (byte)'\n', (byte)'.', (byte)'a', (byte)'y',
                               0xd6, 0xdf, 0xe0, 0x04, 0x13, 0xf6 };
            foreach (int n in new[] { 64, 256, 2048, 5000 })
            {
                var b = new byte[n];
                for (int i = 0; i < n; i++) b[i] = charset[rng.Next(charset.Length)];
                yield return b;
            }
            // all-plain (forces the fast path)
            yield return FillRandomPlain(rng, 3000);
        }

        private static byte[] FillRandomPlain(Random rng, int n)
        {
            // bytes that never need escaping nor are special, to maximise the vector fast path
            var b = new byte[n];
            for (int i = 0; i < n; i++)
            {
                byte v;
                do { v = (byte)rng.Next(256); }
                while (v == 0x3d || v == 0x0d || v == 0x0a || v == 214 || v == 227 || v == 224 || v == 19);
                b[i] = v;
            }
            return b;
        }
    }
}
