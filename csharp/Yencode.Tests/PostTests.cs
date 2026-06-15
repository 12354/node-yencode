using System;
using System.Text;
using Xunit;

namespace Yencode.Tests
{
    public class PostTests
    {
        private static byte[] Ascii(string s) { var b = new byte[s.Length]; for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i]; return b; }
        private static string Str(byte[] b) { var sb = new StringBuilder(b.Length); foreach (var x in b) sb.Append((char)x); return sb.ToString(); }
        private static byte[] Concat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; Array.Copy(a, r, a.Length); Array.Copy(b, 0, r, a.Length, b.Length); return r; }

        [Fact]
        public void Post_Example()
        {
            var result = Yenc.Post("bytes.bin", new byte[] { 0, 1, 2, 3, 4 });
            Assert.Equal("=ybegin line=128 size=5 name=bytes.bin\r\n*+,-.\r\n=yend size=5 crc32=515ad3cc", Str(result));
        }

        [Fact]
        public void MultiPost_Single()
        {
            var enc = Yenc.MultiPost("bytes.bin", 5, 1);
            var r = enc.Encode(new byte[] { 0, 1, 2, 3, 4 });
            Assert.Equal("=ybegin line=128 size=5 name=bytes.bin\r\n*+,-.\r\n=yend size=5 crc32=515ad3cc", Str(r));
            Assert.Equal(0x515ad3ccu, enc.Crc);
        }

        [Fact]
        public void MultiPost_TwoParts()
        {
            var enc = Yenc.MultiPost("bytes.bin", 5, 2);
            var p1 = enc.Encode(new byte[] { 0, 1, 2, 3 });
            Assert.Equal("=ybegin part=1 total=2 line=128 size=5 name=bytes.bin\r\n=ypart begin=1 end=4\r\n*+,-\r\n=yend size=4 part=1 pcrc32=8bb98613", Str(p1));
            var p2 = enc.Encode(new byte[] { 4 });
            Assert.Equal("=ybegin part=2 total=2 line=128 size=5 name=bytes.bin\r\n=ypart begin=5 end=5\r\n=n\r\n=yend size=1 part=2 pcrc32=d56f2b94 crc32=515ad3cc", Str(p2));
            Assert.Equal(0x515ad3ccu, enc.Crc);
        }

        [Fact]
        public void FromPost_Basic()
        {
            string postData = string.Join("\r\n", new[]
            {
                "=ybegin part=5 line=128 size=500000 name=myBinary.dat",
                "=ypart begin=499991 end=500000",
                "..... data",
                "=yend size=10 part=5 pcrc32=97f4bd52",
                ""
            });
            var post = Yenc.FromPost(Ascii(postData));
            Assert.False(post.IsError);
            Assert.Equal(0, post.YencStart);
            Assert.Equal(postData.IndexOf("..."), post.DataStart);
            Assert.Equal(postData.IndexOf("\r\n=yend"), post.DataEnd);
            Assert.Equal(postData.Length, post.YencEnd);

            Assert.Equal("5", post.Props["begin"]["part"]);
            Assert.Equal("128", post.Props["begin"]["line"]);
            Assert.Equal("500000", post.Props["begin"]["size"]);
            Assert.Equal("myBinary.dat", post.Props["begin"]["name"]);
            Assert.Equal("499991", post.Props["part"]["begin"]);
            Assert.Equal("500000", post.Props["part"]["end"]);
            Assert.Equal("10", post.Props["end"]["size"]);
            Assert.Equal("5", post.Props["end"]["part"]);
            Assert.Equal("97f4bd52", post.Props["end"]["pcrc32"]);

            Assert.Null(post.Warnings);
            Assert.Equal(post.DataEnd.Value - post.DataStart.Value, post.Data.Length);
        }

        [Fact]
        public void FromPost_ExtraData_Warnings()
        {
            string postData = string.Join("\r\n", new[]
            {
                "ignored data",
                "=ybegin part=5a some_prop=hello line=0 size=0 name=name with space and = chars",
                ".... data",
                "=yend size=2 pcrc32=invalid pcrc32=invalid invalid_prop",
            });
            var post = Yenc.FromPost(Ascii(postData));
            Assert.False(post.IsError);
            Assert.Equal(postData.IndexOf("=ybegin"), post.YencStart);
            Assert.Equal(postData.IndexOf("..."), post.DataStart);
            Assert.Equal(postData.IndexOf("\r\n=yend"), post.DataEnd);
            Assert.Equal(postData.Length, post.YencEnd);

            Assert.Equal("5a", post.Props["begin"]["part"]);
            Assert.Equal("0", post.Props["begin"]["line"]);
            Assert.Equal("hello", post.Props["begin"]["some_prop"]);
            Assert.Equal("name with space and = chars", post.Props["begin"]["name"]);
            Assert.Equal("invalid", post.Props["end"]["pcrc32"]);
            Assert.Equal(post.DataEnd.Value - post.DataStart.Value, post.Data.Length);

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var w in post.Warnings) seen.Add(w.Code);
            Assert.Contains("ignored_line_data", seen);
            Assert.Contains("duplicate_property", seen);
            Assert.Contains("missing_yend_newline", seen);
            Assert.Contains("invalid_prop_part", seen);
            Assert.Contains("zero_prop_line", seen);
            Assert.Contains("invalid_prop_pcrc32", seen);
            Assert.Contains("size_mismatch", seen);
        }

        [Fact]
        public void FromPost_EmptyPost()
        {
            string postData = string.Join("\r\n", new[]
            {
                "=ybegin size=0 line=1 name=",
                "=yend size=0",
                ""
            });
            var post = Yenc.FromPost(Ascii(postData));
            Assert.False(post.IsError);
            Assert.Equal(0, post.YencStart);
            Assert.Null(post.DataStart);
            Assert.Null(post.DataEnd);
            Assert.Equal(postData.Length, post.YencEnd);
            Assert.Null(post.Data);
            Assert.Null(post.Warnings);
            Assert.Equal("0", post.Props["begin"]["size"]);
            Assert.Equal("1", post.Props["begin"]["line"]);
            Assert.Equal("", post.Props["begin"]["name"]);
            Assert.Equal("0", post.Props["end"]["size"]);
        }

        [Fact]
        public void FromPost_Errors()
        {
            var p1 = Yenc.FromPost(Ascii("invalid post"));
            Assert.True(p1.IsError);
            Assert.Equal("no_start_found", p1.ErrorCode);

            var p2 = Yenc.FromPost(Ascii("=ybegin abc=def"));
            Assert.True(p2.IsError);
            Assert.Equal("no_end_found", p2.ErrorCode);
        }

        [Fact]
        public void Post_RoundTrip_Random()
        {
            var rng = new Random(555);
            for (int round = 0; round < 20; round++)
            {
                int n = rng.Next(0, 5000);
                var data = new byte[n];
                rng.NextBytes(data);
                // post() emits no trailing CRLF after =yend; append one so the parse is clean
                var post = Concat(Yenc.Post("file" + round + ".bin", data, 128), Ascii("\r\n"));
                var parsed = Yenc.FromPost(post);
                Assert.False(parsed.IsError);
                Assert.Equal(data, parsed.Data ?? Array.Empty<byte>());
                Assert.Null(parsed.Warnings);
            }
        }
    }
}
