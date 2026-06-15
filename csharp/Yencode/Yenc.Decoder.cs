using System;

namespace Yencode
{
    public static partial class Yenc
    {
        /// <summary>The instruction set used by the decoder on this machine.</summary>
        public static IsaLevel DecoderIsa => DecoderSimd.Isa;

        // ---- public decode API ----

        /// <summary>
        /// Performs raw yEnc decoding on <paramref name="data"/> and returns the result.
        /// If <paramref name="stripDots"/> is true, NNTP "dot unstuffing" is performed during decode.
        /// </summary>
        public static byte[] Decode(ReadOnlySpan<byte> data, bool stripDots = false)
        {
            if (data.Length == 0) return Array.Empty<byte>();
            var dest = new byte[data.Length];
            DecoderState s = DecoderState.Crlf;
            int w = DecodeNoEndDispatch(stripDots, data, dest, ref s, false);
            if (w == dest.Length) return dest;
            var ret = new byte[w];
            Array.Copy(dest, ret, w);
            return ret;
        }

        /// <summary>
        /// Performs raw yEnc decoding on <paramref name="data"/>, writing into <paramref name="output"/>
        /// (which must be at least <c>data.Length</c> bytes; may alias <paramref name="data"/> for in-situ
        /// decoding) and returning the number of bytes written.
        /// </summary>
        public static int DecodeTo(ReadOnlySpan<byte> data, Span<byte> output, bool stripDots = false)
        {
            if (data.Length == 0) return 0;
            if (output.Length < data.Length)
                throw new ArgumentException("Destination buffer does not have enough space", nameof(output));
            DecoderState s = DecoderState.Crlf;
            return DecodeNoEndDispatch(stripDots, data, output, ref s, false);
        }

        /// <summary>
        /// Incrementally decodes a chunk of raw yEnc data sourced from NNTP, performing dot unstuffing
        /// and detecting the end of the data. Pass the <paramref name="state"/> returned by the previous
        /// call (start a fresh article with <see cref="DecoderState.Crlf"/>).
        /// </summary>
        /// <param name="read">Number of input bytes consumed.</param>
        /// <param name="written">Number of bytes written to <paramref name="output"/>.</param>
        /// <returns>Whether (and how) the end of the yEnc data was reached.</returns>
        public static DecodeEnd DecodeIncremental(ReadOnlySpan<byte> data, Span<byte> output, ref DecoderState state, out int read, out int written)
        {
            if (data.Length == 0) { read = 0; written = 0; return DecodeEnd.None; }
            if (output.Length < data.Length)
                throw new ArgumentException("Destination buffer does not have enough space", nameof(output));
            return DecodeEndDispatch(data, output, ref state, true, out read, out written);
        }

        /// <summary>Result of <see cref="DecodeChunk(ReadOnlySpan{byte}, DecoderState)"/>.</summary>
        public struct DecodeChunkResult
        {
            /// <summary>Number of input bytes consumed.</summary>
            public int Read;
            /// <summary>Number of decoded bytes.</summary>
            public int Written;
            /// <summary>The decoded data.</summary>
            public byte[] Output;
            /// <summary>Whether (and how) the end of the yEnc data was reached.</summary>
            public DecodeEnd Ended;
            /// <summary>State to feed into the next call.</summary>
            public DecoderState State;
        }

        /// <summary>
        /// Convenience wrapper over <see cref="DecodeIncremental"/> that allocates the output buffer.
        /// </summary>
        public static DecodeChunkResult DecodeChunk(ReadOnlySpan<byte> data, DecoderState state = DecoderState.Crlf)
        {
            var output = new byte[data.Length];
            var st = state;
            var end = DecodeIncremental(data, output, ref st, out int read, out int written);
            if (written != output.Length)
            {
                var o = new byte[written];
                Array.Copy(output, o, written);
                output = o;
            }
            return new DecodeChunkResult { Read = read, Written = written, Output = output, Ended = end, State = st };
        }

        // ---- dispatch ----

        internal static int DecodeNoEndDispatch(bool isRaw, ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState)
        {
            if (DecoderSimd.TryDecodeNoEnd(isRaw, src, dest, ref state, hasState, out int written))
                return written;
            return isRaw
                ? DecodeNoEndRaw(src, dest, ref state, hasState)
                : DecodeNoEndPlain(src, dest, ref state, hasState);
        }

