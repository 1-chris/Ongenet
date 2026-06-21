using System.Collections.Generic;

namespace Ongenet.Core.Audio.Instruments.Sampler.Sfz;

/// <summary>
/// Lexes pre-processed SFZ text (comments/includes/defines already resolved) into a flat token
/// stream of headers and opcode/value pairs.
/// </summary>
/// <remarks>
/// The non-trivial rule is that an opcode value runs until the next opcode or header, not merely to
/// the next whitespace — so <c>sample=My Piano C4.wav lokey=36</c> yields the value <c>My Piano C4.wav</c>.
/// Whitespace is only a value terminator when the run after it begins a new <c>opcode=</c> or a
/// <c>&lt;header&gt;</c>.
/// </remarks>
public static class SfzTokenizer
{
    public static List<SfzToken> Tokenize(string text)
    {
        var tokens = new List<SfzToken>();
        var n = text.Length;
        var pos = 0;

        while (pos < n)
        {
            if (IsWhitespace(text[pos])) { pos++; continue; }

            // Header: <name>
            if (text[pos] == '<')
            {
                var close = text.IndexOf('>', pos + 1);
                if (close < 0) break; // unterminated header; nothing sensible left to read
                tokens.Add(SfzToken.Header(text.Substring(pos + 1, close - pos - 1).Trim()));
                pos = close + 1;
                continue;
            }

            // Opcode key: identifier characters up to '='.
            var keyStart = pos;
            while (pos < n && IsOpcodeChar(text[pos])) pos++;
            var keyLen = pos - keyStart;

            // Tolerate whitespace around '=' (some files use "cutoff = 1000").
            var afterKey = pos;
            while (afterKey < n && IsWhitespace(text[afterKey])) afterKey++;

            if (keyLen == 0 || afterKey >= n || text[afterKey] != '=')
            {
                // Not an opcode (stray token / symbol); skip one char if we made no progress.
                if (pos == keyStart) pos++;
                continue;
            }

            var key = text.Substring(keyStart, keyLen);
            pos = afterKey + 1; // past '='

            var value = ReadValue(text, ref pos, n);
            tokens.Add(SfzToken.Opcode(key, value));
        }

        return tokens;
    }

    // Reads a value starting at pos, advancing pos to the start of the next token. The value extends
    // through internal whitespace and ends only where a new opcode/header begins (or at end of input).
    private static string ReadValue(string text, ref int pos, int n)
    {
        var start = pos;
        var i = pos;

        while (i < n)
        {
            var ch = text[i];
            if (ch == '<') break; // a header always ends the current value

            if (IsWhitespace(ch))
            {
                // Peek past the whitespace run: does a new opcode/header begin?
                var j = i;
                while (j < n && IsWhitespace(text[j])) j++;
                if (j >= n) break;            // trailing whitespace
                if (text[j] == '<') break;    // header follows

                var k = j;
                while (k < n && IsOpcodeChar(text[k])) k++;
                if (k > j && k < n && text[k] == '=') break; // next opcode follows

                // Otherwise the whitespace belongs to the value (e.g. a space in a filename).
                i = j;
                continue;
            }

            i++;
        }

        pos = i;
        return text.Substring(start, i - start).Trim();
    }

    private static bool IsOpcodeChar(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f' or '\v';
}
