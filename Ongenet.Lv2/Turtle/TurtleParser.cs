using System;
using System.Collections.Generic;
using System.Text;

namespace Ongenet.Lv2.Turtle;

/// <summary>
/// A small, dependency-free Turtle (RDF 1.1) reader, scoped to what LV2 bundle files use: <c>@prefix</c>
/// / <c>@base</c> (and the SPARQL-style <c>PREFIX</c>/<c>BASE</c>), IRIs and prefixed names, the <c>a</c>
/// keyword, blank-node property lists <c>[ … ]</c> and labels <c>_:x</c>, RDF collections <c>( … )</c>,
/// predicate-object lists (<c>;</c>) and object lists (<c>,</c>), string/number/boolean literals (with
/// <c>^^</c> datatype and <c>@lang</c>), and <c>#</c> comments. Triples are accumulated into a
/// <see cref="TurtleGraph"/>; blank nodes get synthetic ids. Relative IRIs are resolved against
/// <c>@base</c> when one is set, otherwise returned verbatim for the caller to resolve against the
/// bundle directory. This is not a full RDF 1.1 conformance parser.
/// </summary>
public sealed class TurtleParser
{
    private readonly string _s;
    private int _pos;
    private readonly TurtleGraph _graph;
    private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);
    private string? _base;
    private readonly int _scope;

    private TurtleParser(string text, TurtleGraph graph, string? baseIri)
    {
        _s = text;
        _graph = graph;
        _base = baseIri;
        _scope = graph.NextScope();
    }

    /// <summary>Parses <paramref name="text"/> into a fresh graph (optional starting <paramref name="baseIri"/>).</summary>
    public static TurtleGraph Parse(string text, string? baseIri = null)
    {
        var graph = new TurtleGraph();
        ParseInto(text, graph, baseIri);
        return graph;
    }

    /// <summary>Parses <paramref name="text"/> and merges its triples into an existing graph.</summary>
    public static void ParseInto(string text, TurtleGraph graph, string? baseIri = null)
        => new TurtleParser(text, graph, baseIri).Run();

    private void Run()
    {
        SkipWs();
        while (!Eof)
        {
            if (Peek == '@') ParseAtDirective();
            else if (TryKeyword("prefix")) ParsePrefix(expectDot: true);
            else if (TryKeyword("base")) ParseBase(expectDot: true);
            else ParseStatement();

            SkipWs();
        }
    }

    // --- Directives ---

    private void ParseAtDirective()
    {
        Expect('@');
        var word = ReadName();
        if (word.Equals("prefix", StringComparison.OrdinalIgnoreCase)) ParsePrefix(expectDot: true);
        else if (word.Equals("base", StringComparison.OrdinalIgnoreCase)) ParseBase(expectDot: true);
        else throw Error($"Unknown directive @{word}");
    }

    private void ParsePrefix(bool expectDot)
    {
        SkipWs();
        var prefix = ReadName(); // possibly empty (default prefix)
        Expect(':');
        SkipWs();
        var iri = ReadIriRef();
        _prefixes[prefix] = iri;
        FinishDirective(expectDot);
    }

    private void ParseBase(bool expectDot)
    {
        SkipWs();
        _base = ReadIriRef();
        FinishDirective(expectDot);
    }

    private void FinishDirective(bool expectDot)
    {
        SkipWs();
        if (Peek == '.') _pos++; // tolerate both Turtle (.) and SPARQL (no .) forms
    }

    // --- Statements ---

    private void ParseStatement()
    {
        var subject = ParseSubject();
        SkipWs();
        if (Peek == '.') { _pos++; return; } // blank-node-only statement: [ ... ] .
        ParsePredicateObjectList(subject);
        SkipWs();
        Expect('.');
    }

    private Node ParseSubject()
    {
        SkipWs();
        return Peek switch
        {
            '[' => ParseBlankNodePropertyList(),
            '(' => ParseCollection(),
            _ => ParseIriOrPrefixedOrBlank()
        };
    }

    private void ParsePredicateObjectList(Node subject)
    {
        while (true)
        {
            SkipWs();
            var predicate = ParsePredicate();
            ParseObjectList(subject, predicate);
            SkipWs();
            if (Peek != ';') break;
            _pos++;
            SkipWs();
            // A trailing ';' before '.' or ']' (or EOF) is legal and ends the list.
            if (Eof || Peek == '.' || Peek == ']') break;
        }
    }

    private string ParsePredicate()
    {
        SkipWs();
        if (Peek == 'a' && IsBoundary(PeekAt(1)))
        {
            _pos++;
            return Interop.Lv2Api.PredType;
        }

        var node = ParseIriOrPrefixedOrBlank();
        return node.Value; // predicates are always IRIs
    }

    private void ParseObjectList(Node subject, string predicate)
    {
        while (true)
        {
            var obj = ParseObject();
            _graph.Add(subject, predicate, obj);
            SkipWs();
            if (Peek != ',') break;
            _pos++;
        }
    }

    private Node ParseObject()
    {
        SkipWs();
        var c = Peek;
        switch (c)
        {
            case '[': return ParseBlankNodePropertyList();
            case '(': return ParseCollection();
            case '<': return Node.Iri(Resolve(ReadIriRef()));
            case '"':
            case '\'': return ParseStringLiteral();
        }

        if (c == '_' && PeekAt(1) == ':') return ParseBlankLabel();
        if (c == '+' || c == '-' || c == '.' || char.IsDigit(c)) return ParseNumericLiteral();

        // A bareword: 'true'/'false' literals, otherwise a prefixed name.
        if (char.IsLetter(c))
        {
            var save = _pos;
            var word = ReadName();
            if ((word == "true" || word == "false") && Peek != ':')
                return Node.Lit(word, "http://www.w3.org/2001/XMLSchema#boolean");
            _pos = save; // not a boolean; re-read as prefixed name
        }

        return ParseIriOrPrefixedOrBlank();
    }

    // --- Blank nodes & collections ---

    private Node ParseBlankNodePropertyList()
    {
        Expect('[');
        var blank = NewBlank();
        SkipWs();
        if (Peek == ']') { _pos++; return blank; }
        ParsePredicateObjectList(blank);
        SkipWs();
        Expect(']');
        return blank;
    }

    // Parses an RDF collection ( a b c ) into rdf:first/rdf:rest/rdf:nil chain; returns the head.
    private Node ParseCollection()
    {
        Expect('(');
        var items = new List<Node>();
        while (true)
        {
            SkipWs();
            if (Peek == ')') { _pos++; break; }
            items.Add(ParseObject());
        }

        var nil = Node.Iri(Interop.Lv2Api.NsRdf + "nil");
        if (items.Count == 0) return nil;

        var first = Interop.Lv2Api.NsRdf + "first";
        var rest = Interop.Lv2Api.NsRdf + "rest";
        var head = NewBlank();
        var current = head;
        for (var i = 0; i < items.Count; i++)
        {
            _graph.Add(current, first, items[i]);
            var next = i + 1 < items.Count ? NewBlank() : nil;
            _graph.Add(current, rest, next);
            current = next;
        }

        return head;
    }

    private Node ParseBlankLabel()
    {
        Expect('_');
        Expect(':');
        var label = ReadName();
        // Namespace file-scoped labels so the same _:x in two merged files stays distinct.
        return Node.Blank($"_:s{_scope}_{label}");
    }

    private Node NewBlank() => Node.Blank(_graph.NewBlankId());

    // --- IRIs / prefixed names ---

    private Node ParseIriOrPrefixedOrBlank()
    {
        SkipWs();
        if (Peek == '<') return Node.Iri(Resolve(ReadIriRef()));
        if (Peek == '_' && PeekAt(1) == ':') return ParseBlankLabel();
        return ParsePrefixedName();
    }

    private Node ParsePrefixedName()
    {
        var prefix = ReadName();
        Expect(':');
        var local = ReadName();
        if (!_prefixes.TryGetValue(prefix, out var ns))
            throw Error($"Unknown prefix '{prefix}:'");
        return Node.Iri(ns + local);
    }

    private string ReadIriRef()
    {
        Expect('<');
        var sb = new StringBuilder();
        while (!Eof && Peek != '>')
        {
            var c = Next();
            if (c == '\\') // \uXXXX / \UXXXXXXXX escapes
            {
                var e = Next();
                if (e == 'u') sb.Append(ReadHexChar(4));
                else if (e == 'U') sb.Append(ReadHexChar(8));
                else sb.Append(e);
            }
            else sb.Append(c);
        }

        Expect('>');
        return sb.ToString();
    }

    // --- Literals ---

    private Node ParseStringLiteral()
    {
        var quote = Peek;
        var triple = PeekAt(1) == quote && PeekAt(2) == quote;
        var value = triple ? ReadTripleQuoted(quote) : ReadSingleQuoted(quote);

        string? datatype = null;
        string? lang = null;
        if (Peek == '@')
        {
            _pos++;
            lang = ReadLangTag();
        }
        else if (Peek == '^' && PeekAt(1) == '^')
        {
            _pos += 2;
            SkipWs();
            datatype = ParseIriOrPrefixedOrBlank().Value;
        }

        return Node.Lit(value, datatype, lang);
    }

    private string ReadSingleQuoted(char quote)
    {
        Expect(quote);
        var sb = new StringBuilder();
        while (!Eof && Peek != quote)
        {
            var c = Next();
            if (c == '\\') sb.Append(ReadEscape());
            else sb.Append(c);
        }

        Expect(quote);
        return sb.ToString();
    }

    private string ReadTripleQuoted(char quote)
    {
        _pos += 3; // opening triple quote
        var sb = new StringBuilder();
        while (!Eof)
        {
            if (Peek == quote && PeekAt(1) == quote && PeekAt(2) == quote) { _pos += 3; break; }
            var c = Next();
            if (c == '\\') sb.Append(ReadEscape());
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private char ReadEscape()
    {
        var e = Next();
        return e switch
        {
            't' => '\t',
            'n' => '\n',
            'r' => '\r',
            'b' => '\b',
            'f' => '\f',
            '"' => '"',
            '\'' => '\'',
            '\\' => '\\',
            'u' => ReadHexChar(4),
            'U' => ReadHexChar(8),
            _ => e
        };
    }

    private char ReadHexChar(int digits)
    {
        var code = 0;
        for (var i = 0; i < digits; i++) code = code * 16 + HexVal(Next());
        return (char)code;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0
    };

    private string ReadLangTag()
    {
        var sb = new StringBuilder();
        while (!Eof && (char.IsLetterOrDigit(Peek) || Peek == '-')) sb.Append(Next());
        return sb.ToString();
    }

    private Node ParseNumericLiteral()
    {
        var sb = new StringBuilder();
        var isDouble = false;
        var isDecimal = false;
        while (!Eof)
        {
            var c = Peek;
            if (char.IsDigit(c) || c == '+' || c == '-') { sb.Append(Next()); }
            else if (c == '.') { isDecimal = true; sb.Append(Next()); }
            else if (c == 'e' || c == 'E') { isDouble = true; sb.Append(Next()); }
            else break;
        }

        var xsd = "http://www.w3.org/2001/XMLSchema#";
        var datatype = isDouble ? xsd + "double" : isDecimal ? xsd + "decimal" : xsd + "integer";
        return Node.Lit(sb.ToString(), datatype);
    }

    // --- IRI resolution ---

    private string Resolve(string iri)
    {
        if (_base == null || iri.Length == 0) return iri;
        if (iri.Contains("://") || iri.StartsWith("urn:", StringComparison.OrdinalIgnoreCase)) return iri;
        if (Uri.TryCreate(new Uri(_base, UriKind.Absolute), iri, out var resolved)) return resolved.ToString();
        return iri;
    }

    // --- Scanner primitives ---

    private bool Eof => _pos >= _s.Length;
    private char Peek => _pos < _s.Length ? _s[_pos] : '\0';
    private char PeekAt(int offset) => _pos + offset < _s.Length ? _s[_pos + offset] : '\0';
    private char Next() => _s[_pos++];

    private static bool IsBoundary(char c) => c == '\0' || char.IsWhiteSpace(c) || c == ';' || c == ',' || c == '.' || c == ']' || c == ')' || c == '#';

    private void SkipWs()
    {
        while (!Eof)
        {
            var c = Peek;
            if (char.IsWhiteSpace(c)) { _pos++; }
            else if (c == '#') { while (!Eof && Peek != '\n') _pos++; }
            else break;
        }
    }

    // Reads a run of name characters (prefix labels, local names, directive words).
    private string ReadName()
    {
        var start = _pos;
        while (!Eof)
        {
            var c = Peek;
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') _pos++;
            else break;
        }

        return _s.Substring(start, _pos - start);
    }

    // Matches a SPARQL-style keyword (PREFIX/BASE) case-insensitively at the cursor without consuming
    // unless it fully matches as a standalone word.
    private bool TryKeyword(string word)
    {
        if (_pos + word.Length > _s.Length) return false;
        for (var i = 0; i < word.Length; i++)
            if (char.ToLowerInvariant(_s[_pos + i]) != word[i]) return false;
        var after = PeekAt(word.Length);
        if (char.IsLetterOrDigit(after)) return false;
        _pos += word.Length;
        return true;
    }

    private void Expect(char c)
    {
        if (Eof || _s[_pos] != c) throw Error($"Expected '{c}'");
        _pos++;
    }

    private FormatException Error(string message)
    {
        var line = 1;
        for (var i = 0; i < _pos && i < _s.Length; i++) if (_s[i] == '\n') line++;
        return new FormatException($"Turtle parse error at line {line}, offset {_pos}: {message}");
    }
}