        internal static DecodeEnd DecodeEndDispatch(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState, out int read, out int written)
        {
            if (DecoderSimd.TryDecodeEnd(src, dest, ref state, hasState, out read, out written, out DecodeEnd end))
                return end;
            return DecodeEndRawScalar(src, dest, ref state, hasState, out read, out written);
        }

        // ---- scalar implementations (ports of node-yencode's do_decode_*_scalar) ----

        internal static int DecodeNoEndRaw(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState)
        {
            int len = src.Length;
            if (len < 1) return 0;
            int es = len, p = 0, i = -len, c = 0;

            if (hasState)
            {
                switch (state)
                {
                    case DecoderState.Eq:
                        c = src[es + i]; dest[p++] = (byte)(c - 42 - 64); i++;
                        if (c == '\r') { state = DecoderState.Cr; if (i >= 0) return p; }
                        else { state = DecoderState.None; goto afterState; }
                        goto case DecoderState.Cr;
                    case DecoderState.Cr:
                        if (src[es + i] != '\n') goto afterState;
                        i++; state = DecoderState.Crlf; if (i >= 0) return p;
                        goto case DecoderState.Crlf;
                    case DecoderState.Crlf:
                        if (src[es + i] == '.') i++;
                        goto afterState;
                    default:
                        goto afterState;
                }
            }
            else
            {
                if (src[es + i] == '.') i++;
            }

            afterState:
            for (; i < -2; i++)
            {
                c = src[es + i];
                if (c == '\r')
                {
                    if (src[es + i + 1] == '\n' && src[es + i + 2] == '.') i += 2;
                    continue;
                }
                if (c == '\n') continue;
                if (c == '=')
                {
                    c = src[es + i + 1]; dest[p++] = (byte)(c - 42 - 64); if (c != '\r') i++;
                    continue;
                }
                dest[p++] = (byte)(c - 42);
            }
            if (hasState) state = DecoderState.None;
            if (i == -2)
            {
                c = src[es + i];
                if (c == '\r') { if (hasState && src[es + i + 1] == '\n') { state = DecoderState.Crlf; return p; } }
                else if (c == '\n') { }
                else if (c == '=') { c = src[es + i + 1]; dest[p++] = (byte)(c - 42 - 64); if (c != '\r') i++; }
                else { dest[p++] = (byte)(c - 42); }
                i++;
            }
            if (i == -1)
            {
                c = src[es + i];
                if (c != '\n' && c != '\r' && c != '=') { dest[p++] = (byte)(c - 42); }
                else if (hasState) { if (c == '=') state = DecoderState.Eq; else if (c == '\r') state = DecoderState.Cr; else state = DecoderState.None; }
            }
            return p;
        }

        internal static int DecodeNoEndPlain(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState)
        {
            int len = src.Length;
            if (len < 1) return 0;
            int es = len, p = 0, i = -len, c;

            if (hasState && state == DecoderState.Eq)
            {
                dest[p++] = (byte)(src[es + i] - 42 - 64); i++; state = DecoderState.None;
            }
            for (; i < -1; i++)
            {
                c = src[es + i];
                if (c == '\n' || c == '\r') continue;
                if (c == '=') { i++; c = src[es + i] - 64; }
                dest[p++] = (byte)(c - 42);
            }
            if (hasState) state = DecoderState.None;
            if (i == -1)
            {
                c = src[es + i];
                if (c != '\n' && c != '\r' && c != '=') { dest[p++] = (byte)(c - 42); }
                else if (hasState) { state = (c == '=') ? DecoderState.Eq : DecoderState.None; }
            }
            return p;
        }

