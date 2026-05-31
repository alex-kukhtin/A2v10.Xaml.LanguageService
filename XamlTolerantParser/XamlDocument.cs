using System;
using System.Collections.Generic;

namespace XamlTolerantParser;

public sealed class XamlDocument
{
    public String Source { get; }
    public IReadOnlyList<XamlNode> Roots { get; }
    public IReadOnlyList<XamlDiagnostic> Diagnostics { get; }

    // xmlns view of the document, extracted from root element attributes during Parse.
    // ClrPrefixes maps an xmlns prefix ("" for default) to the (assembly, CLR namespace)
    // it provides. LanguagePrefixes lists prefixes bound to a http(s)://  URL — they
    // enable the XAML language attributes (Name, Key, Class, ...) on that prefix.
    // AssemblyNames is the unique set of assemblies referenced by ClrPrefixes.
    public IReadOnlyDictionary<String, (String asm, String ns)> ClrPrefixes { get; }
    public IReadOnlySet<String> LanguagePrefixes { get; }
    public String[] AssemblyNames { get; }

    // Offsets of the start of every line. _lineStarts[k] is the offset of the
    // first character of line k. Length == number of lines. Populated by the
    // lexer in a single pass; OffsetAt indexes into it.
    private readonly Int32[] _lineStarts;

    public XamlDocument(
        String source,
        IReadOnlyList<XamlNode> roots,
        IReadOnlyList<XamlDiagnostic> diagnostics,
        IReadOnlyDictionary<String, (String asm, String ns)> clrPrefixes,
        IReadOnlySet<String> languagePrefixes,
        String[] assemblyNames,
        Int32[] lineStarts)
    {
        Source = source;
        Roots = roots;
        Diagnostics = diagnostics;
        ClrPrefixes = clrPrefixes;
        LanguagePrefixes = languagePrefixes;
        AssemblyNames = assemblyNames;
        _lineStarts = lineStarts;
    }

    public String TextOf(TextSpan span) =>
        span.Length > 0 ? Source.Substring(span.Start, span.Length) : String.Empty;

    // Returns the most specific node whose span contains (line, column), or null if
    // the position falls outside every root (typical for inter-root whitespace or EOF).
    public XamlNode? FindNodeAt(Int32 line, Int32 column)
    {
        var i = SpanSearch.FindContaining(Roots, line, column, static n => n.Span);
        return i >= 0 ? Roots[i].FindNodeAt(line, column) : null;
    }

    // Structural context for a cursor position — what is being edited
    // (tag name, attribute, value, ...) plus the relevant parse-tree nodes.
    // See XamlPositionContext for the full slot semantics.
    public XamlPositionContext ContextAt(Int32 line, Int32 column) =>
        XamlPositionClassifier.Classify(this, line, column);

    // The on-type auto-edit for a freshly typed trigger character `ch` with the
    // caret at (line, column) — i.e. the character is already in Source, the
    // caret sits right after it. Returns null when the keystroke triggers no
    // edit. Drives LSP onTypeFormatting. See XamlTypingEditor for the rules.
    public XamlTypingEdit? EditAt(Int32 line, Int32 column, Char ch) =>
        XamlTypingEditor.Edit(this, line, column, ch);

    // Converts an LSP (line, column) pair to a source offset. Clamped to document
    // bounds. The line lookup is O(1) via the precomputed line-start table; the
    // remaining work is a bounded scan of that single line to honour the lexer's
    // column-counting rule (\r is swallowed, every other char counts as one column).
    public Int32 OffsetAt(Int32 line, Int32 column)
    {
        if (line < 0 || (line == 0 && column < 0)) return 0;
        var src = Source;
        var len = src.Length;
        if (line >= _lineStarts.Length) return len;

        var lineStart = _lineStarts[line];
        var lineEnd = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : len;

        Int32 col = 0;
        for (Int32 i = lineStart; i < lineEnd; i++)
        {
            if (col == column) return i;
            var c = src[i];
            if (c == '\n') return i; // end of line reached before requested column — clamp
            if (c != '\r') col++;
        }
        return lineEnd;
    }

    // The maximal run of identifier (name) characters around the caret — the token
    // a completion would replace. A pure text scan, independent of the parse tree,
    // so it is immune to the half-open-span quirk where a caret at a token's
    // exclusive end resolves to the parent node. Zero-length at the caret when no
    // name char touches it (a pure insertion). Name chars never span a line, so the
    // walk stays on `line` and column deltas equal offset deltas.
    public TextSpan IdentifierSpanAt(Int32 line, Int32 column)
    {
        var src = Source;
        var caret = OffsetAt(line, column);
        var start = caret;
        while (start > 0 && IsNameChar(src[start - 1])) start--;
        var end = caret;
        while (end < src.Length && IsNameChar(src[end])) end++;
        return new TextSpan(start, end - start, line, column - (caret - start), line, column + (end - caret));
    }

    // The XAML name-character set, shared by the lexer, the position classifier and
    // the markup-extension parser: letters/digits plus the QName punctuation.
    private static Boolean IsNameChar(Char c) =>
        Char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == ':';
}
