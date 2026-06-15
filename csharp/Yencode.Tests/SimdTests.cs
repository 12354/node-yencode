using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
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
        public void SelectedIsa_MatchesHardware()
        {
            // Positive proof that the right vector path is chosen for the current CPU, so a green run
            // on an ARM runner demonstrates the NEON encoder/decoder (and ARM CRC) are the ones in use.
            if (Avx2.IsSupported)
            {
                Assert.Equal(IsaLevel.Avx2, Yenc.EncoderIsa);
                Assert.Equal(IsaLevel.Avx2, Yenc.DecoderIsa);
            }
            else if (Sse2.IsSupported)
            {
                Assert.Equal(IsaLevel.Sse2, Yenc.EncoderIsa);
                Assert.Equal(IsaLevel.Sse2, Yenc.DecoderIsa);
            }
            else if (AdvSimd.Arm64.IsSupported)
            {
                Assert.Equal(IsaLevel.Neon, Yenc.EncoderIsa);
                Assert.Equal(IsaLevel.Neon, Yenc.DecoderIsa);
                Assert.True(Crc32Arm.IsSupported);
                Assert.Equal(IsaLevel.ArmCrc, Crc32.IsaLevel);
            }
        }

        [Fact]
        public void Simd_VectorInstructions_Actually_Execute()
        {
            // Proves the vectorized fast path (the code containing the SSE2/AVX2/NEON intrinsics) is
            // really reached — not just the SIMD-capable method entered, then everything done scalar.
            // Combined with the .NET rule "intrinsic + IsSupported => the actual instruction is emitted",
            // a non-zero count means real SIMD instructions executed on this CPU.
            if (Yenc.EncoderIsa == IsaLevel.Generic) return;

            var plain = FillRandomPlain(new Random(1), 8192); // no escapes / specials -> maximises fast path
            SimdHelpers.EncoderVectorBlocks = 0;
            SimdHelpers.DecoderVectorBlocks = 0;
            SimdHelpers.Probe = true;
            byte[] enc, dec;
            try
            {
                enc = Yenc.Encode(plain, 128);
                dec = Yenc.Decode(enc, false);
            }
            finally
            {
                SimdHelpers.Probe = false;
            }

            Assert.Equal(plain, dec); // round-trips correctly via the vector path
            Assert.True(SimdHelpers.EncoderVectorBlocks > 0, "encoder vector instructions never executed (" + Yenc.EncoderIsa + ")");
            Assert.True(SimdHelpers.DecoderVectorBlocks > 0, "decoder vector instructions never executed (" + Yenc.DecoderIsa + ")");
        }

        [Fact]
        public void Encoder_Simd_Matches_Scalar()
        {
            if (Yenc.EncoderIsa == IsaLevel.Generic) return; // no SIMD here; scalar already validated
            var rng = new Random(31337);
            bool any = false;

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
                        any = true;

                        Assert.Equal(Hex(Slice(ds, ws)), Hex(Slice(dv, wv)));
                    }
                }
            }
            // prove we actually exercised the SIMD encoder (not a vacuous pass)
            Assert.True(any, "SIMD encoder path (" + Yenc.EncoderIsa + ") was never exercised");
        }

        [Fact]
        public void Decoder_NoEnd_Simd_Matches_Scalar()
        {
            if (Yenc.DecoderIsa == IsaLevel.Generic) return;
            var rng = new Random(8088);
            bool any = false;

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
                    any = true;

                    Assert.Equal(Hex(Slice(ds, ws)), Hex(Slice(dv, wv)));
                }
            }
            Assert.True(any, "SIMD decoder path (" + Yenc.DecoderIsa + ") was never exercised");
        }

        [Fact]
        public void Decoder_End_Simd_Matches_Scalar()
        {
            if (Yenc.DecoderIsa == IsaLevel.Generic) return;
            var rng = new Random(6502);
            bool any = false;

            foreach (var data in SampleDatas(rng))
            {
                var ds = new byte[data.Length];
                var s1 = DecoderState.Crlf;
                var eS = Yenc.DecodeEndRawScalar(data, ds, ref s1, true, out int rS, out int wS);

                var dv = new byte[data.Length];
                var s2 = DecoderState.Crlf;
                bool used = DecoderSimd.TryDecodeEnd(data, dv, ref s2, true, out int rV, out int wV, out var eV);
                if (!used) continue;
                any = true;

                Assert.Equal(eS, eV);
                Assert.Equal(rS, rV);
                Assert.Equal(s1, s2);
                Assert.Equal(Hex(Slice(ds, wS)), Hex(Slice(dv, wV)));
            }
            Assert.True(any, "SIMD end-decoder path (" + Yenc.DecoderIsa + ") was never exercised");
        }

        [Fact]
        public void Crc_Simd_Engaged()
        {
            // On hardware with PCLMULQDQ (x86) or ARMv8 CRC, that should be the chosen path.
            if (Crc32Clmul.IsSupported)
                Assert.Equal(IsaLevel.Pclmul, Crc32.IsaLevel);
            else if (Crc32Arm.IsSupported)
                Assert.Equal(IsaLevel.ArmCrc, Crc32.IsaLevel);
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
