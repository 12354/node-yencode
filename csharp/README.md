# Yencode (managed C# port)

A **fully managed** C# port of [node-yencode](https://github.com/animetosho/node-yencode):
a fast yEnc encoder/decoder and CRC32 (IEEE) implementation. There is **no native
code and no P/Invoke** — acceleration is provided purely via .NET hardware intrinsics
(`System.Runtime.Intrinsics`), with a portable scalar fallback.

## Implementations (scalar **and** SIMD)

Every routine has a verified scalar implementation plus a SIMD-accelerated path that is
selected automatically at runtime and produces byte-for-byte identical results:

| Routine  | Scalar            | SIMD (x86)                          | SIMD (ARM)                  |
|----------|-------------------|-------------------------------------|-----------------------------|
| Encoder  | `do_encode` port  | SSE2 / AVX2 (packed escape scan + add) | AdvSimd/NEON              |
| Decoder  | `do_decode` ports | SSE2 / AVX2 (packed special scan + subtract) | AdvSimd/NEON       |
| CRC32    | slice-by-8 table  | PCLMULQDQ folding                   | ARMv8 CRC32 instructions    |

You can see what was chosen on the current machine:

```csharp
Yenc.EncoderIsa;   // e.g. Avx2
Yenc.DecoderIsa;   // e.g. Avx2
Crc32.IsaLevel;    // e.g. Pclmul
```

The SIMD paths require .NET (Core) 3.0+ intrinsics; the `netstandard2.0` build is
scalar-only.

## API

```csharp
using Yencode;

// ---- raw yEnc ----
byte[] enc = Yenc.Encode(data, lineSize: 128, columnOffset: 0);
int written = Yenc.EncodeTo(data, output, 128);          // output >= Yenc.MaxSize(len, 128)
byte[] dec = Yenc.Decode(enc, stripDots: false);
int dlen  = Yenc.DecodeTo(enc, output, stripDots: false); // in-situ allowed (output may alias data)

int max = Yenc.MaxSize(len, 128);
int min = Yenc.MinSize(len, 128);

// ---- incremental decoding (NNTP streaming + end detection) ----
var state = DecoderState.Crlf;
DecodeEnd end = Yenc.DecodeIncremental(chunk, output, ref state, out int read, out int wrote);
var chunkResult = Yenc.DecodeChunk(chunk, DecoderState.Crlf);   // allocating convenience

// ---- CRC32 (IEEE) ----
uint crc      = Crc32.Compute(data, init: 0);
uint combined = Crc32.Combine(crcA, crcB, lenB);
uint zeroes   = Crc32.Zeroes(count, init: 0);
uint product  = Crc32.Multiply(a, b);
uint shifted  = Crc32.Shift(n, crc);
string hex    = Crc32.ToHex(crc);   // 8-digit lower-case, as used in yEnc trailers

// ---- full posts ----
byte[] post = Yenc.Post("file.bin", data, 128);
var enc2 = Yenc.MultiPost("file.bin", size: 1000, parts: 8, lineSize: 128);
byte[] part1 = enc2.Encode(chunk1); // ... etc; enc2.Crc holds the running CRC

YencPost parsed = Yenc.FromPost(postBytes, stripDots: false);
if (!parsed.IsError) {
    byte[] payload = parsed.Data;          // decoded bytes
    var props = parsed.Props["begin"];     // metadata: begin / part / end
    var warnings = parsed.Warnings;        // non-fatal issues (or null)
}
```

## Building & testing

```bash
dotnet test csharp/Yencode.sln -c Release
```

The test suite ports node-yencode's own reference implementations (the slow JS oracles in
`test/testenc.js` / `test/testdec.js`, plus a bitwise CRC32) and the explicit test vectors,
and additionally cross-checks every SIMD kernel against the scalar one over exhaustive
line-size / offset / random sweeps.

## License

CC0-1.0 (matching the original project).
