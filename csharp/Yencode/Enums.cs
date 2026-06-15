namespace Yencode
{
    /// <summary>
    /// The decoder's "previous characters" state, needed by the incremental decoder
    /// because the handling of a character can depend on what preceded it.
    /// Acronyms: CR = carriage return (\r), LF = line feed (\n), EQ = '=', DT = '.'.
    /// Values match the integer codes used by the original native module.
    /// </summary>
    public enum DecoderState
    {
        /// <summary>Default state (after a "\r\n").</summary>
        Crlf = 0,
        /// <summary>Last char was '=' (next byte is escaped).</summary>
        Eq = 1,
        /// <summary>Last char was '\r'.</summary>
        Cr = 2,
        /// <summary>No special preceding context.</summary>
        None = 3,
        /// <summary>Saw "\r\n.".</summary>
        CrlfDt = 4,
        /// <summary>Saw "\r\n.\r".</summary>
        CrlfDtCr = 5,
        /// <summary>Saw "\r\n=" (may be "\r\n.=" in the raw decoder).</summary>
        CrlfEq = 6
    }

    /// <summary>Whether the end of the yEnc data was reached during incremental decoding.</summary>
    public enum DecodeEnd
    {
        /// <summary>End not reached.</summary>
        None = 0,
        /// <summary>"\r\n=y" sequence found (yEnc control line).</summary>
        Control = 1,
        /// <summary>"\r\n.\r\n" sequence found (end of NNTP article).</summary>
        Article = 2
    }

    /// <summary>Identifies which implementation (instruction set) a routine is using.</summary>
    public enum IsaLevel
    {
        /// <summary>Portable scalar implementation.</summary>
        Generic = 0,
        /// <summary>x86 SSE2.</summary>
        Sse2 = 1,
        /// <summary>x86 SSSE3.</summary>
        Ssse3 = 2,
        /// <summary>x86 AVX2.</summary>
        Avx2 = 3,
        /// <summary>x86 carry-less multiply (PCLMULQDQ) — CRC only.</summary>
        Pclmul = 4,
        /// <summary>ARM AdvSimd (NEON).</summary>
        Neon = 5,
        /// <summary>ARM CRC32 instructions — CRC only.</summary>
        ArmCrc = 6
    }
}
