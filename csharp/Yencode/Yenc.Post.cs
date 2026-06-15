using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Yencode
{
    public static partial class Yenc
    {
        /// <summary>Character set used for encoding/decoding filenames (default UTF-8).</summary>
        public static Encoding FilenameEncoding { get; set; } = Encoding.UTF8;

        private static readonly Regex ReYProp = new Regex("([a-z_][a-z_0-9]*)=", RegexOptions.Compiled);
        private static readonly Regex ReNumber = new Regex(@"^\d+$", RegexOptions.Compiled);
        private static readonly Regex ReCrc = new Regex("^[a-fA-F0-9]{8}$", RegexOptions.Compiled);

        // bytes for an ASCII/Latin1 string (each char assumed < 256)
        private static byte[] Latin1(string s)
        {
            var b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        private static string CleanFilename(string filename)
        {
            // mirror the original: strip "\r\n\0" sequences, cap at 256 chars
            filename = filename.Replace("\r\n\0", "");
            if (filename.Length > 256) filename = filename.Substring(0, 256);
            return filename;
        }

        /// <summary>
        /// Returns a single yEnc encoded post (a full <c>=ybegin</c>/<c>=yend</c> block) for
        /// <paramref name="data"/>, suitable for posting to newsgroups.
        /// </summary>
        public static byte[] Post(string filename, ReadOnlySpan<byte> data, int lineSize = 128)
        {
            if (lineSize == 0) lineSize = 128;
            var fn = FilenameEncoding.GetBytes(CleanFilename(filename));
            uint crc = Crc32.Compute(data);
            using var ms = new MemoryStream();
            WriteAscii(ms, "=ybegin line=" + lineSize + " size=" + data.Length + " name=");
            ms.Write(fn, 0, fn.Length);
            ms.WriteByte((byte)'\r'); ms.WriteByte((byte)'\n');
            WriteEncoded(ms, data, lineSize);
            WriteAscii(ms, "\r\n=yend size=" + data.Length + " crc32=" + Crc32.ToHex(crc));
            return ms.ToArray();
        }

        /// <summary>Convenience overload accepting a byte array.</summary>
        public static byte[] Post(string filename, byte[] data, int lineSize = 128) =>
            Post(filename, new ReadOnlySpan<byte>(data ?? Array.Empty<byte>()), lineSize);

        /// <summary>
        /// Creates a <see cref="YEncoder"/> for generating multi-part yEnc posts sequentially.
        /// </summary>
        public static YEncoder MultiPost(string filename, long size, int parts, int lineSize = 128) =>
            new YEncoder(filename, size, parts, lineSize);

        private static void WriteAscii(Stream s, string text)
        {
            var b = Latin1(text);
            s.Write(b, 0, b.Length);
        }

        private static void WriteEncoded(Stream s, ReadOnlySpan<byte> data, int lineSize)
        {
            if (data.Length == 0) return;
            var buf = new byte[MaxSize(data.Length, lineSize)];
            int col = 0;
            int len = EncodeDispatch(lineSize, ref col, data, buf, true);
            s.Write(buf, 0, len);
        }

        // ---- from_post ----

        private static int IndexOf(byte[] hay, int start, byte[] needle)
        {
            if (needle.Length == 0) return -1;
            if (start < 0) start = 0;
            int last = hay.Length - needle.Length;
            for (int i = start; i <= last; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static int LastIndexOf(byte[] hay, int start, int length, byte[] needle)
        {
            if (needle.Length == 0 || needle.Length > length) return -1;
            for (int i = start + length - needle.Length; i >= start; i--)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i - start;
            }
            return -1;
        }

        private static string LineString(byte[] data, int from, int to)
        {
            // Latin1 decode (1:1 with bytes) then trim, matching the original's line handling for ASCII content.
            var sb = new StringBuilder(to - from);
            for (int i = from; i < to; i++) sb.Append((char)data[i]);
            return sb.ToString().Trim();
        }

        private static List<YencWarning> ParseLines(IEnumerable<string> lines, Dictionary<string, Dictionary<string, string>> ydata)
        {
            var warnings = new List<YencWarning>();
            foreach (var rawLine in lines)
            {
                var yprops = new Dictionary<string, string>();
                string line = rawLine.Length >= 2 ? rawLine.Substring(2) : ""; // cut '=y'
                int p = line.IndexOf(' ');
                string tag = p < 0 ? line : line.Substring(0, p);
                line = (tag.Length + 1 <= line.Length ? line.Substring(tag.Length + 1) : "").Trim();

                var m = ReYProp.Match(line);
                while (m.Success)
                {
                    if (m.Index != 0)
                        warnings.Add(new YencWarning("ignored_line_data", "Unknown additional data ignored: \"" + line.Substring(0, m.Index) + "\""));
                    string prop = m.Groups[1].Value;
                    int valPos = m.Index + m.Length;
                    string val;
                    if (tag == "begin" && prop == "name")
                    {
                        val = line.Substring(valPos);
                        line = "";
                    }
                    else
                    {
                        p = line.IndexOf(' ', valPos);
                        val = p < 0 ? line.Substring(valPos) : line.Substring(valPos, p - valPos);
                        line = valPos + val.Length + 1 <= line.Length ? line.Substring(valPos + val.Length + 1) : "";
                    }
                    if (yprops.ContainsKey(prop))
                        warnings.Add(new YencWarning("duplicate_property", "Duplicate property encountered: `" + prop + "`"));
                    yprops[prop] = val;
                    m = ReYProp.Match(line);
                }
                if (line != "")
                    warnings.Add(new YencWarning("ignored_line_data", "Unknown additional end-of-line data ignored: \"" + line + "\""));
                if (ydata.ContainsKey(tag))
                    warnings.Add(new YencWarning("duplicate_line", "Duplicate line encountered: `" + tag + "`"));
                ydata[tag] = yprops;
            }
            return warnings;
        }

        private static bool Truthy(Dictionary<string, string> d, string key) =>
            d != null && d.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v);

        private static string Get(Dictionary<string, string> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v : null;

        private static double Num(string s)
        {
            if (s == null) return double.NaN;
            if (s.Length == 0) return 0;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
        }

        /// <summary>
        /// Decodes a complete yEnc post from <paramref name="data"/>. Set <paramref name="stripDots"/> to
        /// true if NNTP "dot unstuffing" has not yet been performed on the input.
        /// </summary>
        /// <returns>
        /// A <see cref="YencPost"/> describing the parsed post. On failure, the returned object has
        /// <see cref="YencPost.IsError"/> set with a <see cref="YencPost.ErrorCode"/>.
        /// </returns>
        public static YencPost FromPost(ReadOnlySpan<byte> dataSpan, bool stripDots = false)
        {
            byte[] data = dataSpan.ToArray();
            var ret = new YencPost();

            // locate '=ybegin '
            int yencStart;
            byte[] ybeginMarker = Latin1("=ybegin ");
            if (data.Length >= 8 && IsPrefix(data, 0, ybeginMarker))
                yencStart = 0;
            else
            {
                yencStart = IndexOf(data, 0, Latin1("\r\n=ybegin "));
                if (yencStart < 0) return YencPost.Error("no_start_found", "yEnc start marker not found");
                yencStart += 2;
            }
            ret.YencStart = yencStart;

            // collect the start lines
            var lines = new List<string>();
            int sp = yencStart;
            byte[] crlf = Latin1("\r\n");
            int p = IndexOf(data, yencStart + 8, crlf);
            while (p > 0)
            {
                string line = LineString(data, sp, p);
                lines.Add(line);
                sp = p + 2;
                if (line.Length >= 6 && line.Substring(0, 6) == "=yend ")
                {
                    ret.YencEnd = sp;
                    break;
                }
                bool isYLine = sp + 1 < data.Length && data[sp] == 0x3d /*=*/ && data[sp + 1] == 0x79 /*y*/;
                if (!isYLine)
                {
                    ret.DataStart = sp;
                    break;
                }
                p = IndexOf(data, sp, crlf);
            }
            if (ret.DataStart == null && !ret.YencEndSet)
                return YencPost.Error("no_end_found", "yEnd end marker not found");

            var ydata = new Dictionary<string, Dictionary<string, string>>();
            var warnings = ParseLines(lines, ydata);

            if (!ret.YencEndSet)
            {
                int dataStart = ret.DataStart.Value;
                int rev = LastIndexOf(data, dataStart, data.Length - dataStart, Latin1("\r\n=yend "));
                if (rev < 0) return YencPost.Error("no_end_found", "yEnd end marker not found");
                int yencEnd = rev + dataStart;
                ret.DataEnd = yencEnd;
                p = IndexOf(data, yencEnd + 8, crlf);
                int lineEnd;
                if (p < 0)
                {
                    warnings.Add(new YencWarning("missing_yend_newline", "No line terminator found for =yend line"));
                    p = data.Length;
                    ret.YencEnd = p;
                    lineEnd = p;
                }
                else
                {
                    ret.YencEnd = p + 2;
                    lineEnd = p;
                }
                string endLine = LineString(data, yencEnd + 2, lineEnd);
                warnings.AddRange(ParseLines(new[] { endLine }, ydata));
            }

            ret.Props = ydata;

            ydata.TryGetValue("begin", out var begin);
            ydata.TryGetValue("end", out var end);
            ydata.TryGetValue("part", out var part);

            if (begin == null || !Truthy(begin, "line") || !Truthy(begin, "size") || !begin.ContainsKey("name"))
                return YencPost.Error("missing_required_properties", "Could not find line/size/name properties on ybegin line");
            if (end == null || !Truthy(end, "size"))
                return YencPost.Error("missing_required_properties", "Could not find size properties on yend line");

            // numerical field checks
            CheckNumeric(begin, new[] { "line", "total", "part", "size" }, warnings);
            CheckNumeric(part, new[] { "begin", "end" }, warnings);
            CheckNumeric(end, new[] { "size", "part" }, warnings);

            string bPart = Get(begin, "part"), ePart = Get(end, "part");
            if (Truthy(begin, "part") && Truthy(end, "part") && bPart != ePart)
                warnings.Add(new YencWarning("part_number_mismatch", "Part number specified in begin and end do not match"));
            else if (Truthy(begin, "total") &&
                     ((Truthy(begin, "part") && Num(Get(begin, "total")) < Num(bPart)) ||
                      (Truthy(end, "part") && Num(Get(begin, "total")) < Num(ePart))))
                warnings.Add(new YencWarning("part_number_exceeds_total", "Specified part number exceeds specified total"));

            double expectedSize = Num(Get(end, "size"));
            if (part != null && Truthy(part, "begin") && Truthy(part, "end"))
            {
                double partBegin = Num(Get(part, "begin"));
                double partEnd = Num(Get(part, "end"));
                if (partBegin > partEnd)
                    warnings.Add(new YencWarning("invalid_part_range", "begin offset cannot exceed end offset"));
                else if (expectedSize != partEnd - partBegin + 1)
                    warnings.Add(new YencWarning("size_mismatch_part_range", "Specified size does not match part range"));
                else if (partEnd > Num(Get(begin, "size")))
                    warnings.Add(new YencWarning("part_range_exceeds_size", "Specified part range exceeds total file size"));
            }
            else if (Num(Get(begin, "total")) > 1 || Num(Get(begin, "part")) > 1 || Num(Get(end, "part")) > 1)
                warnings.Add(new YencWarning("missing_part_range", "Part range not specified for multi-part post"));

            foreach (var prop in new[] { "pcrc32", "crc32" })
            {
                string v = Get(end, prop);
                if (!string.IsNullOrEmpty(v) && !ReCrc.IsMatch(v))
                    warnings.Add(new YencWarning("invalid_prop_" + prop, "`" + prop + "` is not a valid CRC32 value"));
            }
            if (!Truthy(begin, "part") && Get(begin, "size") != Get(end, "size"))
                warnings.Add(new YencWarning("size_mismatch", "Size specified in begin and end do not match"));
            else if (Num(Get(begin, "size")) < Num(Get(end, "size")))
                warnings.Add(new YencWarning("size_mismatch", "Size specified for part exceeds size specified for whole file"));

            if (ret.DataStart != null)
            {
                var slice = new ReadOnlySpan<byte>(data, ret.DataStart.Value, ret.DataEnd.Value - ret.DataStart.Value);
                ret.Data = Decode(slice, stripDots);
                uint crc = Crc32.Compute(ret.Data);
                ret.Crc32 = crc;
                string hexCrc = Crc32.ToHex(crc);

                if (expectedSize != ret.Data.Length)
                    warnings.Add(new YencWarning("data_size_mismatch", "Decoded data length doesn't match size specified in yend"));
                if (Truthy(end, "pcrc32") && hexCrc != Get(end, "pcrc32").ToLowerInvariant())
                    warnings.Add(new YencWarning("pcrc32_mismatch", "Specified pcrc32 is invalid"));
                if (Truthy(end, "crc32") && part == null && hexCrc != Get(end, "crc32").ToLowerInvariant())
                    warnings.Add(new YencWarning("crc32_mismatch", "Specified crc32 is invalid"));
            }
            else
            {
                if (expectedSize != 0)
                    warnings.Add(new YencWarning("data_size_mismatch", "Decoded data length doesn't match size specified in yend"));
                if (Truthy(end, "pcrc32") && Get(end, "pcrc32") != "00000000")
                    warnings.Add(new YencWarning("pcrc32_mismatch", "Specified pcrc32 is invalid"));
                if (Truthy(end, "crc32") && part == null && Get(end, "crc32") != "00000000")
                    warnings.Add(new YencWarning("crc32_mismatch", "Specified crc32 is invalid"));
            }

            if (warnings.Count > 0) ret.Warnings = warnings;
            return ret;
        }

        /// <summary>Convenience overload accepting a byte array.</summary>
        public static YencPost FromPost(byte[] data, bool stripDots = false) =>
            FromPost(new ReadOnlySpan<byte>(data ?? Array.Empty<byte>()), stripDots);

        private static void CheckNumeric(Dictionary<string, string> d, string[] keys, List<YencWarning> warnings)
        {
            if (d == null) return;
            foreach (var key in keys)
            {
                if (!Truthy(d, key)) continue;
                string val = d[key];
                if (!ReNumber.IsMatch(val))
                    warnings.Add(new YencWarning("invalid_prop_" + key, "`" + key + "` is not a number"));
                else if (val == "0" && key != "size")
                    warnings.Add(new YencWarning("zero_prop_" + key, "`" + key + "` cannot be 0"));
            }
        }

        private static bool IsPrefix(byte[] data, int at, byte[] prefix)
        {
            if (at + prefix.Length > data.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (data[at + i] != prefix[i]) return false;
            return true;
        }
    }

    /// <summary>A non-fatal issue encountered while decoding a yEnc post.</summary>
    public sealed class YencWarning
    {
        public string Code { get; }
        public string Message { get; }
        public YencWarning(string code, string message) { Code = code; Message = message; }
        public override string ToString() => Message;
    }

    /// <summary>The result of <see cref="Yenc.FromPost(byte[], bool)"/>.</summary>
    public sealed class YencPost
    {
        /// <summary>Error code if parsing failed (one of <c>no_start_found</c>, <c>no_end_found</c>, <c>missing_required_properties</c>); otherwise null.</summary>
        public string ErrorCode { get; private set; }
        /// <summary>Human-readable error message, when <see cref="IsError"/>.</summary>
        public string ErrorMessage { get; private set; }
        /// <summary>True if parsing failed.</summary>
        public bool IsError => ErrorCode != null;

        /// <summary>Location of the <c>=ybegin</c> sequence.</summary>
        public int YencStart { get; internal set; }
        /// <summary>Location where the raw yEnc data begins (null for empty posts).</summary>
        public int? DataStart { get; internal set; }
        /// <summary>Location where the raw yEnc data ends (null for empty posts).</summary>
        public int? DataEnd { get; internal set; }

        private int _yencEnd;
        internal bool YencEndSet { get; private set; }
        /// <summary>Location of the end of the <c>=yend</c> line (after the trailing newline).</summary>
        public int YencEnd
        {
            get => _yencEnd;
            internal set { _yencEnd = value; YencEndSet = true; }
        }

        /// <summary>Decoded data (null for empty posts).</summary>
        public byte[] Data { get; internal set; }
        /// <summary>CRC32 of the decoded data (null for empty posts).</summary>
        public uint? Crc32 { get; internal set; }
        /// <summary>Two-level structure of yEnc metadata: line type ("begin"/"part"/"end") -> property -> value.</summary>
        public Dictionary<string, Dictionary<string, string>> Props { get; internal set; }
        /// <summary>Non-fatal issues encountered while decoding (null if none).</summary>
        public List<YencWarning> Warnings { get; internal set; }

        internal static YencPost Error(string code, string message) =>
            new YencPost { ErrorCode = code, ErrorMessage = message };
    }

    /// <summary>
    /// Generates multi-part (or single-part) yEnc posts sequentially. Create via
    /// <see cref="Yenc.MultiPost(string, long, int, int)"/>.
    /// </summary>
    public sealed class YEncoder
    {
        /// <summary>The file's total size.</summary>
        public long Size { get; }
        /// <summary>Number of parts to post.</summary>
        public int Parts { get; }
        /// <summary>Size of each line.</summary>
        public int LineSize { get; }
        /// <summary>Current part number (0 before the first <see cref="Encode(byte[])"/>).</summary>
        public int Part { get; private set; }
        /// <summary>Current position within the file.</summary>
        public long Pos { get; private set; }
        /// <summary>CRC32 of all data fed through <see cref="Encode(byte[])"/> so far.</summary>
        public uint Crc { get; private set; }

        private byte[] _yInfo;       // the shared begin-line tail (" total=.. name=file\r\n" or full "=ybegin .. name=file\r\n")
        private readonly bool _single;
        private bool _done;

        internal YEncoder(string filename, long size, int parts, int lineSize)
        {
            if (lineSize == 0) lineSize = 128;
            Size = size;
            Parts = parts;
            LineSize = lineSize;
            Part = 0;
            Pos = 0;
            Crc = 0;

            var fn = Yenc.FilenameEncoding.GetBytes(CleanFilenameInternal(filename));
            using var ms = new MemoryStream();
            if (parts > 1)
            {
                WriteAscii(ms, " total=" + parts + " line=" + lineSize + " size=" + size + " name=");
                _single = false;
            }
            else
            {
                WriteAscii(ms, "=ybegin line=" + lineSize + " size=" + size + " name=");
                _single = true;
            }
            ms.Write(fn, 0, fn.Length);
            ms.WriteByte((byte)'\r'); ms.WriteByte((byte)'\n');
            _yInfo = ms.ToArray();
        }

        private static string CleanFilenameInternal(string filename)
        {
            filename = filename.Replace("\r\n\0", "");
            if (filename.Length > 256) filename = filename.Substring(0, 256);
            return filename;
        }

        private static void WriteAscii(Stream s, string text)
        {
            for (int i = 0; i < text.Length; i++) s.WriteByte((byte)text[i]);
        }

        private static void WriteEncoded(Stream s, ReadOnlySpan<byte> data, int lineSize)
        {
            if (data.Length == 0) return;
            var buf = new byte[Yenc.MaxSize(data.Length, lineSize)];
            int col = 0;
            int len = Yenc.EncodeDispatch(lineSize, ref col, data, buf, true);
            s.Write(buf, 0, len);
        }

        /// <summary>Encodes the next part and returns the result.</summary>
        public byte[] Encode(ReadOnlySpan<byte> data)
        {
            if (_single) return EncodeSingle(data);

            Part++;
            if (Part > Parts) throw new InvalidOperationException("Exceeded number of specified yEnc parts");
            long end = Pos + data.Length;
            if (end > Size) throw new InvalidOperationException("Exceeded total file size");

            byte[] yInfo = _yInfo;
            uint crc = Crc32.Compute(data);
            string fullCrc = "";
            Crc = Crc32.Combine(Crc, crc, data.Length);
            if (Part == Parts)
            {
                if (end != Size) throw new InvalidOperationException("File size doesn't match total data length");
                fullCrc = " crc32=" + Crc32.ToHex(Crc);
                _yInfo = null;
            }

            using var ms = new MemoryStream();
            WriteAscii(ms, "=ybegin part=" + Part);
            ms.Write(yInfo, 0, yInfo.Length);
            WriteAscii(ms, "=ypart begin=" + (Pos + 1) + " end=" + end + "\r\n");
            WriteEncoded(ms, data, LineSize);
            WriteAscii(ms, "\r\n=yend size=" + data.Length + " part=" + Part + " pcrc32=" + Crc32.ToHex(crc) + fullCrc);

            Pos = end;
            return ms.ToArray();
        }

        /// <summary>Convenience overload accepting a byte array.</summary>
        public byte[] Encode(byte[] data) => Encode(new ReadOnlySpan<byte>(data ?? Array.Empty<byte>()));

        private byte[] EncodeSingle(ReadOnlySpan<byte> data)
        {
            if (_done) throw new InvalidOperationException("Exceeded number of specified yEnc parts");
            if (Size != data.Length) throw new InvalidOperationException("File size doesn't match total data length");
            _done = true;

            Part = 1;
            Pos = data.Length;
            Crc = Crc32.Compute(data);

            byte[] yInfo = _yInfo;
            _yInfo = null;

            using var ms = new MemoryStream();
            ms.Write(yInfo, 0, yInfo.Length);
            WriteEncoded(ms, data, LineSize);
            WriteAscii(ms, "\r\n=yend size=" + data.Length + " crc32=" + Crc32.ToHex(Crc));
            return ms.ToArray();
        }
    }
}