        internal static DecodeEnd DecodeEndRawScalar(ReadOnlySpan<byte> src, Span<byte> dest, ref DecoderState state, bool hasState, out int read, out int written)
        {
            int len = src.Length;
            int es = len, p = 0, i = -len, c = 0;
            if (len < 1) { read = 0; written = 0; return DecodeEnd.None; }

            if (hasState)
            {
                switch (state)
                {
                    case DecoderState.CrlfEq: goto L_ceq;
                    case DecoderState.Eq: goto L_eq;
                    case DecoderState.Cr: goto L_cr;
                    case DecoderState.Crlf: goto L_crlf;
                    case DecoderState.CrlfDt: goto L_crlfdt;
                    case DecoderState.CrlfDtCr: goto L_crlfdtcr;
                    default: goto L_main; // None
                }
            }
            else goto L_crlf;

            L_ceq:
            if (src[es + i] == 'y') { state = DecoderState.None; read = es + i + 1; written = p; return DecodeEnd.Control; }
            // fall through to L_eq
            L_eq:
            c = src[es + i]; dest[p++] = (byte)(c - 42 - 64); i++;
            if (c != '\r') goto L_main;
            if (i == 0) { state = DecoderState.Cr; read = es; written = p; return DecodeEnd.None; }
            // fall through to L_cr
            L_cr:
            if (src[es + i] != '\n') goto L_main;
            i++;
            if (i == 0) { state = DecoderState.Crlf; read = es; written = p; return DecodeEnd.None; }
            // fall through to L_crlf
            L_crlf:
            if (src[es + i] == '.') { i++; if (i == 0) { state = DecoderState.CrlfDt; read = es; written = p; return DecodeEnd.None; } }
            else if (src[es + i] == '=') { i++; if (i == 0) { state = DecoderState.CrlfEq; read = es; written = p; return DecodeEnd.None; } goto L_ceq; }
            else goto L_main;
            // fall through to L_crlfdt
            L_crlfdt:
            if (src[es + i] == '\r') { i++; if (i == 0) { state = DecoderState.CrlfDtCr; read = es; written = p; return DecodeEnd.None; } }
            else if (src[es + i] == '=') { i++; if (i == 0) { state = DecoderState.CrlfEq; read = es; written = p; return DecodeEnd.None; } goto L_ceq; }
            else goto L_main;
            // fall through to L_crlfdtcr
            L_crlfdtcr:
            if (src[es + i] == '\n') { state = DecoderState.Crlf; read = es + i + 1; written = p; return DecodeEnd.Article; }
            else goto L_main;

            L_main:
            for (; i < -2; i++)
            {
                c = src[es + i];
                if (c == '\r')
                {
                    if (src[es + i + 1] == '\n')
                    {
                        if (src[es + i + 2] == '.')
                        {
                            i += 3;
                            if (i == 0) { state = DecoderState.CrlfDt; read = es; written = p; return DecodeEnd.None; }
                            if (src[es + i] == '\r')
                            {
                                i++;
                                if (i == 0) { state = DecoderState.CrlfDtCr; read = es; written = p; return DecodeEnd.None; }
                                if (src[es + i] == '\n') { read = es + i + 1; written = p; state = DecoderState.Crlf; return DecodeEnd.Article; }
                                else i--;
                            }
                            else if (src[es + i] == '=')
                            {
                                i++;
                                if (i == 0) { state = DecoderState.CrlfEq; read = es; written = p; return DecodeEnd.None; }
                                if (src[es + i] == 'y') { read = es + i + 1; written = p; state = DecoderState.None; return DecodeEnd.Control; }
                                else { c = src[es + i]; dest[p++] = (byte)(c - 42 - 64); if (c == '\r') i--; }
                            }
                            else i--;
                        }
                        else if (src[es + i + 2] == '=')
                        {
                            i += 3;
                            if (i == 0) { state = DecoderState.CrlfEq; read = es; written = p; return DecodeEnd.None; }
                            if (src[es + i] == 'y') { read = es + i + 1; written = p; state = DecoderState.None; return DecodeEnd.Control; }
                            else { c = src[es + i]; dest[p++] = (byte)(c - 42 - 64); if (c == '\r') i--; }
                        }
                    }
                    continue;
                }
                if (c == '\n') continue;
                if (c == '=') { c = src[es + i + 1]; dest[p++] = (byte)(c - 42 - 64); if (c != '\r') i++; continue; }
                dest[p++] = (byte)(c - 42);
            }
            if (hasState) state = DecoderState.None;
            if (i == -2)
            {
                c = src[es + i];
                if (c == '\r') { if (hasState && src[es + i + 1] == '\n') { state = DecoderState.Crlf; read = es; written = p; return DecodeEnd.None; } }
                else if (c == '\n') { }
                else if (c == '=') { c = src[es + i + 1]; dest[p++] = (byte)(c - 42 - 64); if (c != '\r') i++; }
                else { dest[p++] = (byte)(c - 42); }
                i++;
            }
            if (i == -1)
            {
                c = src[es + i];
                if (c != '\n' && c != '\r' && c != '=') { dest[p++] = (byte)(c - 42); }
                else if (hasState) { if (c == '=') state = DecoderState.Eq; else if (c == '\r') state = DecoderState.Cr; else state = DecoderState.None; }
            }
            read = es; written = p; return DecodeEnd.None;
        }
    }
}
