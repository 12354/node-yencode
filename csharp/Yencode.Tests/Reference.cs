using System;
using System.Collections.Generic;

namespace Yencode.Tests
{
    /// <summary>
    /// Slow, obviously-correct reference implementations ported directly from node-yencode's
    /// JavaScript test suite (test/testenc.js, test/testdec.js) plus a bitwise CRC32. These are
    /// the oracle the managed implementation is checked against.
    /// </summary>
    internal static class Reference
    {
        // ---- yEnc encode reference (test/testenc.js refYEnc) ----
        public static byte[] Encode(byte[] src, int lineSize, int col)
        {
            var ret = new List<byte>();
            for (int i = 0; i < src.Length; i++)
            {
                int c = (src[i] + 42) & 0xFF;
                bool esc;
                if (c == 0 || c == '\r' || c == '\n' || c == '=') esc = true;
                else if (c == '\t' || c == ' ') esc = !(col > 0 && col < lineSize - 1);
                else if (c == '.') esc = col == 0;
                else esc = false;

                if (esc) { ret.Add((byte)'='); c = (c + 64) & 0xFF; col++; }
                ret.Add((byte)c); col++;
                if (col >= lineSize) { ret.Add((byte)'\r'); ret.Add((byte)'\n'); col = 0; }
            }

            int n = ret.Count;
            if (n >= 2 && ret[n - 2] == '\r' && ret[n - 1] == '\n')
            {
                ret.RemoveAt(n - 1); ret.RemoveAt(n - 2);
            }
            n = ret.Count;
            if (n >= 1 && (ret[n - 1] == '\t' || ret[n - 1] == ' '))
            {
                int c = ret[n - 1];
                ret.RemoveAt(n - 1);
                ret.Add((byte)'=');
                ret.Add((byte)((c + 64) & 0xFF));
            }
            return ret.ToArray();
        }

        // ---- yEnc decode reference, plain (test/testdec.js refYDec) ----
        public static byte[] Decode(byte[] src, bool findEnd)
        {
            var ret = new List<byte>();
            if (findEnd && src.Length >= 2 && src[0] == '=' && src[1] == 'y') return Array.Empty<byte>();
            for (int i = 0; i < src.Length; i++)
            {
                int ch = src[i];
                if (ch == '\r')
                {
                    if (findEnd && At(src, i + 1) == '\n' && At(src, i + 2) == '=' && At(src, i + 3) == 'y')
                        return ret.ToArray();
                    continue; // fall-through to '\n'
                }
                if (ch == '\n') continue;
                if (ch == '=')
                {
                    i++;
                    if (i < src.Length) ret.Add((byte)((src[i] - 42 - 64) & 0xFF));
                    if (At(src, i) == '\r') i--;
                    continue;
                }
                ret.Add((byte)((src[i] - 42) & 0xFF));
            }
            return ret.ToArray();
        }

        // ---- yEnc decode reference, raw/NNTP (test/testdec.js refYDecRaw) ----
        public static byte[] DecodeRaw(byte[] src, bool findEnd)
        {
            var data = new List<byte>();
            int i = 0;
            if (src.Length > 0 && src[0] == '.')
            {
                i++;
                if (findEnd && At(src, 1) == '\r' && At(src, 2) == '\n') return Array.Empty<byte>();
            }
            for (; i < src.Length; i++)
            {
                if (src[i] == '\r' && At(src, i + 1) == '\n' && At(src, i + 2) == '.')
                {
                    data.Add(src[i]); data.Add((byte)At(src, i + 1));
                    if (findEnd && At(src, i + 3) == '\r' && At(src, i + 4) == '\n') break;
                    i += 2;
                    continue;
                }
                data.Add(src[i]);
            }
            return Decode(data.ToArray(), findEnd);
        }

        private static int At(byte[] a, int i) => (i >= 0 && i < a.Length) ? a[i] : -1;

        // ---- bitwise CRC32 reference ----
        public static uint Crc32(byte[] data, uint init = 0)
        {
            uint c = ~init;
            foreach (byte b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++)
                    c = (c >> 1) ^ (0xEDB88320u & (uint)(-(int)(c & 1)));
            }
            return ~c;
        }

        public static string Hex(byte[] b)
        {
            var sb = new System.Text.StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }
    }
}
