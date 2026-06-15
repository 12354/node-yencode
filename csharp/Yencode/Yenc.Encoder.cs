using System;

namespace Yencode
{
    public static partial class Yenc
    {
        // ---- lookup tables for scalar processing ----

        // escapeLUT[n] == 0  => character is "critical" (must be escaped);
        // otherwise it holds the encoded output byte (n+42)&0xff.
        internal static readonly byte[] EscapeLut = BuildEscapeLut();

        // escapedLUT[n] != 0  => character needs full escaping (including tab/space/dot for line edges).
        // The 16-bit value packs the two output bytes little-endian: low='=' (0x3d), high=(n+42+64)&0xff.
        internal static readonly ushort[] EscapedLut = BuildEscapedLut();

        private static byte[] BuildEscapeLut()
        {
            var lut = new byte[256];
            for (int n = 0; n < 256; n++)
            {
                bool critical = n == 214 || n == ('\r' + 214) || n == ('\n' + 214) || n == ('=' - 42);
                lut[n] = critical ? (byte)0 : (byte)((n + 42) & 0xff);
            }
            return lut;
        }

        private static ushort[] BuildEscapedLut()
        {
            var lut = new ushort[256];
            for (int n = 0; n < 256; n++)
            {
                bool esc = n == 214 || n == (214 + '\r') || n == (214 + '\n') || n == ('=' - 42)
                        || n == (214 + '\t') || n == (214 + ' ') || n == ('.' - 42);
                lut[n] = esc ? (ushort)('=' | (((n + 42 + 64) & 0xff) << 8)) : (ushort)0;
            }
            return lut;
        }

        /// <summary>The instruction set used by the encoder on this machine.</summary>
        public static IsaLevel EncoderIsa => EncoderSimd.Isa;

        // ---- size helpers ----

        /// <summary>
        /// Returns the maximum possible size of a raw yEnc encoded message of <paramref name="length"/>
        /// bytes (an over-estimate that includes padding for the SIMD implementation).
        /// <paramref name="escapeRatio"/> (0..1) lets you estimate an expected size instead of the max;
        /// the default of 1 yields the true maximum and is required for <see cref="EncodeTo"/>.
        /// </summary>
        public static int MaxSize(long length, int lineSize = 128, double escapeRatio = 1.0)
        {
            if (length == 0) return 0;
            double mult = escapeRatio + 1.0;
            if (mult < 1.0 || mult > 2.0)
                throw new ArgumentOutOfRangeException(nameof(escapeRatio), "yEnc escape ratio must be between 0 and 1");
            if (lineSize == 0) lineSize = 128;
            double esc = length * mult;
            return checked((int)(Math.Ceiling(esc)
                + 2 * Math.Floor(esc / lineSize)
                + 2   // provision for a column offset / an early newline
                + 64) // padding to make SIMD logic easier
            );
        }

        /// <summary>Returns the minimum possible size of a raw yEnc encoded message of <paramref name="length"/> bytes.</summary>
        public static int MinSize(long length, int lineSize = 128)
        {
            if (length == 0) return 0;
            if (lineSize == 0) lineSize = 128;
            return checked((int)(length + 2 * (length / lineSize)));
        }

        // ---- public encode API ----

        /// <summary>
        /// Performs raw yEnc encoding on <paramref name="data"/> and returns the result.
        /// </summary>
        /// <param name="lineSize">How often to insert newlines (lines may end up slightly longer, per the spec).</param>
        /// <param name="columnOffset">The column of the first character.</param>
        public static byte[] Encode(ReadOnlySpan<byte> data, int lineSize = 128, int columnOffset = 0)
        {
            if (data.Length == 0) return Array.Empty<byte>();
            (lineSize, columnOffset) = NormalizeEncodeArgs(lineSize, columnOffset);
            var dest = new byte[MaxSize(data.Length, lineSize)];
            int col = columnOffset;
            int len = EncodeDispatch(lineSize, ref col, data, dest, true);
            if (len == dest.Length) return dest;
            var ret = new byte[len];
            Array.Copy(dest, ret, len);
            return ret;
        }

        /// <summary>
        /// Performs raw yEnc encoding on <paramref name="data"/>, writing into <paramref name="output"/>
        /// and returning the number of bytes written. <paramref name="output"/> must be at least
        /// <see cref="MaxSize(long,int,double)"/> bytes.
        /// </summary>
        public static int EncodeTo(ReadOnlySpan<byte> data, Span<byte> output, int lineSize = 128, int columnOffset = 0)
        {
            if (data.Length == 0) return 0;
            (lineSize, columnOffset) = NormalizeEncodeArgs(lineSize, columnOffset);
            if (output.Length < MaxSize(data.Length, lineSize))
                throw new ArgumentException("Destination buffer does not have enough space (use MaxSize to compute required space)", nameof(output));
            int col = columnOffset;
            return EncodeDispatch(lineSize, ref col, data, output, true);
        }

        private static (int lineSize, int col) NormalizeEncodeArgs(int lineSize, int col)
        {
            if (lineSize == 0) lineSize = 128;
            if (lineSize < 0) throw new ArgumentOutOfRangeException(nameof(lineSize), "Line size must be at least 1 byte");
            if (col < 0 || col > lineSize)
                throw new ArgumentOutOfRangeException(nameof(col), "Column offset cannot exceed the line size and cannot be negative");
            if (col == lineSize) col = 0;
            return (lineSize, col);
        }

        internal static int EncodeDispatch(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd)
        {
            if (EncoderSimd.TryEncode(lineSize, ref col, src, dest, doEnd, out int written))
                return written;
            return EncodeScalar(lineSize, ref col, src, dest, doEnd);
        }

        /// <summary>
        /// Portable scalar yEnc encoder. Faithful port of node-yencode's <c>do_encode_generic</c>.
        /// </summary>
        internal static int EncodeScalar(int lineSize, ref int col, ReadOnlySpan<byte> src, Span<byte> dest, bool doEnd)
        {
            var escapeLut = EscapeLut;
            var escapedLut = EscapedLut;
            int len = src.Length;
            if (len < 1) return 0;

            int es = len;       // src[es + i] addresses input like the C++ "es[i]"
            int p = 0;          // destination write position
            int i = -len;       // input position (negative, walking toward 0)
            int c, escaped;

            if (col == 0)
            {
                c = src[es + i]; i++;
                ushort v = escapedLut[c];
                if (v != 0) { dest[p] = (byte)v; dest[p + 1] = (byte)(v >> 8); p += 2; col = 2; }
                else { dest[p++] = (byte)(c + 42); col = 1; }
            }

            while (i < 0)
            {
                int sp = 0;
                bool spSet = false;
                while (i < -1 - 8 && lineSize - col - 1 > 8)
                {
                    sp = p; spSet = true;
                    for (int n = 0; n < 8; n++)
                    {
                        c = src[es + i + n]; escaped = escapeLut[c];
                        if (escaped != 0) { dest[p++] = (byte)escaped; }
                        else { ushort v = escapedLut[c]; dest[p] = (byte)v; dest[p + 1] = (byte)(v >> 8); p += 2; }
                    }
                    i += 8; col += p - sp;
                }
                if (spSet && col >= lineSize - 1)
                {
                    col -= p - sp; p = sp; i -= 8;
                }
                while (col < lineSize - 1)
                {
                    c = src[es + i]; i++; escaped = escapeLut[c];
                    if (escaped != 0) { dest[p++] = (byte)escaped; col++; }
                    else { ushort v = escapedLut[c]; dest[p] = (byte)v; dest[p + 1] = (byte)(v >> 8); p += 2; col += 2; }
                    if (i >= 0) goto end;
                }

                // last char on the line
                if (col < lineSize)
                {
                    c = src[es + i]; i++;
                    ushort v = escapedLut[c];
                    if (v != 0 && c != ('.' - 42)) { dest[p] = (byte)v; dest[p + 1] = (byte)(v >> 8); p += 2; }
                    else { dest[p++] = (byte)(c + 42); }
                }

                if (i >= 0) break;

                c = src[es + i]; i++;
                ushort w = escapedLut[c];
                if (w != 0)
                {
                    dest[p] = (byte)'\r'; dest[p + 1] = (byte)'\n'; dest[p + 2] = (byte)w; dest[p + 3] = (byte)(w >> 8);
                    p += 4; col = 2;
                }
                else
                {
                    dest[p] = (byte)'\r'; dest[p + 1] = (byte)'\n'; dest[p + 2] = (byte)(c + 42);
                    p += 3; col = 1;
                }
            }

            end:
            if (doEnd)
            {
                int lc = dest[p - 1];
                if (lc == '\t' || lc == ' ')
                {
                    dest[p - 1] = (byte)'='; dest[p] = (byte)(lc + 64); p++; col++;
                }
            }
            return p;
        }
    }
}
